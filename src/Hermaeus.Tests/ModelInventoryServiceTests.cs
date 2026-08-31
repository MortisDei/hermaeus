using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ModelInventoryServiceTests
{
    [Fact]
    public async Task Scan_reuses_a_bounded_snapshot_when_file_identities_are_unchanged()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var assetsRoot = temp.PathFor("assets");
        var modelsRoot = Path.Combine(assetsRoot, "Models", "local");
        Directory.CreateDirectory(modelsRoot);
        File.WriteAllBytes(Path.Combine(modelsRoot, "one.gguf"), [1, 2, 3]);
        var service = new ModelInventoryService(new ModelManifestStore(settings), maximumEntries: 2);

        var first = await service.ScanAsync(assetsRoot);
        var second = await service.ScanAsync(assetsRoot);

        Assert.Single(first.Entries);
        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(first.Generation, second.Generation);
        Assert.Null(first.Entries[0].GgufInfo);
    }

    [Fact]
    public async Task Scan_bounds_results_and_reports_when_the_model_tree_is_truncated()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var modelsRoot = Path.Combine(temp.PathFor("assets"), "Models");
        Directory.CreateDirectory(modelsRoot);
        for (var i = 0; i < 3; i++)
            File.WriteAllBytes(Path.Combine(modelsRoot, $"model-{i}.gguf"), [1]);
        var service = new ModelInventoryService(new ModelManifestStore(settings), maximumEntries: 2);

        var snapshot = await service.ScanAsync(temp.PathFor("assets"));

        Assert.Equal(2, snapshot.Entries.Count);
        Assert.True(snapshot.IsTruncated);
    }

    [Fact]
    public async Task Size_or_timestamp_change_invalidates_the_snapshot_and_metadata_identity()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var assetsRoot = temp.PathFor("assets");
        var modelsRoot = Path.Combine(assetsRoot, "Models");
        Directory.CreateDirectory(modelsRoot);
        var modelPath = Path.Combine(modelsRoot, "model.gguf");
        File.WriteAllBytes(modelPath, [1]);
        var service = new ModelInventoryService(new ModelManifestStore(settings), cacheLifetime: TimeSpan.FromHours(1));

        var first = await service.ScanAsync(assetsRoot);
        File.AppendAllBytes(modelPath, [2]);
        File.SetLastWriteTimeUtc(modelPath, DateTime.UtcNow.AddMinutes(1));
        var second = await service.ScanAsync(assetsRoot);

        Assert.False(second.FromCache);
        Assert.True(second.Generation > first.Generation);
        Assert.Equal(2, second.Entries[0].SizeBytes);
    }

    [Fact]
    public async Task Explicit_invalidation_refreshes_manifest_attachment_without_a_filesystem_watcher()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var assetsRoot = temp.PathFor("assets");
        var modelsRoot = Path.Combine(assetsRoot, "Models");
        Directory.CreateDirectory(modelsRoot);
        var modelPath = Path.Combine(modelsRoot, "model.gguf");
        File.WriteAllBytes(modelPath, [1]);
        var manifest = new ModelManifestStore(settings);
        var service = new ModelInventoryService(manifest, cacheLifetime: TimeSpan.FromHours(1));

        var first = await service.ScanAsync(assetsRoot);
        await manifest.UpsertAsync(new ModelManifestEntry { FilePath = modelPath, RepoId = "owner/repo" });
        service.Invalidate(assetsRoot);
        var second = await service.ScanAsync(assetsRoot);

        Assert.Null(first.Entries[0].Manifest);
        Assert.Equal("owner/repo", second.Entries[0].Manifest?.RepoId);
        Assert.False(second.FromCache);
    }
}
