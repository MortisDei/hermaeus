using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class RuntimeProfileService : IRuntimeProfileService, IDisposable
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly ISettingsService _settings;

    public RuntimeProfileService(ISettingsService settings)
    {
        _settings = settings;
        EnsureDefaults();
    }

    public IReadOnlyList<RuntimeProfile> Profiles
    {
        get
        {
            EnsureDefaults();
            DeduplicateProfiles();
            return _settings.Settings.RuntimeProfiles;
        }
    }

    public async Task SaveAsync(RuntimeProfile profile, CancellationToken ct = default)
    {
        var normalized = NormalizeProfile(profile);
        var existing = _settings.Settings.RuntimeProfiles.FirstOrDefault(p => p.Id == normalized.Id);
        if (existing is null)
            _settings.Settings.RuntimeProfiles.Add(normalized);
        else
        {
            existing.Name = normalized.Name;
            existing.Kind = normalized.Kind;
            existing.BaseUrl = normalized.BaseUrl;
            existing.ApiKey = normalized.ApiKey;
            existing.Enabled = normalized.Enabled;
            existing.StartManagedLlamaServer = normalized.StartManagedLlamaServer;
            existing.LinkedServerId = normalized.LinkedServerId;
        }

        await _settings.SaveAsync();
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        _settings.Settings.RuntimeProfiles.RemoveAll(p => p.Id == id);
        await _settings.SaveAsync();
    }

    public async Task<RuntimeHealth> CheckHealthAsync(RuntimeProfile profile, CancellationToken ct = default)
    {
        try
        {
            var url = profile.Kind switch
            {
                RuntimeKind.Ollama => $"{Trim(profile.BaseUrl)}/api/tags",
                RuntimeKind.LlamaCpp => $"{Trim(profile.BaseUrl)}/health",
                _ => $"{Trim(profile.BaseUrl)}/v1/models"
            };
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(profile.ApiKey))
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", profile.ApiKey);

            var resp = await _http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode
                ? new RuntimeHealth(profile.Id, true, "Healthy")
                : new RuntimeHealth(profile.Id, false, $"{(int)resp.StatusCode} {resp.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            return new RuntimeHealth(profile.Id, false, ex.Message);
        }
    }

    private void EnsureDefaults()
    {
        var profiles = _settings.Settings.RuntimeProfiles;
        DeduplicateProfiles();
        if (profiles.Count > 0) return;

        profiles.Add(new RuntimeProfile
        {
            Name = "llama.cpp local",
            Kind = RuntimeKind.LlamaCpp,
            BaseUrl = _settings.Settings.Llm.LlamaCppBaseUrl,
            Enabled = _settings.Settings.Llm.LlamaCppEnabled,
            StartManagedLlamaServer = true,
            LinkedServerId = _settings.Settings.ManagedServers.FirstOrDefault(s => !s.EmbeddingsMode)?.Id ?? string.Empty
        });
        profiles.Add(new RuntimeProfile
        {
            Name = "Ollama local",
            Kind = RuntimeKind.Ollama,
            BaseUrl = "http://127.0.0.1:11434",
            Enabled = false
        });
        profiles.Add(new RuntimeProfile
        {
            Name = "OpenAI-compatible",
            Kind = RuntimeKind.OpenAiCompatible,
            BaseUrl = _settings.Settings.Llm.OpenAiBaseUrl,
            ApiKey = _settings.Settings.Llm.OpenAiApiKey,
            Enabled = _settings.Settings.Llm.OpenAiEnabled
        });
    }

    private void DeduplicateProfiles()
    {
        var profiles = _settings.Settings.RuntimeProfiles;
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = profiles.Count - 1; i >= 0; i--)
        {
            var normalized = NormalizeProfile(profiles[i]);
            var key = $"{normalized.Kind}|{normalized.Name}|{normalized.BaseUrl}";
            if (!seenIds.Add(normalized.Id) || !seenKeys.Add(key))
                profiles.RemoveAt(i);
        }
    }

    public static RuntimeProfile NormalizeProfile(RuntimeProfile profile) => new()
    {
        Id = string.IsNullOrWhiteSpace(profile.Id) ? Guid.NewGuid().ToString() : profile.Id,
        Name = string.IsNullOrWhiteSpace(profile.Name) ? profile.Kind.ToString() : profile.Name.Trim(),
        Kind = profile.Kind,
        BaseUrl = Trim(profile.BaseUrl),
        ApiKey = profile.ApiKey.Trim(),
        Enabled = profile.Enabled,
        StartManagedLlamaServer = profile.StartManagedLlamaServer,
        LinkedServerId = profile.LinkedServerId.Trim()
    };

    private static string Trim(string url) => string.IsNullOrWhiteSpace(url)
        ? "http://127.0.0.1:8080"
        : url.Trim().TrimEnd('/');

    public void Dispose()
    {
        // HttpClient is static and shared; do not dispose
    }
}
