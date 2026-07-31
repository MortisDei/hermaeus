using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Services.Recall;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class RecallIndexingServiceTests
{
    private static (RecallIndexingService Indexing, RecallIndexStore Store, ISettingsService Settings) New(TempDir temp)
    {
        var s = NewSettings(temp);
        s.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new RecallIndexStore(s, new FakeEmbeddingService());
        var indexing = new RecallIndexingService(store, s);
        return (indexing, store, s);
    }

    private static Conversation Conv(string id, params (string role, string content)[] messages) => new()
    {
        Id = id,
        Title = "Conv " + id,
        Messages = messages.Select(m => new Message { Role = m.role, Content = m.content }).ToList()
    };

    [Fact]
    public async Task Switching_indexing_off_means_no_rows_are_written()
    {
        using var temp = new TempDir();
        var (indexing, store, settings) = New(temp);
        settings.Settings.Memory.RecallIndexingEnabled = false;

        await indexing.IndexConversationAsync(Conv("c1", ("user", "a real message that is long enough")));
        await indexing.IndexTaskAsync(new RecallTaskInput("t1", null, "Goal", "Body text here", "", DateTime.UtcNow));

        var (count, _) = await store.GetSizeAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task System_empty_and_short_messages_are_skipped()
    {
        using var temp = new TempDir();
        var (indexing, store, _) = New(temp);
        var conv = Conv("c1",
            ("system", "you are a helpful assistant with a long system prompt"),
            ("user", ""),
            ("user", "hi"),
            ("assistant", "This is a real, long enough response to index."));

        await indexing.IndexConversationAsync(conv);

        var (count, _) = await store.GetSizeAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Re_indexing_a_conversation_is_an_upsert_not_a_duplicate()
    {
        using var temp = new TempDir();
        var (indexing, store, _) = New(temp);
        var conv = Conv("c1", ("user", "the original long enough message"));

        await indexing.IndexConversationAsync(conv);
        conv.Messages[0].Content = "an edited, still long enough message";
        await indexing.IndexConversationAsync(conv);

        var (count, _) = await store.GetSizeAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Excluding_a_conversation_removes_its_entries_immediately_and_blocks_reindexing()
    {
        using var temp = new TempDir();
        var (indexing, store, _) = New(temp);
        var conv = Conv("c1", ("user", "a message that should disappear"));
        await indexing.IndexConversationAsync(conv);
        var (before, _) = await store.GetSizeAsync();
        Assert.Equal(1, before);

        conv.RecallExcluded = true;
        await indexing.IndexConversationAsync(conv);

        var (after, _) = await store.GetSizeAsync();
        Assert.Equal(0, after);
    }

    [Fact]
    public async Task Deleting_a_conversation_removes_its_entries_in_the_same_operation()
    {
        using var temp = new TempDir();
        var (indexing, store, _) = New(temp);
        await indexing.IndexConversationAsync(Conv("c1", ("user", "a message worth keeping around")));

        await indexing.RemoveConversationAsync("c1");

        var (count, _) = await store.GetSizeAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Task_entry_composes_goal_and_body_and_carries_parent_linkage()
    {
        using var temp = new TempDir();
        var (indexing, store, _) = New(temp);
        var input = new RecallTaskInput("child1", "parent1", "Child goal", "goal summary reservations plan", "proj1", DateTime.UtcNow);

        await indexing.IndexTaskAsync(input);

        var (results, _) = await store.SearchAsync("task", "goal", "");
        var hit = Assert.Single(results);
        Assert.Equal("child1", hit.SourceId);
        Assert.Equal("parent1", hit.SubId);
        Assert.Equal("proj1", hit.ProjectId);
    }

    [Fact]
    public async Task Clear_index_leaves_zero_rows()
    {
        using var temp = new TempDir();
        var (indexing, store, _) = New(temp);
        await indexing.IndexConversationAsync(Conv("c1", ("user", "content that gets cleared away")));

        var removed = await indexing.ClearIndexAsync();

        Assert.Equal(1, removed);
        var (count, _) = await store.GetSizeAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Startup_backfill_indexes_conversations_and_tasks_never_indexed_before_but_is_bounded()
    {
        using var temp = new TempDir();
        var (indexing, store, _) = New(temp);

        var conversations = Enumerable.Range(0, 3)
            .Select(i => Conv($"c{i}", ("user", $"backfilled conversation message number {i}")))
            .ToList();
        var tasks = Enumerable.Range(0, 2)
            .Select(i => new RecallTaskInput($"t{i}", null, $"Goal {i}", $"Body {i}", "", DateTime.UtcNow))
            .ToList();

        // r27 05 5.1: the backfill takes the lightweight projection and loads a
        // full conversation only for the ids it is actually going to index.
        var byId = conversations.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var summaries = conversations.Select(ConversationSummary.From).ToList();
        var loads = 0;
        Task<Conversation?> Load(string id, CancellationToken ct)
        {
            loads++;
            return Task.FromResult(byId.GetValueOrDefault(id));
        }

        await indexing.RunStartupBackfillAsync(summaries, tasks, Load);

        var (count, _) = await store.GetSizeAsync();
        Assert.Equal(5, count);
        Assert.Equal(3, loads);

        // A second run must not duplicate anything already indexed, and must
        // not re-read a single conversation to find that out.
        await indexing.RunStartupBackfillAsync(summaries, tasks, Load);
        Assert.Equal(3, loads);
        var (countAfter, _) = await store.GetSizeAsync();
        Assert.Equal(5, countAfter);
    }
}
