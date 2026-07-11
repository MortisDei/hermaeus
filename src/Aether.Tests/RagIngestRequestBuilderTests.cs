using Aether.Rag;
using Aether.Rag.Models;
using Xunit;

namespace Aether.Tests;

public sealed class RagIngestRequestBuilderTests
{
    [Fact]
    public void PrepareDataset_configures_a_new_local_dataset()
    {
        var ds = RagIngestRequestBuilder.PrepareDataset(
            existing: null,
            newDatasetName: "My Docs",
            enableWebLoader: false,
            ingestPath: "/data/docs",
            webUrlList: string.Empty,
            webMaxPages: 5,
            useParentChild: true,
            embeddingModel: "text-embed-1");

        Assert.Equal("My Docs", ds.Name);
        Assert.Equal("Ingested from /data/docs", ds.Description);
        Assert.True(ds.Config.UseParentChild);
        Assert.Equal("text-embed-1", ds.Config.EmbeddingModel);
        Assert.False(ds.Config.EnableWebLoader);
        Assert.Equal(RagExtractionMode.TextMarkdown, ds.Config.ExtractionMode);
    }

    [Fact]
    public void PrepareDataset_configures_a_new_web_dataset_and_clamps_max_pages()
    {
        var ds = RagIngestRequestBuilder.PrepareDataset(
            existing: null,
            newDatasetName: "Web Docs",
            enableWebLoader: true,
            ingestPath: string.Empty,
            webUrlList: " https://example.com ",
            webMaxPages: 0,
            useParentChild: false,
            embeddingModel: "text-embed-1");

        Assert.Equal("Ingested from explicitly configured web URLs", ds.Description);
        Assert.Equal("https://example.com", ds.Config.WebUrlList);
        Assert.Equal(5, ds.Config.WebMaxPages);
        Assert.Equal(RagExtractionMode.WebUrl, ds.Config.ExtractionMode);
    }

    [Fact]
    public void PrepareDataset_updates_path_and_timestamp_on_an_existing_dataset()
    {
        var existing = new RagDataset { Name = "Existing", LastIngestPath = "/old" };

        var ds = RagIngestRequestBuilder.PrepareDataset(
            existing,
            newDatasetName: "ignored",
            enableWebLoader: false,
            ingestPath: "/new/path",
            webUrlList: string.Empty,
            webMaxPages: 5,
            useParentChild: false,
            embeddingModel: "text-embed-1");

        Assert.Same(existing, ds);
        Assert.Equal("Existing", ds.Name);
        Assert.Equal("/new/path", ds.LastIngestPath);
        Assert.NotNull(ds.LastIngestUtc);
    }

    [Fact]
    public void BuildHealthSummary_includes_only_nonzero_sections()
    {
        var health = new RagIngestHealth { FileCount = 3, DuplicateChunkCount = 2 };

        var summary = RagIngestRequestBuilder.BuildHealthSummary(health);

        Assert.Equal("Files: 3; Duplicate chunks: 2", summary);
    }

    [Fact]
    public void BuildHealthSummary_includes_warnings_when_present()
    {
        var health = new RagIngestHealth { FileCount = 1, Warnings = ["skipped a binary file"] };

        var summary = RagIngestRequestBuilder.BuildHealthSummary(health);

        Assert.Equal("Files: 1; skipped a binary file", summary);
    }
}
