using Aether.Core.Models;
using Aether.Rag.Embeddings;
using Aether.Services;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

public sealed class MemoryEmbeddingBackfillTests
{
    /// <summary>Counts embed calls and records every text it was asked to embed, with pluggable failure/hang behavior.</summary>
    private sealed class CountingEmbeddingService : IEmbeddingService
    {
        public int Dimensions => 3;
        public int CallCount;
        public readonly List<string> Requests = [];
        public bool FailAll;
        public bool HangForever;

        public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            Interlocked.Increment(ref CallCount);
            lock (Requests) Requests.Add(text);

            if (HangForever)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }

            if (FailAll)
                throw new InvalidOperationException("simulated embedding failure");

            return [1f, 0f, 0f];
        }

        public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult(texts.Select(_ => new[] { 1f, 0f, 0f }).ToList());
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    [Fact]
    public async Task SearchAsync_embeds_only_the_query_regardless_of_unembedded_row_count()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var embeddings = new CountingEmbeddingService();
        var store = new MemoryStore(settings, embeddings);
        await store.InitializeAsync();

        for (var i = 0; i < 10; i++)
            await store.SaveAsync(new Memory { Id = $"m{i}", Content = $"note number {i}" });

        // Every SaveAsync above already embedded its own row (existing
        // behavior), so reset the counter to isolate what SearchAsync itself does.
        embeddings.CallCount = 0;
        embeddings.Requests.Clear();

        await store.SearchAsync("note");

        Assert.Equal(1, embeddings.CallCount);
        Assert.Equal(["note"], embeddings.Requests);
    }

    [Fact]
    public async Task RunEmbeddingBackfillAsync_embeds_unembedded_rows_off_the_search_path()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

        // Written with no embedding service, so these rows start unembedded.
        var plainStore = new MemoryStore(settings);
        await plainStore.InitializeAsync();
        await plainStore.SaveAsync(new Memory { Id = "m1", Content = "unembedded row" });

        var embeddings = new CountingEmbeddingService();
        var store = new MemoryStore(settings, embeddings);
        await store.RunEmbeddingBackfillAsync();

        Assert.Equal(1, embeddings.CallCount);
    }

    [Fact]
    public async Task RunEmbeddingBackfillAsync_logs_one_warning_per_batch_not_per_row()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

        var plainStore = new MemoryStore(settings);
        await plainStore.InitializeAsync();
        for (var i = 0; i < 5; i++)
            await plainStore.SaveAsync(new Memory { Id = $"m{i}", Content = $"row {i}" });

        var embeddings = new CountingEmbeddingService { FailAll = true };
        var runtimeLogs = new RuntimeLogService(settings);
        var store = new MemoryStore(settings, embeddings, runtimeLogs);

        await store.RunEmbeddingBackfillAsync();

        Assert.Equal(5, embeddings.CallCount);
        var warnings = runtimeLogs.GetEntries().Where(e => e.Level == RuntimeLogLevel.Warning).ToList();
        Assert.Single(warnings);
    }

    [Fact]
    public async Task RunEmbeddingBackfillAsync_does_not_retry_a_failed_row_before_the_cooldown_elapses()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

        var plainStore = new MemoryStore(settings);
        await plainStore.InitializeAsync();
        await plainStore.SaveAsync(new Memory { Id = "m1", Content = "row" });

        var embeddings = new CountingEmbeddingService { FailAll = true };
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new MemoryStore(settings, embeddings, timeProvider: clock, backfillCooldown: TimeSpan.FromMinutes(10));

        await store.RunEmbeddingBackfillAsync();
        Assert.Equal(1, embeddings.CallCount);

        // Immediately re-running should not retry the row: still within cooldown.
        await store.RunEmbeddingBackfillAsync();
        Assert.Equal(1, embeddings.CallCount);

        // Advance past the cooldown: the row becomes eligible again.
        clock.Advance(TimeSpan.FromMinutes(11));
        await store.RunEmbeddingBackfillAsync();
        Assert.Equal(2, embeddings.CallCount);
    }

    [Fact]
    public async Task SearchAsync_falls_back_to_FTS_ranking_quickly_when_query_embedding_hangs()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

        var embeddings = new CountingEmbeddingService();
        var store = new MemoryStore(settings, embeddings);
        await store.InitializeAsync();
        await store.SaveAsync(new Memory { Id = "m1", Content = "dotnet build fails without restore" });

        // Now make the query embed hang forever; the fast-fail timeout (r9
        // 01-send-path-latency.md 1.3) must still return within a few seconds
        // instead of inheriting the HTTP client's 60 s timeout.
        embeddings.HangForever = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var results = await store.SearchAsync("dotnet build");
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"expected a fast fallback, took {sw.Elapsed}");
        Assert.Contains(results, m => m.Id == "m1");
    }

    [Fact]
    public async Task SearchAsync_logs_the_query_embed_fallback_warning_only_once()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

        // Written through a plain store (no embedding service) so the save
        // path itself never spawns a competing background backfill pass;
        // this test is only about the query-embed fallback warning latch.
        var plainStore = new MemoryStore(settings);
        await plainStore.InitializeAsync();
        await plainStore.SaveAsync(new Memory { Id = "m1", Content = "note" });

        var embeddings = new CountingEmbeddingService { FailAll = true };
        var runtimeLogs = new RuntimeLogService(settings);
        var store = new MemoryStore(settings, embeddings, runtimeLogs);

        await store.SearchAsync("note");
        await store.SearchAsync("note");
        await store.SearchAsync("note");

        var warnings = runtimeLogs.GetEntries().Where(e => e.Level == RuntimeLogLevel.Warning).ToList();
        Assert.Single(warnings);
    }
}
