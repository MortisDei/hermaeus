using System.Text.RegularExpressions;

namespace Hermaeus.Services;

/// <summary>What role a file plays in a model's file set.</summary>
public enum ModelFileRole
{
    /// <summary>The file the user clicked.</summary>
    Model,

    /// <summary>Another part of the same sharded model. Without every shard, nothing loads.</summary>
    Shard,

    /// <summary>A source-mapped vision projector companion.</summary>
    Projector,

    /// <summary>A source-mapped Multi-Token Prediction head.</summary>
    DraftHead
}

/// <summary>One file in a model's set, with the size and hash the download path already verifies.</summary>
public sealed record ModelFileSetEntry(
    string RepoPath,
    long? SizeBytes,
    string? LfsSha256,
    ModelFileRole Role,
    bool Required,
    bool SelectedByDefault)
{
    public string FileName => Path.GetFileName(RepoPath);
}

/// <summary>A model and its companions, resolved from a repository tree.</summary>
public sealed record ModelFileSet(string RepoId, IReadOnlyList<ModelFileSetEntry> Entries)
{
    /// <summary>Everything that must be fetched: a partial shard set is a model that does not load.</summary>
    public IReadOnlyList<ModelFileSetEntry> Required => [.. Entries.Where(e => e.Required)];

    public IReadOnlyList<ModelFileSetEntry> Optional => [.. Entries.Where(e => !e.Required)];

    public long TotalBytes => Entries.Sum(e => e.SizeBytes ?? 0);

    public long DefaultSelectionBytes => Entries.Where(e => e.Required || e.SelectedByDefault).Sum(e => e.SizeBytes ?? 0);

    public bool IsSharded => Entries.Count(e => e.Role is ModelFileRole.Model or ModelFileRole.Shard) > 1;
}

/// <summary>
/// r27 04-models-arrive-complete.md 4.1: a GGUF model is frequently not one
/// file. The download path took one <c>HfFileResultViewModel</c>, resolved one
/// URL, and wrote one destination, so a multimodal model arrived without its
/// projector, a model with an MTP head arrived without the head, and a sharded
/// model arrived as one shard that will not load.
/// The repository tree is already fetched for the browser, so resolving a file
/// set needs no extra request. Pure over that tree.
/// </summary>
public static class ModelFileSetResolver
{
    /// <summary>Reuses the shard pattern the organizer already understands rather than writing a second one.</summary>
    private static readonly Regex ShardRegex =
        new(@"^(?<base>.+)-(?<part>\d{5})-of-(?<total>\d{5})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ModelFileSet Resolve(
        string repoId,
        IReadOnlyList<HfTreeEntry> tree,
        string selectedPath,
        IReadOnlyList<HfCompanionDeclaration>? companionDeclarations = null)
    {
        var selected = (selectedPath ?? string.Empty).Replace('\\', '/').Trim();
        if (selected.Length == 0)
            return new ModelFileSet(repoId, []);

        var entries = new List<ModelFileSetEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedDirectory = DirectoryOf(selected);
        var selectedBase = Path.GetFileNameWithoutExtension(selected);
        var shardMatch = ShardRegex.Match(selectedBase);

        void Add(HfTreeEntry entry, ModelFileRole role, bool required, bool byDefault)
        {
            if (!seen.Add(entry.Path))
                return;
            entries.Add(new ModelFileSetEntry(entry.Path, entry.SizeBytes, entry.LfsSha256, role, required, byDefault));
        }

        var selectedEntry = tree.FirstOrDefault(e => string.Equals(Normalize(e.Path), selected, StringComparison.OrdinalIgnoreCase))
            ?? new HfTreeEntry(selected, null, null);

        if (shardMatch.Success)
        {
            // Shards are not a checkbox: part of the download, or the download
            // is refused. Every sibling with the same base and total, in order.
            var shardBase = shardMatch.Groups["base"].Value;
            var total = shardMatch.Groups["total"].Value;
            var shards = tree
                .Where(e => IsShardOf(e.Path, selectedDirectory, shardBase, total))
                .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var shard in shards)
                Add(shard, string.Equals(Normalize(shard.Path), selected, StringComparison.OrdinalIgnoreCase) ? ModelFileRole.Model : ModelFileRole.Shard,
                    required: true, byDefault: true);
        }
        else
        {
            Add(selectedEntry, ModelFileRole.Model, required: true, byDefault: true);
        }

        // Companions are offered only from an explicit, hash-verified source
        // mapping. A mmproj-/mtp- filename or directory is not compatibility evidence.
        foreach (var declaration in companionDeclarations ?? [])
        {
            if (!string.Equals(Normalize(declaration.ModelPath), selected, StringComparison.OrdinalIgnoreCase))
                continue;

            var entry = tree.FirstOrDefault(e => string.Equals(Normalize(e.Path), Normalize(declaration.CompanionPath), StringComparison.OrdinalIgnoreCase));
            if (entry is null || string.IsNullOrWhiteSpace(entry.LfsSha256))
                continue;

            Add(entry, declaration.Role, required: false, byDefault: true);
        }

        return new ModelFileSet(repoId, entries);
    }

    private static bool IsShardOf(string path, string directory, string shardBase, string total)
    {
        if (!string.Equals(DirectoryOf(path), directory, StringComparison.OrdinalIgnoreCase))
            return false;

        var match = ShardRegex.Match(Path.GetFileNameWithoutExtension(path));
        return match.Success
            && string.Equals(match.Groups["base"].Value, shardBase, StringComparison.OrdinalIgnoreCase)
            && string.Equals(match.Groups["total"].Value, total, StringComparison.Ordinal);
    }

    private static string Normalize(string path) => (path ?? string.Empty).Replace('\\', '/').Trim();

    private static string DirectoryOf(string path)
    {
        var normalized = Normalize(path);
        var index = normalized.LastIndexOf('/');
        return index < 0 ? string.Empty : normalized[..index];
    }

}
