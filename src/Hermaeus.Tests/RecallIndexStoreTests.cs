using Hermaeus.Rag.Embeddings;
using Hermaeus.Services.Recall;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class RecallIndexStoreTests
{
    private static RecallIndexStore NewStore(TempDir temp, bool withEmbeddings = true)
    {
        var s = NewSettings(temp);
        s.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        return new RecallIndexStore(s, withEmbeddings ? new FakeEmbeddingService() : null);
    }

    private static RecallEntry Entry(string kind, string sourceId, string subId, string body, string projectId = "", bool archived = false) => new()
    {
        Id = RecallIndexStore.MakeId(kind, sourceId, subId),
        Kind = kind,
        SourceId = sourceId,
        SubId = subId,
        ProjectId = projectId,
        Title = "Title " + sourceId,
        Body = body,
        IsArchived = archived,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Failed_embedding_is_persisted_and_can_retry_after_store_recreation()
    {
        using var temp = new TempDir();
        var failed = new ToggleEmbeddingService { Fail = true };
        var settings = NewSettings(temp);
        var dataRoot = temp.PathFor("data");
        settings.Settings.DataManagement.DataRootDirectory = dataRoot;
        var first = new RecallIndexStore(settings, failed, backfillCooldown: TimeSpan.Zero, automaticRetry: false);
        await first.UpsertBatchAsync([Entry("message", "c1", "0", "retryable recall content")]);

        var failure = await first.RunEmbeddingBackfillAsync();
        Assert.Equal(1, failure.FailedCount);
        Assert.Equal(1, failure.PendingCount);
        Assert.Equal(nameof(InvalidOperationException), failure.LastFailure);

        var persisted = await first.GetEmbeddingBackfillStatusAsync();
        Assert.Equal(1, persisted.PendingCount);
        Assert.Equal(1, persisted.DeferredCount);
        Assert.Equal(nameof(InvalidOperationException), persisted.LastFailure);

        failed.Fail = false;
        var recreated = new RecallIndexStore(settings, failed, backfillCooldown: TimeSpan.Zero, automaticRetry: false);
        var success = await recreated.RunEmbeddingBackfillAsync();

        Assert.Equal(1, success.EmbeddedCount);
        Assert.Equal(0, (await recreated.GetEmbeddingBackfillStatusAsync()).PendingCount);
    }

    [Fact]
    public async Task Embedding_failure_budget_is_bounded_and_exposed_as_exhausted()
    {
        using var temp = new TempDir();
        var failed = new ToggleEmbeddingService { Fail = true };
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new RecallIndexStore(settings, failed, backfillCooldown: TimeSpan.Zero, automaticRetry: false);
        await store.UpsertBatchAsync([Entry("message", "c1", "0", "content that always fails")]);

        for (var i = 0; i < 6; i++)
            await store.RunEmbeddingBackfillAsync();

        Assert.Equal(5, failed.Calls);
        var status = await store.GetEmbeddingBackfillStatusAsync();
        Assert.Equal(1, status.ExhaustedCount);
        Assert.Equal(1, status.PendingCount);
        Assert.Equal(nameof(InvalidOperationException), status.LastFailure);
    }

    [Fact]
    public async Task Upserting_the_same_source_twice_is_an_update_not_a_duplicate()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.UpsertBatchAsync([Entry("message", "c1", "0", "hello world")]);
        await store.UpsertBatchAsync([Entry("message", "c1", "0", "hello world, edited")]);

        var (results, _) = await store.SearchAsync("message", "edited", "");
        var hit = Assert.Single(results);
        Assert.Contains("edited", hit.Body, StringComparison.Ordinal);

        var (count, _) = await store.GetSizeAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task DeleteBySourceAsync_removes_every_entry_for_that_source_only()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.UpsertBatchAsync([
            Entry("message", "c1", "0", "keep this one out"),
            Entry("message", "c1", "1", "also gone"),
            Entry("message", "c2", "0", "unrelated stays")
        ]);

        await store.DeleteBySourceAsync("message", "c1");

        var (count, _) = await store.GetSizeAsync();
        Assert.Equal(1, count);
        var (results, _) = await store.SearchAsync("message", "unrelated", "");
        Assert.Single(results);
    }

    [Fact]
    public async Task ClearAsync_deletes_every_row_and_reports_the_count_removed()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.UpsertBatchAsync([Entry("message", "c1", "0", "one"), Entry("task", "t1", "", "two")]);

        var removed = await store.ClearAsync();

        Assert.Equal(2, removed);
        var (count, _) = await store.GetSizeAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Archived_entries_are_excluded_by_default_and_included_on_request()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.UpsertBatchAsync([Entry("message", "c1", "0", "archived body text", archived: true)]);

        var (defaultResults, _) = await store.SearchAsync("message", "archived", "");
        Assert.Empty(defaultResults);

        var (withArchived, _) = await store.SearchAsync("message", "archived", "", includeArchived: true);
        Assert.Single(withArchived);
    }

    [Fact]
    public async Task Project_scope_filters_results_to_that_project_only()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.UpsertBatchAsync([
            Entry("message", "c1", "0", "scoped content here", projectId: "p1"),
            Entry("message", "c2", "0", "scoped content here", projectId: "p2")
        ]);

        var (scoped, _) = await store.SearchAsync("message", "scoped", "p1");
        Assert.Single(scoped);
        Assert.Equal("c1", scoped[0].SourceId);
    }

    [Fact]
    public async Task No_embedding_service_configured_degrades_to_keyword_only_honestly()
    {
        using var temp = new TempDir();
        var store = NewStore(temp, withEmbeddings: false);
        await store.UpsertBatchAsync([Entry("message", "c1", "0", "keyword only search text")]);

        var (results, keywordOnly) = await store.SearchAsync("message", "keyword", "");
        Assert.True(keywordOnly);
        Assert.Single(results);
    }

    [Fact]
    public async Task A_dimension_mismatched_embedding_is_skipped_for_semantic_scoring_not_scored_as_garbage()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.UpsertBatchAsync([Entry("message", "c1", "0", "drifted vector text")]);
        await store.RunEmbeddingBackfillAsync();

        // Simulate an embedding-model switch: FakeEmbeddingService always
        // produces 4-dim vectors, so hand-write a mismatched dimension directly.
        var s = NewSettings(temp);
        s.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        await using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(temp.PathFor("data"), "recall.db")}"))
        {
            await c.OpenAsync();
            var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE recall_entries SET embedding_dim = 999";
            await cmd.ExecuteNonQueryAsync();
        }

        // A query against a 999-dim stored vector must not throw and must not
        // silently rank it via a bogus cosine score; it just gets skipped.
        var (results, keywordOnly) = await store.SearchAsync("message", "drifted", "");
        Assert.False(keywordOnly);
        var hit = Assert.Single(results);
        Assert.Equal(0.0, hit.RelevanceScore); // never reranked, since the mismatched vector was skipped
    }

    [Fact]
    public async Task GetIndexedSourceIdsAsync_reflects_upserted_sources()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.UpsertBatchAsync([Entry("task", "t1", "", "goal text")]);

        var ids = await store.GetIndexedSourceIdsAsync("task");
        Assert.Contains("t1", ids);
        Assert.DoesNotContain("t2", ids);
    }

    [Fact]
    public async Task GetTitleAsync_resolves_a_parent_tasks_title_for_sub_task_labeling()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.UpsertBatchAsync([Entry("task", "parent1", "", "parent goal text")]);

        var title = await store.GetTitleAsync("task", "parent1");
        Assert.Equal("Title parent1", title);
    }

    private sealed class ToggleEmbeddingService : IEmbeddingService
    {
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public int Dimensions => 4;

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            Calls++;
            if (Fail)
                throw new InvalidOperationException("embedding endpoint unavailable");
            return Task.FromResult(new[] { 1f, 2f, 3f, 4f });
        }

        public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult(texts.Select(_ => new[] { 1f, 2f, 3f, 4f }).ToList());
    }
}
