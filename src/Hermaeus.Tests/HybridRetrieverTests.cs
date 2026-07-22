using Hermaeus.Rag.Models;
using Hermaeus.Rag.Retrieval;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>r10 02-rag-quality.md 2.3: structural boosts must nudge the fusion, not dominate it.</summary>
public sealed class HybridRetrieverTests
{
    [Fact]
    public void Fuse_rank1_semantic_candidate_with_no_metadata_match_still_beats_rank8_with_every_boost_firing()
    {
        const string query = "database performance tuning page 42";

        var winner = new RagChunk { Id = "winner", Content = "irrelevant unrelated content" };
        var filler = Enumerable.Range(0, 6)
            .Select(i => new RagChunk { Id = $"filler-{i}", Content = "irrelevant unrelated content" })
            .ToList();

        // Every metadata boost line fires for this chunk: content/title/heading/symbol
        // phrase match, heading/symbol/event term match, page match, and freshness.
        var boosted = new RagChunk
        {
            Id = "boosted",
            Content = $"... {query} ...",
            SourceTitle = query,
            HeadingPath = query,
            CodeSymbolInfo = query,
            EventType = "42",
            PageNumber = 42,
            SourceModifiedUtc = DateTime.UtcNow
        };

        // Semantic-only, worst case: no BM25 contribution reinforcing the winner's rank.
        var semantic = new List<ScoredChunk> { new(winner, 0.9f, ScoreSource.Semantic) };
        semantic.AddRange(filler.Select(c => new ScoredChunk(c, 0.5f, ScoreSource.Semantic)));
        semantic.Add(new ScoredChunk(boosted, 0.2f, ScoreSource.Semantic));

        var fused = HybridRetriever.Fuse(query, semantic, bm25: [], topK: 10);

        Assert.Equal("winner", fused[0].Chunk.Id);
    }

    [Fact]
    public void Fuse_prefers_the_candidate_with_a_heading_match_between_two_near_equal_fused_scores()
    {
        const string query = "kubernetes deployment rollback";

        var filler = Enumerable.Range(0, 9)
            .Select(i => new RagChunk { Id = $"filler-{i}", Content = "irrelevant unrelated content" })
            .ToList();
        var noBoost = new RagChunk { Id = "no-boost", Content = "irrelevant unrelated content" };
        var headingMatch = new RagChunk { Id = "heading-match", Content = "irrelevant unrelated content", HeadingPath = query };

        // Adjacent RRF ranks (9 and 10) produce a near-equal fused score gap
        // (~1.4% at defaults); a heading phrase+term match (~2% boost) should
        // be enough to lift the lower-ranked candidate above the higher one.
        var semantic = filler.Select(c => new ScoredChunk(c, 0.5f, ScoreSource.Semantic)).ToList();
        semantic.Add(new ScoredChunk(noBoost, 0.4f, ScoreSource.Semantic));
        semantic.Add(new ScoredChunk(headingMatch, 0.39f, ScoreSource.Semantic));

        var fused = HybridRetriever.Fuse(query, semantic, bm25: [], topK: 20);

        var noBoostRank = fused.FindIndex(s => s.Chunk.Id == "no-boost");
        var headingMatchRank = fused.FindIndex(s => s.Chunk.Id == "heading-match");
        Assert.True(headingMatchRank >= 0 && noBoostRank >= 0);
        Assert.True(headingMatchRank < noBoostRank,
            $"the heading-match candidate (rank {headingMatchRank}) should outrank the near-tied candidate with no boost (rank {noBoostRank})");
    }
}
