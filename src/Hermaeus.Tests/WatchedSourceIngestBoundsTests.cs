using System.Text;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Pipeline;
using Hermaeus.Rag.Storage;
using Xunit;
using static Hermaeus.Tests.Helpers;
using static Hermaeus.Tests.PdfHelpers;

namespace Hermaeus.Tests;

public sealed class WatchedSourceIngestBoundsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Drift_scan_uses_the_same_source_and_character_limits_as_ingest(bool sourceBytes)
    {
        using var temp = new TempDir();
        var (store, pipeline, dataset, file) = await SeedAsync(temp, new RagIngestLimits(
            MaxSourceBytes: 64, MaxExtractedCharacters: 4));
        var previous = Assert.Single(await store.GetChunksAsync(dataset.Id, false));
        await File.WriteAllTextAsync(file, new string('x', sourceBytes ? 65 : 5));

        var plan = await new WatchedSourceService(store, pipeline).ScanAsync(dataset);

        Assert.Contains(sourceBytes ? "byte" : "character", Assert.Single(plan.Errors), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(plan.ChangedFiles);
        Assert.Empty(plan.MissingFiles);
        Assert.Empty(plan.NewFiles);
        Assert.Equal(0, plan.UnchangedCount);
        Assert.Equal(previous.GenerationId, Assert.Single(await store.GetChunksAsync(dataset.Id, false)).GenerationId);
        Assert.Single(await store.GetGenerationHistoryAsync(dataset.Id));
        using var reopened = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(reopened.CanWrite);
    }

    [Fact]
    public async Task Drift_hash_preserves_BOM_decoding_and_untrimmed_text_identity()
    {
        using var temp = new TempDir();
        var (store, pipeline, dataset, file) = await SeedAsync(temp, new RagIngestLimits());
        await File.WriteAllTextAsync(file, "data\n", new UnicodeEncoding(false, true));
        await pipeline.IngestDirectoryAsync(dataset, Path.GetDirectoryName(file)!);
        var previous = Assert.Single(await store.GetChunksAsync(dataset.Id, false));
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(5));

        var plan = await new WatchedSourceService(store, pipeline).ScanAsync(dataset);

        Assert.Empty(plan.Errors);
        Assert.Empty(plan.ChangedFiles);
        Assert.Equal(1, plan.UnchangedCount);
        Assert.Equal(previous.SourceHash, await pipeline.ComputeLocalSourceHashAsync(file, CancellationToken.None));
    }

    [Fact]
    public async Task PDF_drift_hash_matches_ingest_and_keeps_empty_PDF_identity()
    {
        using var temp = new TempDir();
        var store = new SqliteRagStore(NewSettings(temp));
        await store.InitializeAsync();
        var directory = temp.PathFor("pdfs");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "paper.pdf");
        WriteSimplePdf(file, "Digital PDF source");
        var pipeline = new RagPipeline(store, new FakeEmbeddingService());
        var dataset = new RagDataset { Name = "PDF scan" };
        dataset.Config.WatchedSources.Add(new RagWatchedSource { Root = directory });
        await pipeline.IngestDirectoryAsync(dataset, directory);

        var plan = await new WatchedSourceService(store, pipeline).ScanAsync(dataset);

        Assert.Empty(plan.Errors);
        Assert.Equal(1, plan.UnchangedCount);
        var previous = Assert.Single(await store.GetChunksAsync(dataset.Id, false));
        Assert.Equal(previous.SourceHash, await pipeline.ComputeLocalSourceHashAsync(file, CancellationToken.None));
        WriteSimplePdf(file, string.Empty);
        Assert.Equal(string.Empty, await pipeline.ComputeLocalSourceHashAsync(file, CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_during_the_last_source_hash_propagates_instead_of_returning_an_error_plan()
    {
        using var temp = new TempDir();
        var (store, pipeline, dataset, _) = await SeedAsync(temp, new RagIngestLimits());
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var service = new WatchedSourceService(store, pipeline, (_, ct) =>
        {
            calls++;
            cancellation.Cancel();
            return Task.FromCanceled<string>(ct);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ScanAsync(dataset, cancellation.Token));

        Assert.Equal(1, calls);
        Assert.Single(await store.GetGenerationHistoryAsync(dataset.Id));
    }

    private static async Task<(SqliteRagStore Store, RagPipeline Pipeline, RagDataset Dataset, string File)> SeedAsync(
        TempDir temp, RagIngestLimits limits)
    {
        var store = new SqliteRagStore(NewSettings(temp));
        await store.InitializeAsync();
        var directory = temp.PathFor("watched");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "data.txt");
        await File.WriteAllTextAsync(file, "data");
        var pipeline = new RagPipeline(store, new FakeEmbeddingService(), null, limits);
        var dataset = new RagDataset { Name = "bounded scan" };
        dataset.Config.WatchedSources.Add(new RagWatchedSource { Root = directory });
        await pipeline.IngestDirectoryAsync(dataset, directory);
        return (store, pipeline, dataset, file);
    }
}
