using Aether.Core.Models;

namespace Aether.Core.Services;

public record ChatMessage(string Role, string Content);

public interface ILlmService
{
    string ProviderName { get; }
    bool   IsConfigured { get; }
    Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default);
    IAsyncEnumerable<string> StreamChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null,
        double temperature = 0.7,
        CancellationToken ct = default);
    Task PullModelAsync(string modelId, IProgress<string>? progress = null, CancellationToken ct = default);
    Task DeleteModelAsync(string modelId, CancellationToken ct = default);
}
