using System.Diagnostics;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

/// <summary>
/// Coordinates the non-UI application lifecycle shared by Desktop and
/// headless hosts. Subsystems keep their own storage and process ownership;
/// this class only orders them and records the host-level result.
/// </summary>
public sealed class ApplicationLifecycleCoordinator : IApplicationLifecycleCoordinator
{
    private readonly IReadOnlyList<IApplicationLifecycleParticipant> _participants;
    private readonly RecommendationApplicationService _recommendationApplication;
    private readonly ILabRuntimeHost _labRuntime;
    private readonly AppLifecycleJournalService _journal;
    private readonly IRuntimeLogService _logs;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly object _ownerGate = new();
    private readonly List<(string Name, Func<CancellationToken, Task> Shutdown)> _owners = [];
    private ApplicationStartupResult? _startup;
    private Task<ApplicationShutdownResult>? _shutdownTask;
    private CancellationTokenSource? _shutdownCts;

    public ApplicationLifecycleCoordinator(
        IEnumerable<IApplicationLifecycleParticipant> participants,
        RecommendationApplicationService recommendationApplication,
        ILabRuntimeHost labRuntime,
        AppLifecycleJournalService journal,
        IRuntimeLogService logs)
    {
        _participants = participants.ToArray();
        _recommendationApplication = recommendationApplication;
        _labRuntime = labRuntime;
        _journal = journal;
        _logs = logs;
    }

    public bool IsStopping => _shutdownTask is not null || _shutdownCts is not null;

    public async Task<ApplicationStartupResult> StartAsync(CancellationToken ct = default)
    {
        await _startGate.WaitAsync(ct);
        try
        {
            if (_startup is not null)
                return _startup;

            _journal.RecordStartup();
            var phases = new List<ApplicationLifecyclePhase>();
            foreach (var participant in _participants)
                await RunPhaseAsync(phases, participant.Name, participant.InitializeAsync, ct);

            await RunPhaseAsync(
                phases,
                "recommendation reconciliation",
                _ => _recommendationApplication.ReconcileAsync(),
                ct);

            await RunPhaseAsync(
                phases,
                "lab recovery",
                async token =>
                {
                    foreach (var result in await _labRuntime.RecoverOwnedProcessesAsync(token))
                    {
                        _logs.Add(new RuntimeLogEntry(
                            DateTime.UtcNow,
                            result.Contains("Unknown", StringComparison.Ordinal)
                                ? RuntimeLogLevel.Warning
                                : RuntimeLogLevel.Info,
                            RuntimeLogCategory.Service,
                            result));
                    }
                },
                ct);

            var result = new ApplicationStartupResult(
                phases.All(phase => phase.Succeeded),
                phases);
            // A partial startup is an honest result for this attempt, not a
            // successful lifecycle state. Leave the cache empty so a host can
            // retry after repairing the failed participant or its dependency.
            if (result.Ready)
                _startup = result;
            return result;
        }
        finally
        {
            _startGate.Release();
        }
    }

    public void RegisterShutdownOwner(string name, Func<CancellationToken, Task> shutdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(shutdown);

        lock (_ownerGate)
        {
            if (IsStopping)
                throw new InvalidOperationException("Application shutdown has already started.");

            _owners.RemoveAll(owner => string.Equals(owner.Name, name, StringComparison.Ordinal));
            _owners.Add((name, shutdown));
        }
    }

    public Task<ApplicationShutdownResult> ShutdownAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        lock (_ownerGate)
        {
            return _shutdownTask ??= ShutdownCoreAsync(timeout, ct);
        }
    }

    private async Task<ApplicationShutdownResult> ShutdownCoreAsync(TimeSpan timeout, CancellationToken callerToken)
    {
        _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        using var deadline = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token, deadline.Token);
        var phases = new List<ApplicationLifecyclePhase>();

        (string Name, Func<CancellationToken, Task> Shutdown)[] owners;
        lock (_ownerGate)
            // Shutdown unwinds owners in the opposite order from registration:
            // late-started work such as embedding warm-up must stop before the
            // ViewModel owner closes the stores and managed resources it uses.
            owners = _owners.AsEnumerable().Reverse().ToArray();

        foreach (var owner in owners)
        {
            _logs.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Info,
                RuntimeLogCategory.Startup,
                $"Application shutdown owner started: {owner.Name}."));
            await RunPhaseAsync(phases, owner.Name, owner.Shutdown, linked.Token, allowCancellation: true);
            var phase = phases[^1];
            var detail = phase.Succeeded ? "completed" : $"incomplete: {phase.Error}";
            _logs.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                phase.Succeeded ? RuntimeLogLevel.Info : RuntimeLogLevel.Warning,
                RuntimeLogCategory.Startup,
                $"Application shutdown owner {detail}: {owner.Name} ({phase.DurationMilliseconds} ms)."));
        }

        var timedOut = deadline.IsCancellationRequested || callerToken.IsCancellationRequested;
        var clean = !timedOut && phases.All(phase => phase.Succeeded);
        if (clean)
        {
            _journal.RecordCleanExit();
        }
        else
        {
            var reason = timedOut ? "shutdown deadline or cancellation" : "owned shutdown failed";
            _journal.RecordOperation($"incomplete shutdown: {reason}");
            _logs.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Warning,
                RuntimeLogCategory.Startup,
                $"Application shutdown incomplete: reason={reason}."));
        }
        _logs.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            clean ? RuntimeLogLevel.Info : RuntimeLogLevel.Warning,
            RuntimeLogCategory.Startup,
            $"Application shutdown result: clean={clean}, timedOut={timedOut}, owners={phases.Count}."));

        return new ApplicationShutdownResult(clean, timedOut, phases);
    }

    private static async Task RunPhaseAsync(
        ICollection<ApplicationLifecyclePhase> phases,
        string name,
        Func<CancellationToken, Task> action,
        CancellationToken ct,
        bool allowCancellation = false)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            if (!allowCancellation)
            {
                await action(ct);
            }
            else
            {
                var actionTask = action(ct);
                var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, ct);
                if (await Task.WhenAny(actionTask, cancellationTask) != actionTask)
                {
                    // An owner that ignores cancellation must not hold the
                    // shutdown caller past its deadline. It remains running
                    // outside this result and its exception is observed here
                    // so an incomplete shutdown cannot become an unobserved
                    // process-level fault.
                    _ = actionTask.ContinueWith(
                        completed => _ = completed.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    phases.Add(new ApplicationLifecyclePhase(name, false, timer.ElapsedMilliseconds, "Shutdown owner did not stop before the deadline."));
                    return;
                }

                await actionTask;
            }
            phases.Add(new ApplicationLifecyclePhase(name, true, timer.ElapsedMilliseconds));
        }
        catch (OperationCanceledException) when (allowCancellation && ct.IsCancellationRequested)
        {
            phases.Add(new ApplicationLifecyclePhase(name, false, timer.ElapsedMilliseconds, "Cancelled."));
        }
        catch (Exception ex)
        {
            phases.Add(new ApplicationLifecyclePhase(name, false, timer.ElapsedMilliseconds, ex.Message));
        }
    }
}
