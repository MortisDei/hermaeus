using Aether.Core.Models;
using Aether.Core.Services;
using System.Threading;

namespace Aether.Services;

public sealed class CompositeLlmService : ILlmService, IDisposable
{
    private readonly LlamaCppService _llamaCpp;
    private readonly OpenAiService _openAi;
    private readonly OllamaService _ollama;
    private readonly ISettingsService _settings;
    private readonly RuntimeProfileService _runtimeProfiles;
    private delegate IAsyncEnumerable<LlmStreamEvent> StreamChatDelegate(
        string modelId, IReadOnlyList<ChatMessage> messages, LlmChatOptions? options, CancellationToken ct);

    private readonly Dictionary<string, StreamChatDelegate> _streamByTag;
    private readonly List<LlmModel> _cachedModels = [];
    private readonly Dictionary<string, string> _providerTagsByModelId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReaderWriterLockSlim _cacheLock = new();
    private DateTime _cacheUntilUtc = DateTime.MinValue;

    public string ProviderName => "Composite";
    public bool   IsConfigured => _llamaCpp.IsConfigured || _openAi.IsConfigured
                                   || _runtimeProfiles.Profiles.Any(p => p.Enabled && p.Kind == RuntimeKind.Ollama);

    /// <summary>All providers this composite can route to, with capabilities.</summary>
    public static IReadOnlyList<ProviderDescriptor> Providers { get; } =
    [
        LlamaCppService.Descriptor,
        OllamaService.Descriptor,
        OpenAiService.Descriptor
    ];

    /// <summary>Maps a runtime profile's kind to the provider descriptor it corresponds
    /// to, so UI display strings come from one registry instead of a duplicated switch.</summary>
    public static ProviderDescriptor DescriptorFor(RuntimeKind kind) => kind switch
    {
        RuntimeKind.LlamaCpp => LlamaCppService.Descriptor,
        RuntimeKind.Ollama => OllamaService.Descriptor,
        _ => OpenAiService.Descriptor
    };

    /// <summary>
    /// The single source of truth for "is this provider enabled," so the
    /// per-provider settings flags aren't matched against a provider tag in
    /// more than one place (docs/review/06-technical-debt.md item 4).
    /// </summary>
    public static bool IsProviderEnabled(string tag, AppSettings settings) => tag switch
    {
        "openai" => settings.Llm.OpenAiEnabled,
        "llama.cpp" => settings.Llm.LlamaCppEnabled,
        "ollama" => settings.RuntimeProfiles.Any(p => p.Enabled && p.Kind == RuntimeKind.Ollama),
        _ => false
    };

    /// <summary>Describes the provider a model id routes to.</summary>
    public ProviderDescriptor DescribeModel(string modelId)
    {
        var tag = ResolveProviderTag(modelId);
        if (tag is null && OllamaService.IsOllamaModelId(modelId))
            tag = OllamaService.Descriptor.Tag;
        return Providers.FirstOrDefault(p => string.Equals(p.Tag, tag, StringComparison.OrdinalIgnoreCase))
               ?? LlamaCppService.Descriptor;
    }

    public CompositeLlmService(
        LlamaCppService llamaCpp,
        OpenAiService openAi,
        OllamaService ollama,
        ISettingsService settings,
        RuntimeProfileService runtimeProfiles)
    {
        _llamaCpp = llamaCpp; _openAi = openAi; _ollama = ollama; _settings = settings; _runtimeProfiles = runtimeProfiles;
        _streamByTag = new(StringComparer.OrdinalIgnoreCase)
        {
            [LlamaCppService.Descriptor.Tag] = llamaCpp.StreamChatAsync,
            [OllamaService.Descriptor.Tag] = ollama.StreamChatAsync,
            [OpenAiService.Descriptor.Tag] = openAi.StreamChatAsync
        };
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
        if (IsProviderEnabled(LlamaCppService.Descriptor.Tag, _settings.Settings))
            loads.Add(GetWithTimeoutAsync(_llamaCpp.GetModelsAsync, ct));
        if (IsProviderEnabled(OpenAiService.Descriptor.Tag, _settings.Settings) && _openAi.IsConfigured)
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
        ProbedContextLength = model.ProbedContextLength,
        IsVisible = model.IsVisible,
        Avatar = model.Avatar
    };

    public IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
        string modelId, IReadOnlyList<ChatMessage> messages,
        LlmChatOptions? options = null,
        CancellationToken ct = default)
    {
        var tag = DescribeModel(modelId).Tag;
        var stream = _streamByTag.TryGetValue(tag, out var fn) ? fn : _llamaCpp.StreamChatAsync;
        return stream(modelId, messages, options, ct);
    }

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

    public void Dispose()
    {
        _cacheLock?.Dispose();
    }
}
