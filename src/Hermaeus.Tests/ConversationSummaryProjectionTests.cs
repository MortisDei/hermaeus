using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r27 05-small-open-items.md 5.1. The conversation sidebar shows titles,
/// folders, tags, and pinned and archived flags. Drawing it used to deserialise
/// every message of every conversation and then walk them again to backfill
/// parent links.
/// </summary>
public sealed class ConversationSummaryProjectionTests
{
    private static ConversationStore NewStore(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        return new ConversationStore(settings);
    }

    private static Conversation Conv(string id, string title, bool archived = false, bool pinned = false) => new()
    {
        Id = id,
        Title = title,
        ModelId = "gemma.gguf",
        SystemPrompt = "you are helpful",
        Folder = "Research",
        Tags = ["alpha", "beta"],
        IsPinned = pinned,
        IsArchived = archived,
        ProjectId = "p1",
        RagDatasetId = "ds1",
        RecallExcluded = true,
        Messages =
        [
            new Message { Role = "user", Content = $"question in {id}" },
            new Message { Role = "assistant", Content = $"answer in {id}" }
        ]
    };

    [Fact]
    public async Task The_summary_carries_every_field_the_sidebar_draws()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();
        await store.SaveAsync(Conv("c1", "First conversation"));

        var summary = Assert.Single(await store.GetSummariesAsync());

        Assert.Equal("c1", summary.Id);
        Assert.Equal("First conversation", summary.Title);
        Assert.Equal("gemma.gguf", summary.ModelId);
        Assert.Equal("Research", summary.Folder);
        Assert.Equal(["alpha", "beta"], summary.Tags);
        Assert.False(summary.IsPinned);
        Assert.False(summary.IsArchived);
        Assert.Equal("p1", summary.ProjectId);
        Assert.Equal("ds1", summary.RagDatasetId);
        Assert.True(summary.RecallExcluded);
        Assert.NotEqual(default, summary.UpdatedAt);
    }

    [Fact]
    public async Task The_summary_and_the_full_read_agree_on_ordering_and_membership()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();
        await store.SaveAsync(Conv("c1", "Plain"));
        await store.SaveAsync(Conv("c2", "Pinned", pinned: true));
        await store.SaveAsync(Conv("c3", "Archived", archived: true));

        var full = await store.GetAllAsync(includeArchived: true);
        var summaries = await store.GetSummariesAsync(includeArchived: true);
        Assert.Equal(
            string.Join(",", full.Select(c => c.Id)),
            string.Join(",", summaries.Select(c => c.Id)));

        var visibleFull = await store.GetAllAsync(includeArchived: false);
        var visibleSummaries = await store.GetSummariesAsync(includeArchived: false);
        Assert.Equal(
            string.Join(",", visibleFull.Select(c => c.Id)),
            string.Join(",", visibleSummaries.Select(c => c.Id)));
        Assert.DoesNotContain(visibleSummaries, c => c.Id == "c3");
    }

    /// <summary>
    /// FTS keeps matching message text. The projection changes what is returned,
    /// not what is searched.
    /// </summary>
    [Fact]
    public async Task Search_summaries_still_match_message_text()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();
        await store.SaveAsync(Conv("c1", "Unrelated title"));
        await store.SaveAsync(Conv("c2", "Also unrelated"));

        var hits = await store.SearchSummariesAsync("question in c2");

        Assert.Contains(hits, h => h.Id == "c2");
    }

    [Fact]
    public async Task A_malformed_search_falls_back_rather_than_throwing()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();
        await store.SaveAsync(Conv("c1", "Anything"));

        var hits = await store.SearchSummariesAsync("\"unbalanced AND NEAR( *");

        Assert.NotNull(hits);
    }

    /// <summary>
    /// The parent-chain backfill runs when a conversation is loaded, not when it
    /// is listed. r25 introduced it and the sidebar has no business knowing about
    /// branch structure.
    /// </summary>
    [Fact]
    public async Task Opening_a_conversation_still_backfills_its_parent_chain()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();
        await store.SaveAsync(Conv("c1", "Loaded"));

        var loaded = await store.GetByIdAsync("c1");

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Messages.Count);
        Assert.False(string.IsNullOrEmpty(loaded.Messages[1].ParentId),
            "GetByIdAsync is untouched: opening a conversation genuinely needs its messages and their parent chain");
    }

    [Fact]
    public void No_list_path_type_carries_a_message_collection()
    {
        // The projection exists so the sidebar cannot accidentally start
        // depending on messages again.
        Assert.Null(typeof(ConversationSummary).GetProperty("Messages"));
        Assert.Null(typeof(ConversationItemViewModel).GetProperty("Messages"));
    }
}
