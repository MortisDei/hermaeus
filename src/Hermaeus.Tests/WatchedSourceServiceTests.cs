using Hermaeus.Core.Services;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Pipeline;
using Hermaeus.Rag.Storage;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class WatchedSourceServiceTests
{
    private static async Task<(WatchedSourceService Service, SqliteRagStore Store, RagPipeline Pipeline)> NewAsync(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteRagStore(settings);
        await store.InitializeAsync();
        var pipeline = new RagPipeline(store, new FakeEmbeddingService());
        return (new WatchedSourceService(store, pipeline), store, pipeline);
    }

    private static RagWatchedSource Watched(string root) => new() { Root = root };

    [Fact]
    public async Task Scan_classifies_new_files_with_no_prior_ingest()
    {
        using var temp = new TempDir();
        var (service, store, _) = await NewAsync(temp);
        var dir = temp.PathFor("watched");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "a.md"), "hello world");

        var dataset = new RagDataset { Name = "ds" };
        dataset.Config.WatchedSources.Add(Watched(dir));
        await store.SaveDatasetAsync(dataset);

        var plan = await service.ScanAsync(dataset);

        Assert.Single(plan.NewFiles);
        Assert.Empty(plan.ChangedFiles);
        Assert.Empty(plan.MissingFiles);
        Assert.Equal(0, plan.UnchangedCount);
    }

    [Fact]
    public async Task Default_excludes_keep_node_modules_and_git_out_of_a_scan()
    {
        using var temp = new TempDir();
        var (service, store, _) = await NewAsync(temp);
        var dir = temp.PathFor("repo");
        Directory.CreateDirectory(Path.Combine(dir, "node_modules", "pkg"));
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        await File.WriteAllTextAsync(Path.Combine(dir, "node_modules", "pkg", "README.md"), "noise");
        await File.WriteAllTextAsync(Path.Combine(dir, ".git", "COMMIT_EDITMSG.md"), "noise");
        await File.WriteAllTextAsync(Path.Combine(dir, "real.md"), "signal");

        var dataset = new RagDataset { Name = "ds" };
        dataset.Config.WatchedSources.Add(Watched(dir));
        await store.SaveDatasetAsync(dataset);

        var plan = await service.ScanAsync(dataset);

        var file = Assert.Single(plan.NewFiles);
        Assert.Equal("real.md", Path.GetFileName(file));
    }

    [Fact]
    public async Task Scan_prefers_hash_over_mtime_when_a_hash_is_stored()
    {
        using var temp = new TempDir();
        var (service, store, pipeline) = await NewAsync(temp);
        var dir = temp.PathFor("watched");
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, "a.md");
        await File.WriteAllTextAsync(filePath, "version one");

        var dataset = new RagDataset { Name = "ds" };
        dataset.Config.WatchedSources.Add(Watched(dir));
        await pipeline.IngestDirectoryAsync(dataset, dir);

        // Touch mtime without changing content: hash-based detection must
        // report unchanged even though mtime moved.
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddMinutes(5));
        var reloaded = (await store.GetDatasetsAsync()).Single(d => d.Id == dataset.Id);
        var planUnchanged = await service.ScanAsync(reloaded);
        Assert.Equal(0, planUnchanged.NewFiles.Count + planUnchanged.ChangedFiles.Count);
        Assert.Equal(1, planUnchanged.UnchangedCount);

        await File.WriteAllTextAsync(filePath, "version two, actually different");
        var planChanged = await service.ScanAsync(reloaded);
        Assert.Single(planChanged.ChangedFiles);
    }

    [Fact]
    public async Task Scan_detects_a_missing_file_under_a_watched_root()
    {
        using var temp = new TempDir();
        var (service, store, pipeline) = await NewAsync(temp);
        var dir = temp.PathFor("watched");
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, "a.md");
        await File.WriteAllTextAsync(filePath, "content");

        var dataset = new RagDataset { Name = "ds" };
        dataset.Config.WatchedSources.Add(Watched(dir));
        await pipeline.IngestDirectoryAsync(dataset, dir);
        var reloaded = (await store.GetDatasetsAsync()).Single(d => d.Id == dataset.Id);

        File.Delete(filePath);
        var plan = await service.ScanAsync(reloaded);

        Assert.Single(plan.MissingFiles);
    }

    [Fact]
    public async Task ApplyNewAndChangedAsync_never_touches_missing_files()
    {
        using var temp = new TempDir();
        var (service, store, pipeline) = await NewAsync(temp);
        var dir = temp.PathFor("watched");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "keep.md"), "keep");
        await File.WriteAllTextAsync(Path.Combine(dir, "gone.md"), "gone");

        var dataset = new RagDataset { Name = "ds" };
        dataset.Config.WatchedSources.Add(Watched(dir));
        await pipeline.IngestDirectoryAsync(dataset, dir);
        var reloaded = (await store.GetDatasetsAsync()).Single(d => d.Id == dataset.Id);
        File.Delete(Path.Combine(dir, "gone.md"));
        await File.WriteAllTextAsync(Path.Combine(dir, "keep.md"), "keep, edited");

        var plan = await service.ScanAsync(reloaded);
        await service.ApplyNewAndChangedAsync(reloaded, plan);

        var chunksAfter = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
        Assert.Contains(chunksAfter, c => c.SourcePath.EndsWith("gone.md", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MissingIsOverHalf_flags_a_likely_unmounted_drive_or_bad_glob()
    {
        var plan = new RagRefreshPlan([], [], ["a", "b", "c"], 1, []);
        Assert.True(plan.MissingIsOverHalf(existingSourceCount: 4));
        Assert.False(plan.MissingIsOverHalf(existingSourceCount: 10));
    }

    [Fact]
    public async Task A_second_refresh_while_one_runs_is_refused_not_queued()
    {
        using var temp = new TempDir();
        var (service, store, _) = await NewAsync(temp);
        var dataset = new RagDataset { Name = "ds" };
        await store.SaveDatasetAsync(dataset);

        var plan = new RagRefreshPlan([temp.PathFor("nonexistent.md")], [], [], 0, []);
        // Reserve the slot directly to simulate a refresh already in flight,
        // without needing a real long-running ingest to race against.
        var reserveField = typeof(WatchedSourceService).GetField("_refreshing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var set = (HashSet<string>)reserveField.GetValue(service)!;
        set.Add(dataset.Id);

        await ThrowsAsync<InvalidOperationException>(() => service.ApplyNewAndChangedAsync(dataset, plan));
    }

    [Fact]
    public async Task Scan_is_cancellable_mid_walk()
    {
        using var temp = new TempDir();
        var (service, store, _) = await NewAsync(temp);
        var dir = temp.PathFor("watched");
        Directory.CreateDirectory(dir);
        for (var i = 0; i < 20; i++)
            await File.WriteAllTextAsync(Path.Combine(dir, $"f{i}.md"), $"content {i}");

        var dataset = new RagDataset { Name = "ds" };
        dataset.Config.WatchedSources.Add(Watched(dir));
        await store.SaveDatasetAsync(dataset);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => service.ScanAsync(dataset, cts.Token));
    }
}
