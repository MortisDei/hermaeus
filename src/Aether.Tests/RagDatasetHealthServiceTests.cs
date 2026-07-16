using Aether.Rag;
using Aether.Rag.Models;
using Xunit;

namespace Aether.Tests;

public sealed class RagDatasetHealthServiceTests
{
    [Fact]
    public void Compute_counts_sources_duplicates_missing_and_stale_files()
    {
        using var temp = new TempDir();
        var freshPath = temp.PathFor("fresh.md");
        var stalePath = temp.PathFor("stale.md");
        File.WriteAllText(freshPath, "fresh");
        File.WriteAllText(stalePath, "stale");

        var chunks = new List<RagChunk>
        {
            new() { SourcePath = freshPath, ChunkIndex = 0, SourceModifiedUtc = File.GetLastWriteTimeUtc(freshPath) },
            new() { SourcePath = stalePath, ChunkIndex = 0, SourceModifiedUtc = DateTime.UtcNow.AddDays(-1) },
            new() { SourcePath = stalePath, ChunkIndex = 0, SourceModifiedUtc = DateTime.UtcNow.AddDays(-1) }, // duplicate chunk index
            new() { SourcePath = temp.PathFor("missing.md"), ChunkIndex = 0 },
            new() { SourcePath = "https://example.com/page", ChunkIndex = 0 }
        };

        var health = RagDatasetHealthService.Compute(chunks);

        Assert.Equal(4, health.SourceCount);
        Assert.Equal(1, health.DuplicateSources);
        Assert.Equal(1, health.MissingFiles);
        Assert.Equal(1, health.StaleFiles);
        Assert.Equal([temp.PathFor("missing.md")], health.MissingSourcePaths);
    }

    [Fact]
    public void Compute_ignores_http_sources_for_missing_and_stale_checks()
    {
        var chunks = new List<RagChunk>
        {
            new() { SourcePath = "https://example.com/a", ChunkIndex = 0 },
            new() { SourcePath = "http://example.com/b", ChunkIndex = 0 }
        };

        var health = RagDatasetHealthService.Compute(chunks);

        Assert.Equal(2, health.SourceCount);
        Assert.Equal(0, health.MissingFiles);
        Assert.Equal(0, health.StaleFiles);
    }

    [Fact]
    public void Compute_produces_identical_results_from_full_chunks_and_the_lightweight_projection()
    {
        // r10 02-rag-quality.md 2.5: the lightweight overload must agree
        // with the full-chunk overload exactly; the RAG tab's refresh path
        // switches to it precisely because it's cheaper, not different.
        using var temp = new TempDir();
        var freshPath = temp.PathFor("fresh.md");
        var stalePath = temp.PathFor("stale.md");
        File.WriteAllText(freshPath, "fresh");
        File.WriteAllText(stalePath, "stale");

        var chunks = new List<RagChunk>
        {
            new() { SourcePath = freshPath, ChunkIndex = 0, Content = "full body text that health does not need", SourceModifiedUtc = File.GetLastWriteTimeUtc(freshPath) },
            new() { SourcePath = stalePath, ChunkIndex = 0, Content = "more body text", SourceModifiedUtc = DateTime.UtcNow.AddDays(-1) },
            new() { SourcePath = stalePath, ChunkIndex = 0, Content = "duplicate chunk index body", SourceModifiedUtc = DateTime.UtcNow.AddDays(-1) },
            new() { SourcePath = temp.PathFor("missing.md"), ChunkIndex = 0, Content = "body for a source that no longer exists" },
            new() { SourcePath = "https://example.com/page", ChunkIndex = 0, Content = "web body" }
        };
        var projection = chunks.Select(c => new RagChunkHealthInfo(c.SourcePath, c.ChunkIndex, c.SourceModifiedUtc)).ToList();

        var fromFullChunks = RagDatasetHealthService.Compute(chunks);
        var fromProjection = RagDatasetHealthService.Compute(projection);

        // RagDatasetHealth's record-equality would compare MissingSourcePaths
        // (IReadOnlyList<string>) by reference; Assert.Equivalent compares
        // structurally instead, which is what "identical results" means here.
        Assert.Equivalent(fromFullChunks, fromProjection, strict: true);
    }
}
