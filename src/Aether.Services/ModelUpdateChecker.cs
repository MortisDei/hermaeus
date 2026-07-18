namespace Aether.Services;

public enum ModelUpdateStatus { NotLinked, UpToDate, UpdateAvailable, NoLongerPublished, CheckFailed }

public sealed record ModelUpdateCheckResult(ModelUpdateStatus Status, HfTreeEntry? MatchedEntry);

/// <summary>
/// Pure per-file comparison of a manifest entry against a fetched repo tree
/// (r13 03-hugging-face.md 3.2). Orchestration - fetching the tree, batching by repo, hashing
/// migration-sourced entries that have no stored hash yet - lives in the caller; this is only
/// the decision logic, so it is directly testable with canned tree fixtures.
/// </summary>
public static class ModelUpdateChecker
{
    /// <param name="manifest">The stored entry; its RepoFile is matched against the tree's
    /// path, and its Sha256 is compared against the tree's lfs.oid.</param>
    /// <param name="tree">Null means the tree fetch itself failed (network/parse error),
    /// which is CheckFailed - distinct from a successful fetch where the file is genuinely
    /// gone (NoLongerPublished).</param>
    public static ModelUpdateCheckResult Evaluate(ModelManifestEntry manifest, IReadOnlyList<HfTreeEntry>? tree)
    {
        if (tree is null)
            return new ModelUpdateCheckResult(ModelUpdateStatus.CheckFailed, null);

        var match = tree.FirstOrDefault(e => string.Equals(e.Path, manifest.RepoFile, StringComparison.Ordinal));
        if (match is null)
            return new ModelUpdateCheckResult(ModelUpdateStatus.NoLongerPublished, null);

        // A non-LFS entry (unusual for a multi-hundred-MB GGUF, but possible for a tiny test
        // model) has no oid to compare; treat it as current rather than guessing.
        if (string.IsNullOrWhiteSpace(match.LfsSha256) || string.Equals(match.LfsSha256, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            return new ModelUpdateCheckResult(ModelUpdateStatus.UpToDate, match);

        return new ModelUpdateCheckResult(ModelUpdateStatus.UpdateAvailable, match);
    }
}
