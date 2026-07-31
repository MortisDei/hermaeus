namespace Hermaeus.LocalApi;

public sealed record ChatMessageDto(string Role, string Content);

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

public sealed record ChatCompletionResponse(string Content, int? PromptTokens, int? CompletionTokens);

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
