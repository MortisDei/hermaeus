using System.Text.Json;
using System.Text.Json.Serialization;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class ModelManifestEntry
{
    public string FilePath { get; set; } = string.Empty;
    public string RepoId { get; set; } = string.Empty;
    public string RepoFile { get; set; } = string.Empty;
    public string RevisionSha { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>"starter" | "hf-browser" | "local-ai-setup" | "migration" | "manual".</summary>
    public string Source { get; set; } = string.Empty;

    // Pending-update fields, set by a 3.2 check when the tree's lfs.oid differs from Sha256;
    // cleared once a 3.3 update is applied.
    public string? PendingSha256 { get; set; }
    public string? PendingRevisionSha { get; set; }
    public long? PendingSizeBytes { get; set; }
    public DateTime? LastCheckedAtUtc { get; set; }
    public bool NoLongerPublished { get; set; }

    /// <summary>Known companions mapped by trusted source metadata for this primary model.</summary>
    public List<ModelCompanionManifestEntry> Companions { get; set; } = [];

    /// <summary>Set on a companion entry so recovery and safe removal can find its owner.</summary>
    public string ParentModelPath { get; set; } = string.Empty;
    public string CompanionRole { get; set; } = string.Empty;

    public bool HasPendingUpdate => !string.IsNullOrWhiteSpace(PendingSha256);
}

public sealed class ModelCompanionManifestEntry
{
    public string LocalFilePath { get; set; } = string.Empty;
    public string RepoFile { get; set; } = string.Empty;
    public string RevisionSha { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long? SizeBytes { get; set; }
    public bool RequiresUserConfirmation { get; set; }
}

/// <summary>
/// Provenance manifest at <c>{DataRoot}/model-manifest.json</c>: which local GGUF files came
/// from which Hugging Face repo, so update checks (doc 03 3.2) know what to compare against.
/// Swept into backup/data-root migration automatically - it lives under the data root and
/// DataRootManifest.EnumerateAll walks the whole tree except settings.json
/// (r13 03-hugging-face.md 3.1).
/// </summary>
public sealed class ModelManifestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private readonly ISettingsService _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ModelManifestStore(ISettingsService settings) => _settings = settings;

    private string ManifestPath => Path.Combine(SettingsService.ResolveDataRoot(_settings.Settings), "model-manifest.json");

    /// <summary>Entries whose file still exists on disk. A deleted model's entry is silently
    /// pruned on the next load rather than erroring.</summary>
    public async Task<IReadOnlyList<ModelManifestEntry>> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return await LoadUnlockedAsync(ct); }
        finally { _gate.Release(); }
    }

    public async Task<ModelManifestEntry?> FindAsync(string filePath, CancellationToken ct = default)
    {
        var normalized = NormalizeKey(filePath);
        var entries = await LoadAsync(ct);
        return entries.FirstOrDefault(e => ModelPathSafety.AreSameLocalPath(e.FilePath, normalized));
    }

    /// <summary>Find-or-replace by canonical local file path using the host filesystem case policy.</summary>
    public async Task UpsertAsync(ModelManifestEntry entry, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var entries = await LoadUnlockedAsync(ct);
            var normalized = NormalizeKey(entry.FilePath);
            entries.RemoveAll(e => ModelPathSafety.AreSameLocalPath(e.FilePath, normalized));
            entries.Add(entry);
            await SaveUnlockedAsync(entries, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task RemoveAsync(string filePath, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var entries = await LoadUnlockedAsync(ct);
            var normalized = NormalizeKey(filePath);
            entries.RemoveAll(e => ModelPathSafety.AreSameLocalPath(e.FilePath, normalized));
            await SaveUnlockedAsync(entries, ct);
        }
        finally { _gate.Release(); }
    }

    private async Task<List<ModelManifestEntry>> LoadUnlockedAsync(CancellationToken ct)
    {
        var path = ManifestPath;
        if (!File.Exists(path))
            return [];

        List<ModelManifestEntry>? entries;
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            entries = JsonSerializer.Deserialize<List<ModelManifestEntry>>(json, JsonOptions);
        }
        catch
        {
            return [];
        }

        entries ??= [];
        return entries.Where(e => File.Exists(e.FilePath)).ToList();
    }

    private async Task SaveUnlockedAsync(List<ModelManifestEntry> entries, CancellationToken ct)
    {
        var path = ManifestPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temp, json, ct);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    /// <summary>Removes one companion mapping while preserving the primary model entry.</summary>
    public async Task RemoveCompanionAsync(string modelPath, string companionPath, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var entries = await LoadUnlockedAsync(ct);
            var parent = entries.FirstOrDefault(entry => ModelPathSafety.AreSameLocalPath(entry.FilePath, modelPath));
            if (parent is null)
                return;

            parent.Companions.RemoveAll(companion =>
                ModelPathSafety.AreSameLocalPath(companion.LocalFilePath, companionPath));
            await SaveUnlockedAsync(entries, ct);
        }
        finally { _gate.Release(); }
    }

    private static string NormalizeKey(string path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path.Trim());
}
