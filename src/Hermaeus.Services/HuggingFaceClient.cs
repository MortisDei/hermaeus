using System.Security.Cryptography;
using System.Text.Json;

namespace Hermaeus.Services;

public sealed record HfModelCard(string Sha, DateTimeOffset? LastModified, string? License, long? Downloads);

public sealed record HfTreeEntry(string Path, long? SizeBytes, string? LfsSha256);

/// <summary>Explicit source mapping between a model file and a compatible companion.</summary>
public sealed record HfCompanionDeclaration(string ModelPath, string CompanionPath, ModelFileRole Role);

public sealed record HfSearchResult(string RepoId, long Downloads);

/// <summary>
/// Anonymous, read-only client for the subset of the Hugging Face API this app needs:
/// model-card metadata, a repo's file tree (source of the SHA256 update-detection
/// primitive, <c>lfs.oid</c>), and repo search. HTTPS + huggingface.co only, every call is a
/// direct response to a user button press (never on startup or a timer), failures are
/// non-fatal and returned as null/empty for the caller to surface as a status message
/// (r13 03-hugging-face.md; security posture applies to every item in that doc).
/// </summary>
public sealed class HuggingFaceClient
{
    private const string BaseUrl = "https://huggingface.co";
    private static readonly HttpClient DefaultHttp = BuildDefaultClient();
    private readonly HttpClient _http;

    public HuggingFaceClient(HttpClient? http = null)
    {
        _http = http ?? DefaultHttp;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Hermaeus/1.0");
    }

    private static HttpClient BuildDefaultClient() => new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>GET /api/models/{repoId}. Returns null on any failure (network, non-2xx,
    /// malformed JSON) - never throws for a repo that does not resolve.</summary>
    public async Task<HfModelCard?> GetModelCardAsync(string repoId, CancellationToken ct = default)
    {
        ValidateRepoId(repoId);
        try
        {
            using var response = await _http.GetAsync($"{BaseUrl}/api/models/{repoId}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            var sha = root.TryGetProperty("sha", out var shaEl) ? shaEl.GetString() ?? string.Empty : string.Empty;
            DateTimeOffset? lastModified = root.TryGetProperty("lastModified", out var lmEl)
                && DateTimeOffset.TryParse(lmEl.GetString(), out var parsedLm) ? parsedLm : null;
            string? license = root.TryGetProperty("cardData", out var cardEl)
                && cardEl.ValueKind == JsonValueKind.Object
                && cardEl.TryGetProperty("license", out var licenseEl) ? licenseEl.GetString() : null;
            long? downloads = root.TryGetProperty("downloads", out var dlEl) && dlEl.TryGetInt64(out var dl) ? dl : null;

            return new HfModelCard(sha, lastModified, license, downloads);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>GET /api/models/{repoId}/tree/main?recursive=true. Returns null on any failure
    /// (network, non-2xx, malformed JSON) - distinct from a successful call that legitimately
    /// finds zero files, so callers can surface "check failed" instead of "no longer
    /// published" for a transient error.</summary>
    public async Task<IReadOnlyList<HfTreeEntry>?> GetTreeAsync(string repoId, CancellationToken ct = default)
    {
        ValidateRepoId(repoId);
        try
        {
            using var response = await _http.GetAsync($"{BaseUrl}/api/models/{repoId}/tree/main?recursive=true", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return ParseTree(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    internal static List<HfTreeEntry> ParseTree(JsonElement root)
    {
        var entries = new List<HfTreeEntry>();
        if (root.ValueKind != JsonValueKind.Array)
            return entries;

        foreach (var item in root.EnumerateArray())
        {
            var path = item.TryGetProperty("path", out var pathEl) ? pathEl.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrEmpty(path))
                continue;

            long? size = item.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var s) ? s : null;
            string? oid = item.TryGetProperty("lfs", out var lfsEl)
                && lfsEl.ValueKind == JsonValueKind.Object
                && lfsEl.TryGetProperty("oid", out var oidEl) ? oidEl.GetString() : null;

            entries.Add(new HfTreeEntry(path, size, oid));
        }
        return entries;
    }

    /// <summary>
    /// Reads the optional, exact-path companion manifest from the same repository.
    /// The manifest is accepted only when its own Hugging Face LFS SHA256 is present
    /// and matches the downloaded bytes. A filename convention alone is never enough.
    /// </summary>
    public async Task<IReadOnlyList<HfCompanionDeclaration>> GetCompanionMetadataAsync(
        string repoId, IReadOnlyList<HfTreeEntry> tree, CancellationToken ct = default)
    {
        const string manifestPath = ".hermaeus/companions.json";
        var manifestEntry = tree.FirstOrDefault(e => string.Equals(e.Path.Replace('\\', '/'), manifestPath, StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null || string.IsNullOrWhiteSpace(manifestEntry.LfsSha256))
            return [];

        try
        {
            using var response = await _http.GetAsync(ResolveDownloadUrl(repoId, manifestPath), ct);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 256 * 1024)
                return [];

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length > 256 * 1024)
                return [];

            var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (!string.Equals(actual, manifestEntry.LfsSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                return [];

            using var document = JsonDocument.Parse(bytes);
            if (!document.RootElement.TryGetProperty("models", out var models)
                || models.ValueKind != JsonValueKind.Array)
                return [];

            var declarations = new List<HfCompanionDeclaration>();
            foreach (var model in models.EnumerateArray())
            {
                if (!model.TryGetProperty("model_path", out var modelPathElement)
                    || modelPathElement.ValueKind != JsonValueKind.String)
                    continue;

                var modelPath = NormalizeRepoPath(modelPathElement.GetString());
                if (modelPath.Length == 0 || !tree.Any(e => string.Equals(NormalizeRepoPath(e.Path), modelPath, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (!model.TryGetProperty("companions", out var companions)
                    || companions.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var companion in companions.EnumerateArray())
                {
                    if (!companion.TryGetProperty("path", out var pathElement)
                        || pathElement.ValueKind != JsonValueKind.String
                        || !companion.TryGetProperty("role", out var roleElement)
                        || roleElement.ValueKind != JsonValueKind.String)
                        continue;

                    var companionPath = NormalizeRepoPath(pathElement.GetString());
                    var role = roleElement.GetString()?.Trim().ToLowerInvariant() switch
                    {
                        "projector" => ModelFileRole.Projector,
                        "draft_head" => ModelFileRole.DraftHead,
                        _ => (ModelFileRole?)null
                    };
                    var companionEntry = tree.FirstOrDefault(e => string.Equals(NormalizeRepoPath(e.Path), companionPath, StringComparison.OrdinalIgnoreCase));
                    if (role is null || companionPath.Length == 0 || companionEntry is null
                        || string.IsNullOrWhiteSpace(companionEntry.LfsSha256)
                        || string.Equals(modelPath, companionPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    declarations.Add(new HfCompanionDeclaration(modelPath, companionPath, role.Value));
                }
            }

            return declarations
                .Distinct()
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>GET /api/models?search=...&amp;filter=gguf&amp;sort=downloads&amp;limit=25.
    /// Returns an empty list on any failure.</summary>
    public async Task<IReadOnlyList<HfSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseUrl}/api/models?search={Uri.EscapeDataString(query)}&filter=gguf&sort=downloads&limit=25";
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return [];

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return ParseSearch(doc.RootElement);
        }
        catch
        {
            return [];
        }
    }

    internal static List<HfSearchResult> ParseSearch(JsonElement root)
    {
        var results = new List<HfSearchResult>();
        if (root.ValueKind != JsonValueKind.Array)
            return results;

        foreach (var item in root.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrEmpty(id))
                continue;

            var downloads = item.TryGetProperty("downloads", out var dlEl) && dlEl.TryGetInt64(out var dl) ? dl : 0;
            results.Add(new HfSearchResult(id, downloads));
        }
        return results;
    }

    /// <summary>The GGUF-only resolve download URL, the same convention StarterModelCatalog
    /// already uses.</summary>
    public static string ResolveDownloadUrl(string repoId, string repoFilePath)
    {
        ValidateRepoId(repoId);
        var segments = string.Join('/', repoFilePath.Split('/').Select(Uri.EscapeDataString));
        return $"{BaseUrl}/{repoId}/resolve/main/{segments}";
    }

    private static void ValidateRepoId(string repoId)
    {
        if (string.IsNullOrWhiteSpace(repoId))
            throw new ArgumentException("Repo id is required.", nameof(repoId));

        var parts = repoId.Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace) || repoId.Contains(".."))
            throw new ArgumentException("Repo id must look like 'org/repo'.", nameof(repoId));
    }

    private static string NormalizeRepoPath(string? path) => (path ?? string.Empty).Replace('\\', '/').Trim('/');
}
