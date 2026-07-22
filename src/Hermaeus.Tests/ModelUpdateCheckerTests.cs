using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

// r13 03-hugging-face.md 3.2: pure per-file update decision against a fetched tree.
public sealed class ModelUpdateCheckerTests
{
    private static ModelManifestEntry NewEntry(string repoFile = "model.gguf", string sha256 = "abc") => new()
    {
        FilePath = @"C:\models\model.gguf",
        RepoId = "org/repo",
        RepoFile = repoFile,
        Sha256 = sha256,
        Source = "hf-browser"
    };

    [Fact]
    public void Evaluate_matches_oid_as_up_to_date()
    {
        var entry = NewEntry(sha256: "abc123");
        var tree = new List<HfTreeEntry> { new("model.gguf", 100, "abc123") };

        var result = ModelUpdateChecker.Evaluate(entry, tree);

        Assert.Equal(ModelUpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public void Evaluate_oid_drift_is_update_available()
    {
        var entry = NewEntry(sha256: "abc123");
        var tree = new List<HfTreeEntry> { new("model.gguf", 100, "def456") };

        var result = ModelUpdateChecker.Evaluate(entry, tree);

        Assert.Equal(ModelUpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal("def456", result.MatchedEntry!.LfsSha256);
    }

    [Fact]
    public void Evaluate_file_missing_from_tree_is_no_longer_published()
    {
        var entry = NewEntry();
        var tree = new List<HfTreeEntry> { new("other-file.gguf", 100, "abc123") };

        var result = ModelUpdateChecker.Evaluate(entry, tree);

        Assert.Equal(ModelUpdateStatus.NoLongerPublished, result.Status);
    }

    [Fact]
    public void Evaluate_null_tree_is_check_failed_not_no_longer_published()
    {
        var entry = NewEntry();

        var result = ModelUpdateChecker.Evaluate(entry, null);

        Assert.Equal(ModelUpdateStatus.CheckFailed, result.Status);
    }

    [Fact]
    public void Evaluate_treats_a_non_lfs_match_as_up_to_date()
    {
        var entry = NewEntry(sha256: "abc123");
        var tree = new List<HfTreeEntry> { new("model.gguf", 10, null) };

        var result = ModelUpdateChecker.Evaluate(entry, tree);

        Assert.Equal(ModelUpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public void Evaluate_oid_comparison_is_case_insensitive()
    {
        var entry = NewEntry(sha256: "ABC123");
        var tree = new List<HfTreeEntry> { new("model.gguf", 100, "abc123") };

        var result = ModelUpdateChecker.Evaluate(entry, tree);

        Assert.Equal(ModelUpdateStatus.UpToDate, result.Status);
    }
}
