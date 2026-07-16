using Aether.Rag.Models;
using Aether.Rag.Retrieval;
using Xunit;

namespace Aether.Tests;

/// <summary>r10 02-rag-quality.md 2.4: each chunk must be tokenized at most once per query, regardless of query-variant count.</summary>
public sealed class Bm25ScorerTests
{
    private static List<RagChunk> BuildCorpus() =>
    [
        new() { Id = "c1", Content = "apple banana carrot delta echo foxtrot" },
        new() { Id = "c2", Content = "golf hotel india juliet kilo lima" },
        new() { Id = "c3", Content = "apple mike november oscar papa quebec" },
        new() { Id = "c4", Content = "romeo sierra tango uniform victor apple" }
    ];

    [Fact]
    public void Score_with_precomputed_tf_index_tokenizes_each_chunk_at_most_once_per_query()
    {
        var corpus = BuildCorpus();
        var stats = Bm25Scorer.BuildStats(corpus);
        var scorer = new Bm25Scorer();
        var variants = new[] { "apple banana", "apple mike", "apple romeo" };

        var before = Bm25Scorer.TfComputations;
        var tfIndex = Bm25Scorer.BuildTfIndex(corpus);
        var afterIndexBuild = Bm25Scorer.TfComputations;
        Assert.Equal(corpus.Count, afterIndexBuild - before);

        foreach (var variant in variants)
            scorer.Score(variant, corpus, stats, tfIndex);

        var afterAllVariants = Bm25Scorer.TfComputations;
        Assert.Equal(corpus.Count, afterAllVariants - before);
    }

    [Fact]
    public void Score_with_precomputed_tf_index_matches_scores_from_the_recompute_every_call_path()
    {
        var corpus = BuildCorpus();
        var stats = Bm25Scorer.BuildStats(corpus);
        var scorer = new Bm25Scorer();
        const string query = "apple banana mike";

        var expected = scorer.Score(query, corpus, stats)
            .OrderBy(s => s.Chunk.Id, StringComparer.Ordinal)
            .Select(s => s.Score)
            .ToList();

        var tfIndex = Bm25Scorer.BuildTfIndex(corpus);
        var actual = scorer.Score(query, corpus, stats, tfIndex)
            .OrderBy(s => s.Chunk.Id, StringComparer.Ordinal)
            .Select(s => s.Score)
            .ToList();

        Assert.Equal(expected, actual);
    }
}
