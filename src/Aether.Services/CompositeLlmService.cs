using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class CompositeLlmService : ILlmService
{
    private readonly LlamaCppService _llamaCpp;
    private readonly OpenAiService _openAi;
    private readonly ISettingsService _settings;

    public string ProviderName => "Composite";
    public bool   IsConfigured => _llamaCpp.IsConfigured || _openAi.IsConfigured;

    public CompositeLlmService(LlamaCppService llamaCpp, OpenAiService openAi, ISettingsService settings)
    {
        _llamaCpp = llamaCpp; _openAi = openAi; _settings = settings;
    }

    public async Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default)
    {
        var all = new List<LlmModel>();
        if (_settings.Settings.LlamaCppEnabled)
            all.AddRange(await _llamaCpp.GetModelsAsync(ct));
        if (_settings.Settings.OpenAiEnabled && _openAi.IsConfigured)
            all.AddRange(await _openAi.GetModelsAsync(ct));
        return all;
    }

    public IAsyncEnumerable<string> StreamChatAsync(
        string modelId, IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null, double temperature = 0.7,
        CancellationToken ct = default)
    {
        var isOpenAi = modelId.StartsWith("gpt") || modelId.StartsWith("o1") ||
                       modelId.StartsWith("o3") || modelId.StartsWith("o4");
        return isOpenAi
            ? _openAi.StreamChatAsync(modelId, messages, systemPrompt, temperature, ct)
            : _llamaCpp.StreamChatAsync(modelId, messages, systemPrompt, temperature, ct);
    }

    public Task PullModelAsync(string m, IProgress<string>? p = null, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task DeleteModelAsync(string m, CancellationToken ct = default)
        => Task.CompletedTask;
}
