using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class CompositeLlmService : ILlmService
{
    private readonly LlamaCppService _llamaCpp;
    private readonly OpenAiService _openAi;
    private readonly ISettingsService _settings;
    private readonly List<LlmModel> _cachedModels = [];
    private DateTime _cacheUntilUtc = DateTime.MinValue;

    public string ProviderName => "Composite";
    public bool   IsConfigured => _llamaCpp.IsConfigured || _openAi.IsConfigured;

    public CompositeLlmService(LlamaCppService llamaCpp, OpenAiService openAi, ISettingsService settings)
    {
        _llamaCpp = llamaCpp; _openAi = openAi; _settings = settings;
    }

    public async Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default)
    {
        if (_cachedModels.Count > 0 && DateTime.UtcNow < _cacheUntilUtc)
            return _cachedModels.Select(Clone).ToList();

        var all = new List<LlmModel>();
        if (_settings.Settings.LlamaCppEnabled)
            all.AddRange(await GetWithTimeoutAsync(_llamaCpp.GetModelsAsync, ct));
        if (_settings.Settings.OpenAiEnabled && _openAi.IsConfigured)
            all.AddRange(await GetWithTimeoutAsync(_openAi.GetModelsAsync, ct));
        _cachedModels.Clear();
        _cachedModels.AddRange(all.Select(Clone));
        _cacheUntilUtc = DateTime.UtcNow.AddSeconds(30);
        return all;
    }

    private static async Task<List<LlmModel>> GetWithTimeoutAsync(
        Func<CancellationToken, Task<List<LlmModel>>> load,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            return await load(timeout.Token);
        }
        catch
        {
            return [];
        }
    }

    private static LlmModel Clone(LlmModel model) => new()
    {
        Id = model.Id,
        Name = model.Name,
        Provider = model.Provider,
        SizeBytes = model.SizeBytes,
        ModifiedAt = model.ModifiedAt,
        ProfileDisplayName = model.ProfileDisplayName,
        Description = model.Description,
        Tags = model.Tags.ToList(),
        DefaultTemperature = model.DefaultTemperature,
        DefaultContextSize = model.DefaultContextSize,
        DefaultMaxTokens = model.DefaultMaxTokens,
        IsVisible = model.IsVisible,
        Avatar = model.Avatar
    };

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
