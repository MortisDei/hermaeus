using Aether.Core.Models;
using Aether.Core.Services;
using System.Threading;

namespace Aether.Services;

public sealed class CompositeLlmService : ILlmService
{
    private const string OpenAiProviderTagValue = "openai";
    private const string LlamaCppProviderTagValue = "llama.cpp";
    private const string OllamaProviderTagValue = "ollama";
    private readonly LlamaCppService _llamaCpp;
    private readonly OpenAiService _openAi;
    private readonly OllamaService _ollama;
    private readonly ISettingsService _settings;
    private readonly IRuntimeProfileService _runtimeProfiles;
    private readonly List<LlmModel> _cachedModels = [];
    private readonly Dictionary<string, string> _providerTagsByModelId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReaderWriterLockSlim _cacheLock = new();
    private DateTime _cacheUntilUtc = DateTime.MinValue;

    public string ProviderName => "Composite";
    public bool   IsConfigured => _llamaCpp.IsConfigured || _openAi.IsConfigured
                                   || _runtimeProfiles.Profiles.Any(p => p.Enabled && p.Kind == RuntimeKind.Ollama);

    public CompositeLlmService(
        LlamaCppService llamaCpp,
        OpenAiService openAi,
        OllamaService ollama,
        ISettingsService settings,
        IRuntimeProfileService runtimeProfiles)
    {
        _llamaCpp = llamaCpp; _openAi = openAi; _ollama = ollama; _settings = settings; _runtimeProfiles = runtimeProfiles;
    }

    public async Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default)
    {
        _cacheLock.EnterReadLock();
        try
        {
            if (_cachedModels.Count > 0 && DateTime.UtcNow < _cacheUntilUtc)
                return _cachedModels.Select(Clone).ToList();
        }
        finally
        {
            _cacheLock.ExitReadLock();
        }

        var all = new List<LlmModel>();
        var loads = new List<Task<List<LlmModel>>>();
        if (_settings.Settings.Llm.LlamaCppEnabled)
            loads.Add(GetWithTimeoutAsync(_llamaCpp.GetModelsAsync, ct));
        if (_settings.Settings.Llm.OpenAiEnabled && _openAi.IsConfigured)
            loads.Add(GetWithTimeoutAsync(_openAi.GetModelsAsync, ct));
        loads.Add(GetWithTimeoutAsync(_ollama.GetModelsAsync, ct));

        var results = await Task.WhenAll(loads);
        foreach (var models in results)
            all.AddRange(models);

        _cacheLock.EnterWriteLock();
        try
        {
            _cachedModels.Clear();
            _cachedModels.AddRange(all.Select(Clone));
            _providerTagsByModelId.Clear();
            foreach (var model in _cachedModels)
            {
                if (!string.IsNullOrWhiteSpace(model.ProviderTag))
                    _providerTagsByModelId[model.Id] = model.ProviderTag;
            }
            _cacheUntilUtc = DateTime.UtcNow.AddSeconds(300);
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }

        return all;
    }

    private static async Task<List<LlmModel>> GetWithTimeoutAsync(
        Func<CancellationToken, Task<List<LlmModel>>> load,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            return await load(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // Provider took too long to respond; return empty list gracefully
            return [];
        }
        catch
        {
            // Other errors (connection issues, etc.) also return empty list
            return [];
        }
    }

    private static LlmModel Clone(LlmModel model) => new()
    {
        Id = model.Id,
        Name = model.Name,
        Provider = model.Provider,
        ProviderTag = model.ProviderTag,
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
        return ResolveProviderTag(modelId) switch
        {
            OpenAiProviderTagValue => _openAi.StreamChatAsync(modelId, messages, systemPrompt, temperature, ct),
            OllamaProviderTagValue => _ollama.StreamChatAsync(modelId, messages, systemPrompt, temperature, ct),
            LlamaCppProviderTagValue => _llamaCpp.StreamChatAsync(modelId, messages, systemPrompt, temperature, ct),
            _ when OllamaService.IsOllamaModelId(modelId) => _ollama.StreamChatAsync(modelId, messages, systemPrompt, temperature, ct),
            _ => _llamaCpp.StreamChatAsync(modelId, messages, systemPrompt, temperature, ct)
        };
    }

    public IAsyncEnumerable<LlmStreamEvent> StreamChatEventsAsync(
        string modelId, IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null, double temperature = 0.7,
        CancellationToken ct = default)
    {
        return ResolveProviderTag(modelId) switch
        {
            OpenAiProviderTagValue => _openAi.StreamChatEventsAsync(modelId, messages, systemPrompt, temperature, ct),
            OllamaProviderTagValue => _ollama.StreamChatEventsAsync(modelId, messages, systemPrompt, temperature, ct),
            LlamaCppProviderTagValue => _llamaCpp.StreamChatEventsAsync(modelId, messages, systemPrompt, temperature, ct),
            _ when OllamaService.IsOllamaModelId(modelId) => _ollama.StreamChatEventsAsync(modelId, messages, systemPrompt, temperature, ct),
            _ => _llamaCpp.StreamChatEventsAsync(modelId, messages, systemPrompt, temperature, ct)
        };
    }

    public Task PullModelAsync(string m, IProgress<string>? p = null, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task DeleteModelAsync(string m, CancellationToken ct = default)
        => Task.CompletedTask;

    private string? ResolveProviderTag(string modelId)
    {
        _cacheLock.EnterReadLock();
        try
        {
            if (_providerTagsByModelId.TryGetValue(modelId, out var tag) && !string.IsNullOrWhiteSpace(tag))
                return tag;

            var model = _cachedModels.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(model?.ProviderTag))
                return model.ProviderTag;

            return null;
        }
        finally
        {
            _cacheLock.ExitReadLock();
        }
    }
}
