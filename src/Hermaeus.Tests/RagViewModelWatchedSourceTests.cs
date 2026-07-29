using Hermaeus.Rag;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Pipeline;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>doc 03 3.4-3.5: ViewModel-layer coverage for watched-source commands. The
/// service layer (scan classification, apply semantics, concurrency refusal) is covered
/// by WatchedSourceServiceTests; these tests only cover what the ViewModel adds - wiring,
/// the automatic-refresh entry point, and the embedding-mismatch/never-delete guards.</summary>
public sealed class RagViewModelWatchedSourceTests
{
    private static async Task<(RagViewModel Vm, SqliteRagStore Store, RagQueryService Query, WatchedSourceService Watched, Hermaeus.Core.Services.ISettingsService Settings)> NewAsync(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteRagStore(settings);
        await store.InitializeAsync();
        var embed = new FakeEmbeddingService();
        var query = new RagQueryService(store, embed, new FakeLlm(), settings, new NoOpReranker());
        var pipeline = new RagPipeline(store, embed);
        var eval = new Hermaeus.Rag.Eval.RagEvalService(query, settings, new FakeEvalStore());
        var watched = new WatchedSourceService(store, pipeline);
        var vm = new RagViewModel(query, pipeline, eval, new FakeToasts(), new RuntimeLogService(settings), settings,
            services: null, xtts: null, kokoro: null, activity: null, watchedSources: watched);
        return (vm, store, query, watched, settings);
    }

    [Fact]
    public async Task Automatic_refresh_ingests_new_and_changed_but_never_touches_missing()
    {
        using var temp = new TempDir();
        var (vm, store, query, _, _) = await NewAsync(temp);
        var dir = temp.PathFor("watched");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "keep.md"), "keep");
        await File.WriteAllTextAsync(Path.Combine(dir, "gone.md"), "gone");

        var dataset = new RagDataset { Name = "auto-ds" };
        dataset.Config.WatchedSources.Add(new RagWatchedSource { Root = dir });
        await query.SaveDatasetAsync(dataset);

        // First pass ingests the two seed files as "new".
        await vm.RunAutomaticWatchedRefreshAsync();
        var afterFirst = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
        Assert.True(afterFirst.Count > 0, "the automatic pass should have ingested the seed files");

        File.Delete(Path.Combine(dir, "gone.md"));
        await File.WriteAllTextAsync(Path.Combine(dir, "keep.md"), "keep, edited");
        await File.WriteAllTextAsync(Path.Combine(dir, "new.md"), "brand new");

        await vm.RunAutomaticWatchedRefreshAsync();

        var after = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
        Assert.Contains(after, c => c.SourcePath.EndsWith("gone.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(after, c => c.SourcePath.EndsWith("new.md", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Automatic_refresh_skips_a_dataset_whose_embedding_model_has_drifted()
    {
        using var temp = new TempDir();
        var (vm, store, query, _, settings) = await NewAsync(temp);
        var dir = temp.PathFor("watched");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "a.md"), "content");

        var dataset = new RagDataset { Name = "mismatch-ds" };
        dataset.Config.WatchedSources.Add(new RagWatchedSource { Root = dir });
        dataset.Config.EmbeddingModel = "model-a";
        await query.SaveDatasetAsync(dataset);
        settings.Settings.Rag.EmbeddingModel = "model-b";

        await vm.RunAutomaticWatchedRefreshAsync();

        var after = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
        Assert.Empty(after);
    }

    [Fact]
    public async Task Refresh_command_applies_only_after_confirmation_is_granted()
    {
        using var temp = new TempDir();
        var (vm, store, query, _, _) = await NewAsync(temp);
        var dir = temp.PathFor("watched");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "a.md"), "content");

        var dataset = new RagDataset { Name = "confirm-ds" };
        dataset.Config.WatchedSources.Add(new RagWatchedSource { Root = dir });
        await query.SaveDatasetAsync(dataset);

        vm.RequestConfirmWatchedRefresh = (_, _) => Task.FromResult(false);
        await vm.RefreshDatasetManagerCommand.ExecuteAsync(null);
        var item = vm.DatasetManagerItems.First(i => i.Id == dataset.Id);
        await vm.RefreshWatchedSourcesCommand.ExecuteAsync(item);
        Assert.Empty(await store.GetChunksAsync(dataset.Id, includeEmbeddings: false));

        vm.RequestConfirmWatchedRefresh = (_, _) => Task.FromResult(true);
        item.DriftPlan = null;
        await vm.RefreshWatchedSourcesCommand.ExecuteAsync(item);
        Assert.NotEmpty(await store.GetChunksAsync(dataset.Id, includeEmbeddings: false));
    }
}
