using Hermaeus.Rag.Embeddings;
using Hermaeus.Core.Models;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Pipeline;
using Hermaeus.Rag.Storage;
using Hermaeus.Services.Recall;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class DocumentRecallSourceTests
{
    [Fact]
    public async Task Document_recall_returns_calibrated_relevance_after_hybrid_ordering()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var store = new SqliteRagStore(settings);
        await store.InitializeAsync();

        var docs = temp.PathFor("docs");
        Directory.CreateDirectory(docs);
        await File.WriteAllTextAsync(Path.Combine(docs, "archive.txt"),
            "The archive seal is a gold monogram grown through with a tree and an open book.");

        var embeddings = new ConstantEmbeddingService();
        var dataset = new RagDataset { Name = "archive" };
        await new RagPipeline(store, embeddings).IngestDirectoryAsync(dataset, docs);

        var source = new DocumentRecallSource(store, embeddings);
        var hits = await source.SearchAsync("archive seal", string.Empty, CancellationToken.None);

        var hit = Assert.Single(hits);
        Assert.Equal(RecallKind.Document, hit.Kind);
        Assert.True(hit.Score >= 0.40, $"document source relevance was {hit.Score}");

        var fused = await new RecallService([source], embeddings).SearchAsync("archive seal");
        Assert.Single(fused.Hits);
    }

    private sealed class ConstantEmbeddingService : IEmbeddingService
    {
        public int Dimensions => 4;

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new[] { 1f, 0f, 0f, 0f });

        public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult(texts.Select(_ => new[] { 1f, 0f, 0f, 0f }).ToList());
    }
}
