using Hermaeus.Rag.Models;
using Hermaeus.Rag.Pipeline;
using Hermaeus.Rag.Storage;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class RagLineageTests
{
    [Fact]
    public async Task Ingest_publishes_one_current_generation_with_exact_source_lineage()
    {
        using var temp = new TempDir();
        var (store, _) = await NewAsync(temp);
        var docs = temp.PathFor("docs");
        Directory.CreateDirectory(docs);
        var file = Path.Combine(docs, "knowledge.md");
        await File.WriteAllTextAsync(file, "The exact source content is indexed here.");

        var dataset = new RagDataset { Name = "lineage" };
        await new RagPipeline(store, new FakeEmbeddingService()).IngestDirectoryAsync(dataset, docs);

        var chunk = Assert.Single(await store.GetChunksAsync(dataset.Id, includeEmbeddings: false));
        Assert.NotEmpty(chunk.GenerationId);
        Assert.NotEmpty(chunk.SourceId);
        Assert.NotEmpty(chunk.SourceRevisionId);
        Assert.Equal(dataset.Id, chunk.DatasetId);
        Assert.Contains($"rag:{dataset.Id}/generation:{chunk.GenerationId}/source:{chunk.SourceId}/revision:{chunk.SourceRevisionId}/content:{chunk.SourceHash}",
            RagCitationIdentity.BuildLocator(chunk), StringComparison.Ordinal);

        var reloaded = Assert.Single(await store.GetDatasetsAsync(), d => d.Id == dataset.Id);
        Assert.Equal(chunk.GenerationId, reloaded.CurrentGenerationId);
        var generation = Assert.Single(await store.GetGenerationHistoryAsync(dataset.Id));
        Assert.Equal(RagDatasetGenerationState.Current, generation.State);
        Assert.Equal(chunk.GenerationId, generation.GenerationId);
        Assert.Equal(1, generation.ChunkCount);
    }

    [Fact]
    public async Task Reingest_creates_a_new_generation_and_keeps_source_identity_stable()
    {
        using var temp = new TempDir();
        var (store, _) = await NewAsync(temp);
        var docs = temp.PathFor("docs");
        Directory.CreateDirectory(docs);
        var file = Path.Combine(docs, "knowledge.md");
        await File.WriteAllTextAsync(file, "first revision content");

        var dataset = new RagDataset { Name = "revisions" };
        var pipeline = new RagPipeline(store, new FakeEmbeddingService());
        await pipeline.IngestDirectoryAsync(dataset, docs);
        var first = Assert.Single(await store.GetChunksAsync(dataset.Id, includeEmbeddings: false));
        var firstGeneration = first.GenerationId;

        await File.WriteAllTextAsync(file, "second revision content");
        await pipeline.IngestDirectoryAsync(dataset, docs);
        var second = Assert.Single(await store.GetChunksAsync(dataset.Id, includeEmbeddings: false));
        var history = await store.GetGenerationHistoryAsync(dataset.Id);

        Assert.Equal(2, history.Count);
        Assert.Equal(RagDatasetGenerationState.Current, history[0].State);
        Assert.Equal(RagDatasetGenerationState.Superseded, history[1].State);
        Assert.Equal(firstGeneration, history[0].PreviousGenerationId);
        Assert.NotEqual(first.GenerationId, second.GenerationId);
        Assert.Equal(first.SourceId, second.SourceId);
        Assert.NotEqual(first.SourceRevisionId, second.SourceRevisionId);
        Assert.Equal("second revision content", second.Content);
    }

    [Fact]
    public async Task Failed_embedding_cardinality_leaves_the_prior_generation_current()
    {
        using var temp = new TempDir();
        var (store, _) = await NewAsync(temp);
        var docs = temp.PathFor("docs");
        Directory.CreateDirectory(docs);
        var file = Path.Combine(docs, "knowledge.md");
        await File.WriteAllTextAsync(file, "first revision content");

        var dataset = new RagDataset { Name = "cardinality" };
        await new RagPipeline(store, new FakeEmbeddingService()).IngestDirectoryAsync(dataset, docs);
        var before = Assert.Single(await store.GetChunksAsync(dataset.Id, includeEmbeddings: false));

        await File.WriteAllTextAsync(file, "second revision content");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RagPipeline(store, new WrongCountEmbeddingService()).IngestDirectoryAsync(dataset, docs));

        var after = Assert.Single(await store.GetChunksAsync(dataset.Id, includeEmbeddings: false));
        Assert.Equal(before.GenerationId, after.GenerationId);
        Assert.Equal(before.SourceRevisionId, after.SourceRevisionId);
        Assert.Equal(before.Content, after.Content);
        Assert.Single(await store.GetGenerationHistoryAsync(dataset.Id));
    }

    [Fact]
    public async Task Mixed_embedding_dimensions_are_rejected_before_publication()
    {
        using var temp = new TempDir();
        var (store, _) = await NewAsync(temp);
        var docs = temp.PathFor("docs");
        Directory.CreateDirectory(docs);
        await File.WriteAllTextAsync(Path.Combine(docs, "a.md"), "alpha content");
        await File.WriteAllTextAsync(Path.Combine(docs, "b.md"), "bravo content");

        var dataset = new RagDataset { Name = "dimensions" };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RagPipeline(store, new MixedDimensionEmbeddingService()).IngestDirectoryAsync(dataset, docs));

        Assert.Empty(await store.GetDatasetsAsync());
    }

    [Fact]
    public async Task Source_change_after_embedding_is_rejected_without_replacing_current_content()
    {
        using var temp = new TempDir();
        var (store, _) = await NewAsync(temp);
        var docs = temp.PathFor("docs");
        Directory.CreateDirectory(docs);
        var file = Path.Combine(docs, "knowledge.md");
        await File.WriteAllTextAsync(file, "stable first revision");

        var dataset = new RagDataset { Name = "race" };
        await new RagPipeline(store, new FakeEmbeddingService()).IngestDirectoryAsync(dataset, docs);
        var before = Assert.Single(await store.GetChunksAsync(dataset.Id, includeEmbeddings: false));

        await File.WriteAllTextAsync(file, "revision loaded before embedding");
        var mutating = new MutatingEmbeddingService(file, "revision changed during embedding");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RagPipeline(store, mutating).IngestDirectoryAsync(dataset, docs));

        var after = Assert.Single(await store.GetChunksAsync(dataset.Id, includeEmbeddings: false));
        Assert.Equal(before.GenerationId, after.GenerationId);
        Assert.Equal(before.SourceRevisionId, after.SourceRevisionId);
        Assert.Equal(before.Content, after.Content);
    }

    private static async Task<(SqliteRagStore Store, Hermaeus.Services.SettingsService Settings)> NewAsync(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteRagStore(settings);
        await store.InitializeAsync();
        return (store, settings);
    }

    private sealed class WrongCountEmbeddingService : Hermaeus.Rag.Embeddings.IEmbeddingService
    {
        public int Dimensions => 4;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new[] { 1f, 0f, 0f, 0f });
        public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult(texts.Take(Math.Max(0, texts.Count - 1))
                .Select(_ => new[] { 1f, 0f, 0f, 0f }).ToList());
    }

    private sealed class MixedDimensionEmbeddingService : Hermaeus.Rag.Embeddings.IEmbeddingService
    {
        public int Dimensions => 4;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new[] { 1f, 0f, 0f, 0f });
        public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult(texts.Select((_, index) => index % 2 == 0
                    ? new[] { 1f, 0f, 0f, 0f }
                    : new[] { 1f, 0f, 0f, 0f, 0f }).ToList());
    }

    private sealed class MutatingEmbeddingService : Hermaeus.Rag.Embeddings.IEmbeddingService
    {
        private readonly string _path;
        private readonly string _replacement;
        private bool _mutated;

        public MutatingEmbeddingService(string path, string replacement)
        {
            _path = path;
            _replacement = replacement;
        }

        public int Dimensions => 4;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new[] { 1f, 0f, 0f, 0f });
        public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            if (!_mutated)
            {
                _mutated = true;
                File.WriteAllText(_path, _replacement);
            }

            return Task.FromResult(texts.Select(_ => new[] { 1f, 0f, 0f, 0f }).ToList());
        }
    }
}
