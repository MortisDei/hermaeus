namespace Aether.LocalApi;

public sealed record ChatMessageDto(string Role, string Content);

public sealed record ChatCompletionRequest(string ModelId, List<ChatMessageDto> Messages, double? Temperature, int? MaxTokens);

public sealed record ChatCompletionResponse(string Content, int? PromptTokens, int? CompletionTokens);

public sealed record MemoryDto(string Id, string Category, string Content, double Importance);

public sealed record MemoryQueryResponse(List<MemoryDto> Memories);

public sealed record RagQueryRequest(string DatasetId, string Question, int? TopK);

public sealed record RagSourceDto(string Title, string File, string Path, float Score);

public sealed record RagQueryResponse(string Answer, List<RagSourceDto> Sources);

public sealed record ModelDto(string Id, string Name, string Provider, int? ContextLength);

public sealed record ModelsResponse(List<ModelDto> Models);
