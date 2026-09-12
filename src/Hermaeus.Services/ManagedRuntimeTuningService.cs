using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services.ProcessManagement;

namespace Hermaeus.Services;

/// <summary>
/// Owns the one managed-runtime tuning operation used by Services and Models.
/// Candidate probing is serialized, admission-backed, and performed through
/// the same process manager used for an ordinary server start. A tune result is
/// useful only after the candidate process has been stopped and its allocation
/// released.
/// </summary>
public sealed class ManagedRuntimeTuningService : IManagedRuntimeTuningService
{
    private readonly IManagedRuntimeProcessFactory _runtimeFactory;
    private readonly IResourceCoordinator _resourceCoordinator;
    private readonly AdaptiveInferenceExperienceService _experience;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public ManagedRuntimeTuningService(
        IManagedRuntimeProcessFactory runtimeFactory,
        IResourceCoordinator resourceCoordinator,
        AdaptiveInferenceExperienceService experience)
    {
        _runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
        _resourceCoordinator = resourceCoordinator ?? throw new ArgumentNullException(nameof(resourceCoordinator));
        _experience = experience ?? throw new ArgumentNullException(nameof(experience));
    }

    public bool IsBusy => _operationGate.CurrentCount == 0;

    public async Task<ServerTuneResult> RunAsync(
        ServerConfig config,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        GgufModelInfo? ggufInfo = null,
        HardwareProfile? hardware = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!await _operationGate.WaitAsync(0, ct))
            throw new InvalidOperationException("Another managed-runtime auto-tune operation is already in progress.");

        var operationId = $"tune-{Guid.NewGuid():N}";
        try
        {
            progress?.Report($"[hermaeus] Auto-tune operation {operationId} acquired the shared runtime owner.");
            return await ServerProcessManager.AutoTuneWithProbeAsync(
                config,
                progress,
                ct,
                portOwnerLookup: null,
                ggufInfo,
                hardware,
                (candidate, requestedLayers, report, token) =>
                    ProbeCandidateAsync(operationId, candidate, requestedLayers, report, token, ggufInfo, hardware));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<ProbeResult> ProbeCandidateAsync(
        string operationId,
        ServerConfig candidate,
        int requestedLayers,
        IProgress<string>? progress,
        CancellationToken ct,
        GgufModelInfo? ggufInfo,
        HardwareProfile? hardware)
    {
        var consumerId = $"tune.{SafeIdentity(candidate.Id)}";
        var allocationId = $"tune-allocation-{Guid.NewGuid():N}";
        IResourceAdmissionLease? lease = null;
        ServerProcessManager? manager = null;
        ServerLaunchResult? launch = null;
        ResourceWorkloadPlan? workload = null;
        RuntimeIdentityV2? runtime = null;
        ModelIdentityV2? model = null;
        string? configurationIdentity = null;
        var evidenceAttempted = false;

        try
        {
            _resourceCoordinator.RegisterConsumer(ResourceAllocationFactory.ManagedServerConsumer(candidate, consumerId));
            var proposal = ResourceAllocationFactory.ManagedServerProposal(
                candidate,
                consumerId,
                allocationId,
                attemptId: $"{operationId}-{requestedLayers}");
            var request = new ResourceAdmissionRequest(
                consumerId,
                proposal,
                AdaptiveInferencePlanner.HeadroomPolicy(candidate),
                callerId: operationId,
                allowUnknown: true);

            // AcquireAsync intentionally takes a fresh snapshot, but a
            // cancellation or admission refusal can happen before it returns
            // a lease. Keep a pre-admission plan so those attempts still have
            // a durable, path-free workload identity and are not silently
            // absent from the tuning evidence trail.
            workload = await _resourceCoordinator.PlanAsync(request, CancellationToken.None);
            runtime = await CreateRuntimeIdentityForEvidenceAsync(candidate.ExecutablePath, ct);
            model = RuntimeIdentityFactory.CreateModelIdentity(candidate.ModelPath, ggufInfo);
            configurationIdentity = ConfigurationIdentityFactory.Create(candidate).StableId;
            ct.ThrowIfCancellationRequested();

            lease = await _resourceCoordinator.AcquireAsync(request, ct);
            workload = lease.Plan;
            progress?.Report($"[hermaeus] Auto-tune candidate {requestedLayers}: {FormatPlan(lease.Plan)}");

            manager = _runtimeFactory.Create();
            await manager.StartAsync(candidate, lease, ct);
            launch = manager.LastLaunchResult;
            runtime = launch.EffectiveLaunch?.RuntimeIdentity
                ?? runtime;

            var result = launch.FailureKind == ServerLaunchFailureKind.None
                && manager.Status == ServerStatus.Running
                ? ProbeResult.Ok(BuildTuneResult(manager, requestedLayers, candidate.Threads))
                : ProbeResult.Failed(BuildFailure(requestedLayers, launch, manager));

            evidenceAttempted = true;
            await RecordOutcomeAsync(workload, runtime, model, configurationIdentity, requestedLayers, candidate, launch, result, progress);
            if (ct.IsCancellationRequested)
                ct.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException)
        {
            if (!evidenceAttempted && workload is not null && runtime is not null && model is not null && configurationIdentity is not null)
            {
                var cancelled = ProbeResult.Failed($"Candidate {requestedLayers} was cancelled.");
                evidenceAttempted = true;
                await RecordOutcomeAsync(
                    workload,
                    runtime,
                    model,
                    configurationIdentity,
                    requestedLayers,
                    candidate,
                    launch ?? new ServerLaunchResult(
                        ServerStatus.Stopped,
                        ServerLaunchFailureKind.Cancelled,
                        null,
                        cancelled.Error),
                    cancelled,
                    progress);
            }
            throw;
        }
        catch (ResourceAdmissionException ex)
        {
            var message = $"Candidate {requestedLayers} was refused by resource admission ({ex.Plan.Feasibility}).";
            progress?.Report($"[hermaeus] {message}");
            workload = ex.Plan;
            if (!evidenceAttempted && runtime is not null && model is not null && configurationIdentity is not null)
            {
                evidenceAttempted = true;
                await RecordOutcomeAsync(
                    workload,
                    runtime,
                    model,
                    configurationIdentity,
                    requestedLayers,
                    candidate,
                    new ServerLaunchResult(ServerStatus.Stopped, ServerLaunchFailureKind.ResourceExhaustion, null, message),
                    ProbeResult.Failed(message),
                    progress);
            }
            return ProbeResult.Failed(message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            var message = $"Candidate {requestedLayers} could not be prepared: {ex.Message}";
            progress?.Report($"[hermaeus] {message}");
            if (!evidenceAttempted && workload is not null && runtime is not null && model is not null && configurationIdentity is not null)
            {
                evidenceAttempted = true;
                await RecordOutcomeAsync(
                    workload,
                    runtime,
                    model,
                    configurationIdentity,
                    requestedLayers,
                    candidate,
                    launch ?? new ServerLaunchResult(ServerStatus.Error, ClassifyPreparationFailure(ex), null, message),
                    ProbeResult.Failed(message),
                    progress);
            }
            return ProbeResult.Failed(message);
        }
        finally
        {
            if (manager is not null)
            {
                try { await manager.StopAsync(); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
                {
                    progress?.Report($"[hermaeus] Auto-tune candidate cleanup failed: {ex.Message}");
                }
                manager.Dispose();
            }

            if (lease is not null && !lease.IsCompleted && !lease.IsReleased)
                await lease.ReleaseAsync("auto-tune candidate cleanup");
        }
    }

    private async Task RecordOutcomeAsync(
        ResourceWorkloadPlan workload,
        RuntimeIdentityV2 runtime,
        ModelIdentityV2 model,
        string configurationIdentity,
        int requestedLayers,
        ServerConfig candidate,
        ServerLaunchResult launch,
        ProbeResult result,
        IProgress<string>? progress)
    {
        try
        {
            var candidateId = $"tune-{requestedLayers}";
            var changedFields = new List<string> { "GPU placement" };
            if (candidate.ContextSize > 0)
                changedFields.Add("context");
            await _experience.RecordAsync(
                workload,
                runtime,
                model,
                configurationIdentity,
                candidateId,
                changedFields,
                launch,
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
        {
            progress?.Report($"[hermaeus] Auto-tune evidence persistence failed: {ex.Message}");
        }
    }

    private static ServerTuneResult BuildTuneResult(
        ServerProcessManager manager,
        int requestedLayers,
        int threads)
    {
        var log = manager.GetLog();
        int? observedLayers = null;
        int? totalLayers = null;
        foreach (var line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parsed = ServerProcessManager.ParseGpuLayerLog(line);
            observedLayers = parsed.Used ?? observedLayers;
            totalLayers = parsed.Total ?? totalLayers;
        }

        return new(
            observedLayers ?? requestedLayers,
            totalLayers,
            threads,
            ServerProcessManager.ParseLlamaBuildLabel(log),
            log);
    }

    private static string BuildFailure(
        int requestedLayers,
        ServerLaunchResult? launch,
        ServerProcessManager manager) =>
        $"Candidate {requestedLayers} failed ({launch?.FailureKind ?? ServerLaunchFailureKind.Unknown}): "
        + $"{launch?.ErrorMessage ?? manager.ErrorMessage}\nRecent log:\n{manager.GetLog()}";

    private static async Task<RuntimeIdentityV2> CreateRuntimeIdentityForEvidenceAsync(
        string executablePath,
        CancellationToken ct)
    {
        try
        {
            return await RuntimeIdentityFactory.CreateRuntimeIdentityAsync(executablePath, null, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The cancellation is recorded against the workload below. An
            // incomplete runtime identity remains useful evidence and must
            // not turn a cancelled candidate into an unrecorded attempt.
            return RuntimeIdentityFactory.Unknown("llama.cpp");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return RuntimeIdentityFactory.Unknown("llama.cpp");
        }
    }

    private static ServerLaunchFailureKind ClassifyPreparationFailure(Exception ex) =>
        ex is FileNotFoundException or DirectoryNotFoundException
            ? ServerLaunchFailureKind.RuntimeUnavailable
            : ServerLaunchFailureKind.Unknown;

    private static string FormatPlan(ResourceWorkloadPlan plan)
    {
        var unknown = plan.UnknownComponents.Count == 0 ? "none" : $"{plan.UnknownComponents.Count} Unknown";
        return $"workload {plan.Feasibility}, {unknown} component(s), snapshot {plan.SnapshotId}";
    }

    private static string SafeIdentity(string value)
    {
        var chars = (string.IsNullOrWhiteSpace(value) ? "server" : value.Trim())
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_')
            .ToArray();
        var safe = new string(chars);
        return safe.Length <= 64 ? safe : safe[..64];
    }
}
