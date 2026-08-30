using System.Text.RegularExpressions;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

/// <summary>One file (or, for a multi-part GGUF set, every part) moving from its current
/// location to <c>&lt;ModelsDirectory&gt;\LLM\&lt;filename&gt;</c>. Filenames are never
/// changed - only the directory - because the filename is the HF update-matching identity
/// (doc 03) and encodes the quant.</summary>
public sealed record ModelMoveItem(
    IReadOnlyList<string> SourcePaths,
    IReadOnlyList<string> DestinationPaths,
    string? HubRepoOrg,
    string? HubRepoName);

public sealed record ModelMoveSkip(string SourcePath, string Reason);

public sealed record ModelOrganizePlan(
    IReadOnlyList<ModelMoveItem> Moves,
    IReadOnlyList<ModelMoveSkip> Skips,
    string DestinationDirectory)
{
    public int ProvenanceCount => Moves.Count(m => m.HubRepoOrg is not null);
}

public sealed record ModelMoveFailure(ModelMoveItem Item, string Error);

public sealed record ModelOrganizeResult(IReadOnlyList<ModelMoveItem> Moved, IReadOnlyList<ModelMoveFailure> Failed);

/// <summary>
/// Flattens the Hugging Face hub-cache maze (<c>hub\models--org--repo\snapshots\&lt;sha&gt;\*.gguf</c>)
/// into <c>&lt;ModelsDirectory&gt;\LLM\&lt;file&gt;.gguf</c> so the folder is human-browsable
/// (r13 02-model-library.md 2.6). Plan is pure and heavily tested; execution is the only part
/// that touches disk or settings, and both are only ever invoked after a preview+confirm in
/// the UI.
/// </summary>
public static class ModelFolderOrganizer
{
    private static readonly Regex MultiPartRegex =
        new(@"^(?<base>.+)-(?<part>\d{5})-of-(?<total>\d{5})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Builds the move plan. <paramref name="ggufPaths"/> should come from
    /// <see cref="LocalAiAssetLocator.FindGgufModels"/>, which already excludes the
    /// embed/embedding/embeddings/rerank/reranker special directories - the organizer never
    /// needs to re-check that exclusion because those files are never in its input.</summary>
    public static ModelOrganizePlan Plan(
        string modelsDirectory,
        IReadOnlyList<string> ggufPaths,
        bool moveRootLevelFiles = true,
        IReadOnlyDictionary<string, string>? repoIdsByPath = null)
    {
        var destination = Path.Combine(modelsDirectory, LlmFolderName.Resolve(modelsDirectory));
        var moves = new List<ModelMoveItem>();
        var skips = new List<ModelMoveSkip>();
        var usedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claimedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in GroupMultiPartSets(ggufPaths))
        {
            if (!moveRootLevelFiles && group.All(p => IsDirectlyUnder(p, modelsDirectory)))
                continue; // root-level files opted out of the move

            // r27 04-models-arrive-complete.md 4.3: companions move with their
            // model, into the same folder, as one group. This is the item that
            // fixes the seven-way mmproj-F16.gguf collision: in per-model
            // folders they no longer compete for one name.
            var members = group.Concat(FindCompanions(group, claimedSources)).ToList();

            var folderName = ResolveFolderName(group, repoIdsByPath);
            if (folderName.Length == 0)
            {
                // Leaving a file alone is always allowed. Moving it to the wrong
                // place is not.
                foreach (var p in members)
                    skips.Add(new ModelMoveSkip(p, "Could not attribute this file to a model or repository, so it was left where it is."));
                continue;
            }

            var modelFolder = Path.Combine(destination, folderName);
            if (members.All(p => IsDirectlyUnder(p, modelFolder)))
                continue; // already in its own per-model folder: nothing to do

            var destPaths = members.Select(p => Path.Combine(modelFolder, Path.GetFileName(p))).ToList();
            var collides = false;
            for (var i = 0; i < destPaths.Count && !collides; i++)
            {
                // A file already sitting at its own destination is not a
                // collision with itself; anything else at that name is.
                collides = usedDestinations.Contains(destPaths[i])
                    || (File.Exists(destPaths[i]) && !IsSameFile(destPaths[i], members[i]));
            }

            if (collides)
            {
                foreach (var p in members)
                    skips.Add(new ModelMoveSkip(p, $"A file named {Path.GetFileName(p)} already exists at the destination."));
                continue;
            }

            foreach (var d in destPaths)
                usedDestinations.Add(d);
            foreach (var p in members)
                claimedSources.Add(NormalizeKey(p));

            var (org, repo) = TryExtractHubRepo(group[0]);
            moves.Add(new ModelMoveItem(members, destPaths, org, repo));
        }

        return new ModelOrganizePlan(moves, skips, destination);
    }

    /// <summary>
    /// r27 4.2/4.3: the per-model folder segment, in the order the doc gives.
    /// Repository provenance where the manifest has it, the hub-cache path where
    /// the source encodes one, and the file's own base name where neither does,
    /// which is the best available answer and is at least stable.
    /// </summary>
    private static string ResolveFolderName(IReadOnlyList<string> group, IReadOnlyDictionary<string, string>? repoIdsByPath)
    {
        if (repoIdsByPath is not null)
        {
            foreach (var path in group)
            {
                if (repoIdsByPath.TryGetValue(NormalizeKey(path), out var repoId) && !string.IsNullOrWhiteSpace(repoId))
                    return ModelRepoFolder.Resolve(repoId);
            }
        }

        var (org, repo) = TryExtractHubRepo(group[0]);
        if (org is not null && repo is not null)
            return ModelRepoFolder.Resolve($"{org}/{repo}");

        var baseName = Path.GetFileNameWithoutExtension(group[0]);
        var shard = MultiPartRegex.Match(baseName);
        if (shard.Success)
            baseName = shard.Groups["base"].Value;

        return string.IsNullOrWhiteSpace(baseName) ? string.Empty : ModelRepoFolder.Resolve(baseName);
    }

    /// <summary>
    /// The <c>mmproj-*</c> and <c>mtp-*</c> files sitting beside a model.
    /// LocalAiAssetLocator.FindGgufModels excludes them from the organizer's
    /// input on purpose (they are not models), so they are picked up here from
    /// the model's own directory rather than being left behind by the move.
    /// A companion is claimed by exactly one model, so two models in one
    /// directory cannot both try to take the same file.
    /// </summary>
    private static IReadOnlyList<string> FindCompanions(IReadOnlyList<string> group, HashSet<string> claimed)
    {
        var directory = Path.GetDirectoryName(group[0]);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return [];

        try
        {
            return Directory.EnumerateFiles(directory, "*.gguf")
                .Where(IsCompanion)
                .Where(p => !claimed.Contains(NormalizeKey(p)))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static bool IsCompanion(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("mtp-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A file already sitting at its own destination is not a collision with itself.</summary>
    private static bool IsSameFile(string a, string b) =>
        string.Equals(NormalizeKey(a), NormalizeKey(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Executes a previously-confirmed plan: moves files (same-volume rename, or
    /// copy+verify-length+delete across volumes), rewrites every stored settings reference to
    /// a moved path, and saves once at the end. Never renames, never deletes without a
    /// preceding successful copy, and a per-move failure does not touch other moves' files or
    /// abort the remaining moves. When <paramref name="manifest"/> is supplied, a move whose
    /// source path encoded an HF hub-cache repo id also gets a provenance manifest entry
    /// (source "migration", hash left empty until the first update check hashes the file -
    /// r13 03-hugging-face.md 3.1's migration writer).</summary>
    public static async Task<ModelOrganizeResult> ExecuteAsync(ModelOrganizePlan plan, ISettingsService settings, ModelManifestStore? manifest = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(plan.DestinationDirectory);
        var pathRewrites = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var moved = new List<ModelMoveItem>();
        var failed = new List<ModelMoveFailure>();

        foreach (var move in plan.Moves)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                for (var i = 0; i < move.SourcePaths.Count; i++)
                {
                    // r27 4.2: destinations are per-model folders now, so the
                    // directory a file lands in may not exist yet.
                    Directory.CreateDirectory(Path.GetDirectoryName(move.DestinationPaths[i])!);
                    MoveFile(move.SourcePaths[i], move.DestinationPaths[i]);
                }

                for (var i = 0; i < move.SourcePaths.Count; i++)
                    pathRewrites[NormalizeKey(move.SourcePaths[i])] = move.DestinationPaths[i];

                moved.Add(move);

                if (manifest is not null && move.HubRepoOrg is not null && move.HubRepoName is not null)
                {
                    var file = new FileInfo(move.DestinationPaths[0]);
                    await manifest.UpsertAsync(new ModelManifestEntry
                    {
                        FilePath = move.DestinationPaths[0],
                        RepoId = $"{move.HubRepoOrg}/{move.HubRepoName}",
                        RepoFile = Path.GetFileName(move.DestinationPaths[0]),
                        RevisionSha = string.Empty,
                        Sha256 = string.Empty,
                        SizeBytes = file.Length,
                        Source = "migration"
                    }, ct);
                }
            }
            catch (Exception ex)
            {
                failed.Add(new ModelMoveFailure(move, ex.Message));
            }
        }

        RewriteReferences(settings.Settings, pathRewrites);
        await settings.SaveAsync();

        return new ModelOrganizeResult(moved, failed);
    }

    /// <summary>Rewrites every stored reference to a path that moved. Pure (no filesystem
    /// access); call after the physical moves succeed. RagSettings carries no filesystem path
    /// for embeddings (EmbeddingModel is a model name/id, and the embeddings GGUF path lives
    /// on the EmbeddingsMode ManagedServers entry, already covered below) - and the embed/
    /// embedding/embeddings special directories are structurally excluded from the organizer's
    /// input, so RAG settings can never reference a moved path in the first place.</summary>
    public static void RewriteReferences(AppSettings settings, IReadOnlyDictionary<string, string> pathRewrites)
    {
        if (pathRewrites.Count == 0)
            return;

        foreach (var server in settings.ManagedServers)
        {
            if (pathRewrites.TryGetValue(NormalizeKey(server.ModelPath), out var newPath))
                server.ModelPath = newPath;
            if (pathRewrites.TryGetValue(NormalizeKey(server.MmprojPath), out var newMmprojPath))
                server.MmprojPath = newMmprojPath;
        }

        foreach (var profile in settings.LlamaTuneProfiles)
            if (pathRewrites.TryGetValue(NormalizeKey(profile.ModelPath), out var newPath))
                profile.ModelPath = newPath;

        foreach (var profile in settings.ModelProfiles)
            if (pathRewrites.TryGetValue(NormalizeKey(profile.ModelId), out var newPath))
                profile.ModelId = newPath;
    }

    /// <summary>Empty directories left behind under the vacated hub-cache tree, deepest first
    /// so a caller removing top-down still catches directories that only became empty once
    /// their child was removed. Never includes non-empty directories.</summary>
    public static IReadOnlyList<string> FindEmptyDirectories(string root)
    {
        if (!Directory.Exists(root))
            return [];

        return Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .Where(d => !Directory.EnumerateFileSystemEntries(d).Any())
            .OrderByDescending(d => d.Length)
            .ToList();
    }

    /// <summary>Removes only directories that are still empty at removal time (a second,
    /// separately-confirmed action per r13 02-model-library.md 2.6); never deletes files, and
    /// silently skips anything that is no longer empty or no longer exists.</summary>
    public static void RemoveEmptyDirectories(IEnumerable<string> directories)
    {
        foreach (var dir in directories.OrderByDescending(d => d.Length))
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch
            {
                // Best-effort cleanup; leaving a directory behind is harmless.
            }
        }
    }

    private static void MoveFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        var sameVolume = ModelPathSafety.AreSameLocalPath(
            Path.GetPathRoot(Path.GetFullPath(source)),
            Path.GetPathRoot(Path.GetFullPath(destination)));
        if (sameVolume)
        {
            File.Move(source, destination);
            return;
        }

        File.Copy(source, destination);
        var sourceLength = new FileInfo(source).Length;
        var destinationLength = new FileInfo(destination).Length;
        if (sourceLength != destinationLength)
        {
            File.Delete(destination);
            throw new IOException($"Copy verification failed for {Path.GetFileName(source)}: size mismatch after copy.");
        }
        File.Delete(source);
    }

    private static bool IsDirectlyUnder(string path, string directory) =>
        ModelPathSafety.AreSameLocalPath(
            Path.GetFullPath(Path.GetDirectoryName(path) ?? string.Empty),
            Path.GetFullPath(directory));

    private static string NormalizeKey(string path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path.Trim());

    /// <summary>Groups <c>*-00001-of-00003.gguf</c>-style parts (matched by directory + base
    /// name + total-part count, using the host filesystem case policy) so they move together or not at all;
    /// everything else is its own single-file group.</summary>
    private static List<List<string>> GroupMultiPartSets(IEnumerable<string> paths)
    {
        var groups = new Dictionary<string, List<string>>(ModelPathSafety.LocalPathComparer);
        var result = new List<List<string>>();

        foreach (var path in paths)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var match = MultiPartRegex.Match(name);
            if (!match.Success)
            {
                result.Add([path]);
                continue;
            }

            var key = $"{Path.GetDirectoryName(path)}|{match.Groups["base"].Value}|{match.Groups["total"].Value}";
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }
            list.Add(path);
        }

        foreach (var group in groups.Values)
            result.Add(group.OrderBy(p => p, ModelPathSafety.LocalPathComparer).ToList());

        return result;
    }

    /// <summary>Extracts (org, repo) from a <c>models--org--repo</c> hub-cache path segment,
    /// the HF cache convention that encodes the repo id with "/" replaced by "--". A 3-way
    /// split on "--" keeps repo names that themselves contain single hyphens intact.</summary>
    private static (string? Org, string? Repo) TryExtractHubRepo(string path)
    {
        foreach (var segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (!segment.StartsWith("models--", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = segment.Split("--", 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3)
                return (parts[1], parts[2]);
        }
        return (null, null);
    }
}
