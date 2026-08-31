using System.Security.Cryptography;
using System.Text.Json;
using System.Net.Http.Headers;

namespace Hermaeus.Services;

public sealed record HfModelCard(
    string Sha,
    DateTimeOffset? LastModified,
    string? License,
    long? Downloads,
    string? Thumbnail = null);

public sealed record HfTreeEntry(string Path, long? SizeBytes, string? LfsSha256, string Revision = "");

/// <summary>Explicit source mapping between a model file and a compatible companion.</summary>
public sealed record HfCompanionDeclaration(
    string ModelPath,
    string CompanionPath,
    ModelFileRole Role,
    HfCompanionEvidence Evidence = HfCompanionEvidence.ExplicitManifest,
    string EvidenceDetail = "")
{
    public bool AutoSelect => Evidence != HfCompanionEvidence.ReviewRequired;
}

public enum HfCompanionEvidence
{
    ExplicitManifest,
    VerifiedGgufMetadata,
    ReviewRequired
}

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
            string? thumbnail = root.TryGetProperty("cardData", out cardEl)
                && cardEl.ValueKind == JsonValueKind.Object
                && cardEl.TryGetProperty("thumbnail", out var thumbnailEl)
                && thumbnailEl.ValueKind == JsonValueKind.String
                && IsBoundedThumbnail(thumbnailEl.GetString())
                ? thumbnailEl.GetString()
                : null;
            long? downloads = root.TryGetProperty("downloads", out var dlEl) && dlEl.TryGetInt64(out var dl) ? dl : null;

            return new HfModelCard(sha, lastModified, license, downloads, thumbnail);
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
    public async Task<IReadOnlyList<HfTreeEntry>?> GetTreeAsync(
        string repoId, string revision = "main", CancellationToken ct = default)
    {
        ValidateRepoId(repoId);
        ValidateRevision(revision);
        try
        {
            using var response = await _http.GetAsync($"{BaseUrl}/api/models/{repoId}/tree/{Uri.EscapeDataString(revision)}?recursive=true", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return ParseTree(doc.RootElement).Select(entry => entry with { Revision = revision }).ToList();
        }
        catch
        {
            return null;
        }
    }

    public Task<IReadOnlyList<HfTreeEntry>?> GetTreeAsync(string repoId, CancellationToken ct) =>
        GetTreeAsync(repoId, "main", ct);

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
        string repoId, IReadOnlyList<HfTreeEntry> tree, string revision = "main", CancellationToken ct = default)
    {
        const string manifestPath = ".hermaeus/companions.json";
        var manifestEntry = tree.FirstOrDefault(e => string.Equals(e.Path.Replace('\\', '/'), manifestPath, StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null || string.IsNullOrWhiteSpace(manifestEntry.LfsSha256))
            return [];

        try
        {
            using var response = await _http.GetAsync(ResolveDownloadUrl(repoId, manifestPath, revision), ct);
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

    public Task<IReadOnlyList<HfCompanionDeclaration>> GetCompanionMetadataAsync(
        string repoId, IReadOnlyList<HfTreeEntry> tree, CancellationToken ct) =>
        GetCompanionMetadataAsync(repoId, tree, "main", ct);

    /// <summary>
    /// Resolves explicit mappings first, then examines conventional existing layouts as
    /// candidates. A candidate is auto-selectable only after a bounded GGUF header probe
    /// supplies deterministic role and compatibility evidence. Layout candidates that do not
    /// meet that bar remain visible to the user but are never selected implicitly.
    /// </summary>
    public async Task<IReadOnlyList<HfCompanionDeclaration>> ResolveCompanionDeclarationsAsync(
        string repoId,
        IReadOnlyList<HfTreeEntry> tree,
        string modelPath,
        GgufModelInfo? modelMetadata,
        string revision = "main",
        CancellationToken ct = default)
    {
        var explicitMappings = await GetCompanionMetadataAsync(repoId, tree, revision, ct);
        var result = explicitMappings.ToList();
        var explicitPaths = explicitMappings
            .Where(mapping => string.Equals(NormalizeRepoPath(mapping.ModelPath), NormalizeRepoPath(modelPath), StringComparison.OrdinalIgnoreCase))
            .Select(mapping => NormalizeRepoPath(mapping.CompanionPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = FindLayoutCandidates(tree, modelPath)
            .Where(candidate => !explicitPaths.Contains(NormalizeRepoPath(candidate.Entry.Path)))
            .ToList();
        if (candidates.Count == 0)
            return result;

        var metadata = new List<(LayoutCandidate Candidate, GgufModelInfo? Info)>();
        foreach (var candidate in candidates)
        {
            var info = await GetGgufMetadataAsync(repoId, candidate.Entry.Path, candidate.Entry.LfsSha256, revision, ct);
            metadata.Add((candidate, info));
        }

        foreach (var group in metadata.GroupBy(item => item.Candidate.Role))
        {
            var autoCompatible = group
                .Where(item => IsAutoCompatible(
                    item.Candidate.Role, repoId, modelPath, revision, modelMetadata, item.Info, group.Count()))
                .ToList();
            var canAutoSelect = autoCompatible.Count == 1;
            foreach (var (candidate, info) in group)
            {
                var auto = canAutoSelect && autoCompatible[0].Candidate.Entry.Path.Equals(candidate.Entry.Path, StringComparison.OrdinalIgnoreCase);
                result.Add(new HfCompanionDeclaration(
                    modelPath,
                    candidate.Entry.Path,
                    candidate.Role,
                    auto ? HfCompanionEvidence.VerifiedGgufMetadata : HfCompanionEvidence.ReviewRequired,
                    auto
                        ? BuildEvidence(candidate.Role, candidate.Entry, info, revision)
                        : "Same-repository layout candidate; review its model compatibility before selecting it."));
            }
        }

        return result;
    }

    /// <summary>Reads only a bounded GGUF header from a repository file. This is metadata
    /// evidence for selection, not an integrity substitute: the later full download still
    /// verifies the complete file against the tree's LFS SHA256.</summary>
    public async Task<GgufModelInfo?> GetGgufMetadataAsync(
        string repoId,
        string repoFilePath,
        string? expectedSha256,
        string revision = "main",
        CancellationToken ct = default)
    {
        if (!IsSha256(expectedSha256))
            return null;

        const int maxProbeBytes = 8 * 1024 * 1024;
        ValidateRepoId(repoId);
        ValidateRevision(revision);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ResolveDownloadUrl(repoId, repoFilePath, revision));
            request.Headers.Range = new RangeHeaderValue(0, maxProbeBytes - 1);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            if (response.StatusCode == System.Net.HttpStatusCode.PartialContent
                && response.Content.Headers.ContentRange?.From is not 0)
                return null;
            if (response.Content.Headers.ContentLength is > maxProbeBytes)
                return null;

            await using var content = await response.Content.ReadAsStreamAsync(ct);
            var bytes = await ReadAtMostAsync(content, maxProbeBytes, ct);
            return bytes.Length > 0 && bytes.Length <= maxProbeBytes
                ? GgufMetadataReader.TryRead(bytes)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private sealed record LayoutCandidate(HfTreeEntry Entry, ModelFileRole Role);

    private static IReadOnlyList<LayoutCandidate> FindLayoutCandidates(
        IReadOnlyList<HfTreeEntry> tree, string modelPath)
    {
        var model = NormalizeRepoPath(modelPath);
        var modelDirectory = DirectoryOf(model);
        return tree
            .Where(entry => IsSha256(entry.LfsSha256)
                && entry.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(NormalizeRepoPath(entry.Path), model, StringComparison.OrdinalIgnoreCase))
            .Select(entry =>
            {
                var path = NormalizeRepoPath(entry.Path);
                var fileName = Path.GetFileName(path);
                var directory = DirectoryOf(path);
                var role = string.Equals(directory, modelDirectory, StringComparison.OrdinalIgnoreCase)
                    && fileName.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase)
                    ? ModelFileRole.Projector
                    : IsMtpCandidate(directory, fileName, modelDirectory)
                        ? ModelFileRole.DraftHead
                        : (ModelFileRole?)null;
                return role is { } value ? new LayoutCandidate(entry, value) : null;
            })
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .ToList();
    }

    private static bool IsMtpCandidate(string directory, string fileName, string modelDirectory) =>
        string.Equals(directory, CombineRepoPath(modelDirectory, "MTP"), StringComparison.OrdinalIgnoreCase)
        || (fileName.StartsWith("mtp", StringComparison.OrdinalIgnoreCase)
            && string.Equals(directory, modelDirectory, StringComparison.OrdinalIgnoreCase));

    private static bool IsAutoCompatible(
        ModelFileRole role,
        string repoId,
        string modelPath,
        string revision,
        GgufModelInfo? model,
        GgufModelInfo? companion,
        int candidateCount)
    {
        if (model is null || companion is null || candidateCount != 1 || !IsImmutableRevision(revision))
            return false;

        if (role == ModelFileRole.Projector)
        {
            return string.Equals(companion.GeneralType, "clip", StringComparison.OrdinalIgnoreCase)
                && (HasExactTargetBinding(companion, repoId, modelPath, model)
                    || IsKnownVisionArchitecture(model.Architecture));
        }

        return role == ModelFileRole.DraftHead
            && companion.NextnPredictLayers is > 0
            && string.Equals(model.Architecture, companion.Architecture, StringComparison.OrdinalIgnoreCase)
            && model.VocabularySize is not null
            && model.VocabularySize == companion.VocabularySize
            && model.TokenizerIdentity.Length > 0
            && string.Equals(model.TokenizerIdentity, companion.TokenizerIdentity, StringComparison.Ordinal);
    }

    private static bool HasExactTargetBinding(
        GgufModelInfo companion, string repoId, string modelPath, GgufModelInfo model)
    {
        var targetValues = new[] { repoId, modelPath, model.Name, model.RepositoryUrl }
            .Select(NormalizeBinding)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var companionValues = new[] { companion.BaseModelName, companion.BaseModelRepositoryUrl }
            .Select(NormalizeBinding)
            .Where(value => value.Length > 0);
        return companionValues.Any(targetValues.Contains);
    }

    private static bool IsKnownVisionArchitecture(string architecture) =>
        architecture.Trim().ToLowerInvariant() is
            "gemma3" or "gemma4" or "llama4" or "qwen2vl" or "qwen2_5_vl"
            or "qwen2.5_vl" or "mllama" or "pixtral" or "minicpmv"
            or "internvl" or "smolvlm" or "molmo" or "phi3v";

    private static string NormalizeBinding(string value) =>
        value.Trim().TrimEnd('/').Replace("https://huggingface.co/", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://huggingface.co/", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static string BuildEvidence(ModelFileRole role, HfTreeEntry entry, GgufModelInfo? info, string revision) =>
        role == ModelFileRole.Projector
            ? $"Same repository revision {revision} contains one LFS-identified GGUF projector with general.type=clip."
            : $"Same repository revision {revision} contains one LFS-identified MTP head with matching architecture, vocabulary, tokenizer, and nextn metadata.";

    private static async Task<byte[]> ReadAtMostAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (buffer.Length <= maxBytes)
        {
            var read = await stream.ReadAsync(chunk, ct);
            if (read == 0)
                break;
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
            if (buffer.Length > maxBytes)
                break;
        }

        return buffer.ToArray();
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
    public static string ResolveDownloadUrl(string repoId, string repoFilePath, string revision = "main")
    {
        ValidateRepoId(repoId);
        ValidateRevision(revision);
        var segments = string.Join('/', repoFilePath.Split('/').Select(Uri.EscapeDataString));
        return $"{BaseUrl}/{repoId}/resolve/{Uri.EscapeDataString(revision)}/{segments}";
    }

    private static void ValidateRepoId(string repoId)
    {
        if (string.IsNullOrWhiteSpace(repoId))
            throw new ArgumentException("Repo id is required.", nameof(repoId));

        var parts = repoId.Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace) || repoId.Contains(".."))
            throw new ArgumentException("Repo id must look like 'org/repo'.", nameof(repoId));
    }

    private static void ValidateRevision(string revision)
    {
        if (string.IsNullOrWhiteSpace(revision) || revision.Contains('/') || revision.Contains('\\') || revision.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Revision must be a non-empty repository revision.", nameof(revision));
    }

    private static string NormalizeRepoPath(string? path) => (path ?? string.Empty).Replace('\\', '/').Trim('/');

    private static string DirectoryOf(string path)
    {
        var normalized = NormalizeRepoPath(path);
        var index = normalized.LastIndexOf('/');
        return index < 0 ? string.Empty : normalized[..index];
    }

    private static string CombineRepoPath(string directory, string child) =>
        string.IsNullOrWhiteSpace(directory) ? child : $"{directory}/{child}";

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsImmutableRevision(string revision) =>
        revision.Length == 40 && revision.All(Uri.IsHexDigit);

    private static bool IsBoundedThumbnail(string? value) =>
        value is not null && System.Text.Encoding.UTF8.GetByteCount(value) <= 2048;
}
