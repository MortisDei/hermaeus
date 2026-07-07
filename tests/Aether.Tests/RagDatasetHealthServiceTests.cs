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
}
