namespace Hermaeus.LocalApi;

public sealed record ChatMessageDto(string Role, string Content, string? ReasoningContent = null);

public sealed record ChatCompletionRequest(
    string ModelId,
    List<ChatMessageDto> Messages,
    double? Temperature,
    int? MaxTokens,
    double? TopP = null,
    int? TopK = null,
    double? MinP = null,
    double? RepeatPenalty = null,
    double? FrequencyPenalty = null,
    double? PresencePenalty = null,
    bool Stream = false);

public sealed record ChatCompletionResponse(string Content, int? PromptTokens, int? CompletionTokens, string ReasoningContent = "");

public sealed record MemoryDto(string Id, string Category, string Content, double Importance);

public sealed record MemoryQueryResponse(List<MemoryDto> Memories);

public sealed record RagQueryRequest(string DatasetId, string Question, int? TopK);

public sealed record RagSourceDto(string Title, string File, string Path, float Score);

public sealed record RagQueryResponse(string Answer, List<RagSourceDto> Sources);

public sealed record ModelDto(string Id, string Name, string Provider, int? ContextLength);

public sealed record ModelsResponse(List<ModelDto> Models);

public sealed record EmbeddingsRequest(List<string> Input);

public sealed record EmbeddingItemDto(int Index, float[] Embedding);

public sealed record EmbeddingsResponse(List<EmbeddingItemDto> Data, int Dimensions);

/// <summary>
/// One feature's readiness. <see cref="Reason"/> is empty when the feature is
/// usable and one sentence saying why not when it is not. It never names a
/// path, a key, a token or a dataset: a caller learns whether a request will
/// work, not how this instance is configured.
/// </summary>
public sealed record CapabilityDto(string Name, bool Usable, string Reason);

/// <summary>
/// What this instance can currently serve. Reports, never probes: no model
/// load, no server start, no network call, no embedding pass. A capabilities
/// endpoint that warms a GPU is a denial-of-service handle wearing a health
/// check's name.
/// </summary>
public sealed record CapabilitiesResponse(
    string Version,
    List<string> Routes,
    List<CapabilityDto> Capabilities);

/// <summary>
/// R31's reviewed Agent API wire contract. The routes are deliberately not
/// mapped until Desktop and Local API share one serialized task-mutation owner.
/// </summary>
public static class AgentApiContract
{
    public const int SchemaVersion = 1;
    public const bool ExecutionRoutesAvailable = false;
    public const string ExecutionUnavailableReason =
        "Agent execution is unavailable because Desktop and Local API do not share a single task-mutation owner.";

    public static IReadOnlyList<string> ConditionalRoutes { get; } =
    [
        "POST /v1/agent/tasks",
        "POST /v1/agent/tasks/{id}/start",
        "GET /v1/agent/tasks/{id}",
        "GET /v1/agent/runs/{runId}",
        "POST /v1/agent/tasks/{id}/steer",
        "POST /v1/agent/tasks/{id}/continue",
        "GET /v1/agent/tasks/{id}/output",
        "GET /v1/agent/tasks/{id}/decisions"
    ];
}

public sealed record AgentTaskCreateRequestV1(
    int SchemaVersion,
    string Goal,
    string WorkspaceProfileId,
    string ModelId,
    string ProjectId = "");

public sealed record AgentTaskCreateResponseV1(int SchemaVersion, string TaskId, string Status);

public sealed record AgentTaskStartResponseV1(int SchemaVersion, string TaskId, string RunId, string Status);

public sealed record AgentTaskStatusResponseV1(
    int SchemaVersion,
    string TaskId,
    string Status,
    AgentApiNormalizedOutcomeDto? Outcome,
    string ActiveStep,
    AgentPendingDecisionDto? PendingDecision,
    List<AgentApiLinkDto> Links);

public sealed record AgentRunStatusResponseV1(
    int SchemaVersion,
    string RunId,
    string TaskId,
    string Status,
    AgentApiNormalizedOutcomeDto? Outcome);

public sealed record AgentSteerRequestV1(int SchemaVersion, string Instruction);

public sealed record AgentContinueRequestV1(int SchemaVersion, string Instruction = "");

public sealed record AgentTaskOutputResponseV1(
    int SchemaVersion,
    string TaskId,
    string Status,
    string Report,
    List<string> Reservations,
    List<string> ProvenanceReferences,
    List<AgentArtifactDto> Artifacts);

public sealed record AgentDecisionListResponseV1(
    int SchemaVersion,
    string TaskId,
    List<AgentPendingDecisionDto> Decisions);

public sealed record AgentPendingDecisionDto(
    string Id,
    string Fingerprint,
    string Risk,
    string Reason,
    bool DesktopReviewRequired = true);

public sealed record AgentApiNormalizedOutcomeDto(string Outcome, string EvidenceOrigin, string Summary);

public sealed record AgentApiLinkDto(string Rel, string Href);

public sealed record AgentArtifactDto(string Name, string RelativePath, string Sha256, long SizeBytes);
