namespace Hermaeus.Core.Services;

public sealed record ApplicationLifecyclePhase(
    string Name,
    bool Succeeded,
    long DurationMilliseconds,
    string Error = "");

public sealed record ApplicationStartupResult(
    bool Ready,
    IReadOnlyList<ApplicationLifecyclePhase> Phases)
{
    public bool IsPartial => !Ready && Phases.Any(phase => phase.Succeeded);
}

public sealed record ApplicationShutdownResult(
    bool Clean,
    bool TimedOut,
    IReadOnlyList<ApplicationLifecyclePhase> Phases)
{
    public bool IsIncomplete => !Clean;
}

public interface IApplicationLifecycleParticipant
{
    string Name { get; }

    Task InitializeAsync(CancellationToken ct = default);
}

public sealed class ApplicationLifecycleParticipant : IApplicationLifecycleParticipant
{
    private readonly Func<CancellationToken, Task> _initialize;

    public ApplicationLifecycleParticipant(string name, Func<CancellationToken, Task> initialize)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A participant name is required.", nameof(name)) : name;
        _initialize = initialize ?? throw new ArgumentNullException(nameof(initialize));
    }

    public string Name { get; }

    public Task InitializeAsync(CancellationToken ct = default) => _initialize(ct);
}

public interface IApplicationLifecycleCoordinator
{
    bool IsStopping { get; }

    Task<ApplicationStartupResult> StartAsync(CancellationToken ct = default);

    void RegisterShutdownOwner(string name, Func<CancellationToken, Task> shutdown);

    Task<ApplicationShutdownResult> ShutdownAsync(
        TimeSpan timeout,
        CancellationToken ct = default);
}
