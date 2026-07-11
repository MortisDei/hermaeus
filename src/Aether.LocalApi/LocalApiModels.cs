namespace Aether.LocalApi;

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
