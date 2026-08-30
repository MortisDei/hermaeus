using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

public interface ILabRuntimeSession : IAsyncDisposable
{
    string OwnershipId { get; }
    int Port { get; }
    bool IsRunning { get; }
    ManagedProcessReference? Process { get; }
    Task StopAsync(CancellationToken ct = default);
}

public sealed record ManagedProcessReference(int ProcessId, DateTime StartedAtUtc);

public interface ILabRuntimeHost
{
    Task<ILabRuntimeSession> StartAsync(
        string runId,
        ServerConfig source,
        LabConfiguration configuration,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> RecoverOwnedProcessesAsync(CancellationToken ct = default);
}

public interface ILabExperimentService
{
    Task<LabExperimentDefinition> CreateDefinitionAsync(
        string name,
        string protocolId,
        ServerConfig source,
        LabConfiguration baseline,
        IReadOnlyList<LabConfiguration> candidates,
        int repetitions,
        LabCorrectnessRequirement correctness,
        CancellationToken ct = default);

    Task<LabRunSnapshot> StartAsync(
        LabExperimentDefinition definition,
        ServerConfig source,
        CancellationToken ct = default);

    Task<LabRunSnapshot> CompleteAsync(
        string runId,
        IReadOnlyList<LabObservation> observations,
        IReadOnlyList<LabOutputEvidence> outputs,
        IReadOnlyList<string>? failures = null,
        CancellationToken ct = default);

    Task<LabRunSnapshot> SwitchConfigurationAsync(
        string runId,
        ServerConfig source,
        string configurationId,
        CancellationToken ct = default);

    Task<LabRunSnapshot> CancelAsync(string runId, CancellationToken ct = default);
    LabRunSnapshot? GetRun(string runId);
    LabApplyReview CreateApplyReview(string runId, string candidateId);
    Task ApplyAsync(LabApplyReview review, CancellationToken ct = default);
}

public interface ILabRecipeService
{
    Task<IReadOnlyList<LabRecipePlan>> InspectAsync(ServerConfig source, CancellationToken ct = default);
    Task<LabRunSnapshot> RunAsync(LabRecipePlan plan, ServerConfig source, string prompt, CancellationToken ct = default);
}
