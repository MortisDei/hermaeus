using Hermaeus.Core.Models;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class MemoryHybridRecallTests
{
    /// <summary>
    /// Deterministic "semantic" fake: two known topics get near-identical
    /// vectors regardless of wording, anything else is orthogonal noise, so
    /// tests can assert real cosine-similarity behavior instead of just
    /// exercising the code path.
    /// </summary>
    private sealed class TopicEmbeddingService : IEmbeddingService
    {
        public int Dimensions => 3;

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(Embed(text));

        public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult(texts.Select(Embed).ToList());

        private static float[] Embed(string text)
        {
            var lower = text.ToLowerInvariant();
            if (lower.Contains("llama") || lower.Contains("local model runtime"))
                return [1f, 0f, 0f];
            if (lower.Contains("australian") || lower.Contains("spelling preference"))
                return [0f, 1f, 0f];
            return [0f, 0f, 1f];
        }
    }

    private static MemoryStore NewHybridStore(TempDir temp, out Hermaeus.Core.Services.ISettingsService settings)
    {
        var s = NewSettings(temp);
        s.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings = s;
        return new MemoryStore(s, new TopicEmbeddingService());
    }

    [Fact]
    public async Task Hybrid_search_surfaces_a_semantic_match_with_no_lexical_overlap()
    {
        using var temp = new TempDir();
        var store = NewHybridStore(temp, out _);
        await store.InitializeAsync();

        await store.SaveAsync(new Memory { Id = "m1", Content = "User prefers running llama.cpp for local inference." });
        await store.SaveAsync(new Memory { Id = "m2", Content = "User likes Australian English spelling preference, e.g. favour and colour." });
        await store.SaveAsync(new Memory { Id = "m3", Content = "Completely unrelated note about lunch plans." });

        // No lexical overlap with "local model runtime" at all, but it's the
        // same topic as m1 in the fake embedding space.
        var results = await store.SearchAsync("local model runtime");

        Assert.Contains(results, m => m.Id == "m1");
        Assert.DoesNotContain(results, m => m.Id == "m2");
        Assert.DoesNotContain(results, m => m.Id == "m3");
        var top = results.OrderByDescending(m => m.RelevanceScore).First();
        Assert.Equal("m1", top.Id);
        Assert.True(top.RelevanceScore > 0, "the top hybrid result should carry a positive relevance score");
    }

    /// <summary>r11 3.3: FTS candidates used to be ordered by is_pinned/importance_score/updated_at, so the "FTS rank" half of hybrid scoring measured importance, not how well the text matched. A short, term-dense match must now outrank a long, low-relevance one even though it has far lower importance.</summary>
    [Fact]
    public async Task Search_ranks_the_lexically_better_match_first_even_with_lower_importance()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new MemoryStore(settings);
        await store.InitializeAsync();

        await store.SaveAsync(new Memory
        {
            Id = "dense-match",
            Content = "rocket rocket launch mission control rocket",
            ImportanceScore = 0.1
        });
        await store.SaveAsync(new Memory
        {
            Id = "diluted-match",
            Content = string.Join(' ', Enumerable.Repeat("filler", 40)) + " rocket " + string.Join(' ', Enumerable.Repeat("padding", 40)),
            ImportanceScore = 0.9
        });

        var results = await store.SearchAsync("rocket");

        Assert.Contains(results, m => m.Id == "dense-match");
        Assert.Contains(results, m => m.Id == "diluted-match");
        var denseScore = results.Single(m => m.Id == "dense-match").RelevanceScore;
        var dilutedScore = results.Single(m => m.Id == "diluted-match").RelevanceScore;
        Assert.True(denseScore > dilutedScore,
            $"the term-dense, low-importance match (score {denseScore}) should rank above the diluted, high-importance match (score {dilutedScore})");
    }

    [Fact]
    public async Task Search_without_an_embedding_service_still_attaches_a_rank_based_score()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new MemoryStore(settings);
        await store.InitializeAsync();

        await store.SaveAsync(new Memory { Id = "m1", Content = "dotnet build fails without restore" });
        var results = await store.SearchAsync("dotnet build");

        Assert.Single(results);
        Assert.NotNull(results[0].RelevanceScore);
        Assert.True(results[0].RelevanceScore > 0);
    }

    [Fact]
    public async Task Keyword_fallback_keeps_more_than_two_genuine_lexical_matches()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new MemoryStore(settings);
        await store.InitializeAsync();

        await store.SaveAsync(new Memory { Id = "pizza", Content = "Pizza pairs well with a medium-bodied red wine." });
        await store.SaveAsync(new Memory { Id = "pasta", Content = "Pasta can pair with red wine." });
        await store.SaveAsync(new Memory { Id = "cheese", Content = "Cheese also pairs with red wine." });

        var results = await store.SearchAsync("wine");

        Assert.Equal(3, results.Count);
        Assert.All(results, result => Assert.True(result.RelevanceScore >= 0.4));
    }

    [Fact]
    public async Task Unembedded_rows_keep_more_than_two_genuine_lexical_matches_with_a_configured_embedder()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

        var plainStore = new MemoryStore(settings);
        await plainStore.InitializeAsync();
        await plainStore.SaveAsync(new Memory { Id = "pizza", Content = "Pizza pairs well with a medium-bodied red wine." });
        await plainStore.SaveAsync(new Memory { Id = "pasta", Content = "Pasta can pair with red wine." });
        await plainStore.SaveAsync(new Memory { Id = "cheese", Content = "Cheese also pairs with red wine." });

        var hybridStore = new MemoryStore(settings, new TopicEmbeddingService());
        var results = await hybridStore.SearchAsync("wine");

        Assert.Equal(3, results.Count);
        Assert.All(results, result => Assert.True(result.RelevanceScore >= 0.4));
    }

    [Fact]
    public async Task Weak_ordinary_and_unrelated_pinned_memories_are_not_injected_by_recall()
    {
        using var temp = new TempDir();
        var store = NewHybridStore(temp, out _);
        await store.InitializeAsync();

        await store.SaveAsync(new Memory { Id = "relevant", Content = "User runs llama.cpp for local inference." });
        await store.SaveAsync(new Memory { Id = "pinned-relevant", Content = "Pinned note: user runs llama.cpp for local inference.", IsPinned = true });
        await store.SaveAsync(new Memory { Id = "ordinary-unrelated", Content = "User's unrelated lunch plans and grocery list." });
        await store.SaveAsync(new Memory { Id = "pinned-unrelated", Content = "Pinned context retained by deliberate user choice.", IsPinned = true });

        var results = await store.SearchAsync("local model runtime");

        Assert.Contains(results, memory => memory.Id == "relevant");
        Assert.Contains(results, memory => memory.Id == "pinned-relevant");
        Assert.DoesNotContain(results, memory => memory.Id == "ordinary-unrelated");
        Assert.DoesNotContain(results, memory => memory.Id == "pinned-unrelated");
    }

    [Fact]
    public async Task A_model_generated_absence_conclusion_cannot_hide_later_positive_evidence()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new MemoryStore(settings);
        await store.InitializeAsync();

        await store.SaveAsync(new Memory
        {
            Id = "old-negative",
            Content = "No prior discussion about food paired with wine was found.",
            IsPinned = true
        });
        await store.SaveAsync(new Memory
        {
            Id = "later-positive",
            Content = "Recommended wine pairing for meat lovers pizza."
        });

        var results = await store.SearchAsync("wine pizza");

        Assert.Contains(results, memory => memory.Id == "later-positive");
        Assert.DoesNotContain(results, memory => memory.Id == "old-negative");
    }

    [Fact]
    public async Task Preexisting_rows_are_backfilled_with_embeddings_after_a_background_pass()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

        // Save without an embedding service configured, so this row starts with no embedding.
        var plainStore = new MemoryStore(settings);
        await plainStore.InitializeAsync();
        await plainStore.SaveAsync(new Memory { Id = "m1", Content = "User prefers running llama.cpp for local inference." });

        // Backfill runs off the send path (r9 01-send-path-latency.md 1.2): a
        // hybrid search never embeds anything but the query, so an explicit
        // background pass is what makes the preexisting row vector-recallable.
        var hybridStore = new MemoryStore(settings, new TopicEmbeddingService());
        await hybridStore.RunEmbeddingBackfillAsync();
        var results = await hybridStore.SearchAsync("local model runtime");

        Assert.Contains(results, m => m.Id == "m1" && m.RelevanceScore > 0.4);
    }

    [Fact]
    public async Task MarkRecalledAsync_bumps_recall_count_and_last_recalled_at()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new MemoryStore(settings);
        await store.InitializeAsync();

        await store.SaveAsync(new Memory { Id = "m1", Content = "note" });
        var before = await store.GetByIdAsync("m1");
        Assert.Equal(0, before!.RecallCount);
        Assert.Null(before.LastRecalledAt);

        await store.MarkRecalledAsync(["m1"]);
        await store.MarkRecalledAsync(["m1", "missing-id"]);

        var after = await store.GetByIdAsync("m1");
        Assert.Equal(2, after!.RecallCount);
        Assert.NotNull(after.LastRecalledAt);
    }

    [Fact]
    public async Task Saving_a_memory_does_not_clobber_an_existing_embedding_when_the_embedding_service_is_unavailable()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

        var hybridStore = new MemoryStore(settings, new TopicEmbeddingService());
        await hybridStore.InitializeAsync();
        await hybridStore.SaveAsync(new Memory { Id = "m1", Content = "User prefers running llama.cpp for local inference." });

        // Re-save the same row through a plain store (no embedding service);
        // its previously computed embedding should survive.
        var plainStore = new MemoryStore(settings);
        var reloaded = await plainStore.GetByIdAsync("m1");
        reloaded!.ImportanceScore = 0.9;
        await plainStore.SaveAsync(reloaded);

        var results = await hybridStore.SearchAsync("local model runtime");
        Assert.Contains(results, m => m.Id == "m1" && m.RelevanceScore > 0.4);
    }
}
