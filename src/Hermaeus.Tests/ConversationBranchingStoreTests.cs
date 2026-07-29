using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r25 doc 01: the branch tree persists inside the existing messages_json blob
/// (Message.ParentId is additive JSON); only the active-leaf pointer needed a
/// column. The backfill that gives pre-r25 conversations a parent chain is the
/// highest-risk change in the round, so it is tested against a seeded 0.31.0
/// row rather than only against data this version wrote.
/// </summary>
public sealed class ConversationBranchingStoreTests
{
    private static ConversationStore NewStore(TempDir temp)
    {
        var s = NewSettings(temp);
        s.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        return new ConversationStore(s);
    }

    [Fact]
    public async Task Parent_ids_and_active_leaf_survive_a_round_trip()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();

        var conversation = new Conversation
        {
            Id = "c1",
            Title = "Branched",
            ActiveLeafId = "m3",
            Messages =
            [
                new Message { Id = "m1", ParentId = string.Empty, Role = "user", Content = "hi" },
                new Message { Id = "m2", ParentId = "m1", Role = "assistant", Content = "first answer" },
                new Message { Id = "m3", ParentId = "m1", Role = "assistant", Content = "second answer" }
            ]
        };
        await store.SaveAsync(conversation);

        var reloaded = await store.GetByIdAsync("c1");
        Assert.NotNull(reloaded);
        Assert.Equal("m3", reloaded!.ActiveLeafId);
        Assert.Equal(3, reloaded.Messages.Count);
        Assert.Equal("m1", reloaded.Messages.Single(m => m.Id == "m2").ParentId);
        Assert.Equal("m1", reloaded.Messages.Single(m => m.Id == "m3").ParentId);

        // Both branches persisted; the abandoned one is still the user's words.
        var path = ConversationTree.ActivePath(reloaded.Messages, reloaded.ActiveLeafId);
        Assert.Equal(["m1", "m3"], path.Select(m => m.Id));
    }

    /// <summary>
    /// A conversation written by 0.31.0 has a v4 schema with no active_leaf_id
    /// and a messages_json blob with no ParentId on any message. It must gain
    /// the column, get its chain inferred, and render identically.
    /// </summary>
    [Fact]
    public async Task Conversations_v4_database_gains_active_leaf_id_and_backfills_a_linear_chain()
    {
        using var temp = new TempDir();
        var dbDir = temp.PathFor("data");
        Directory.CreateDirectory(dbDir);

        // Exactly the shape 0.31.0 wrote: no parent_id inside the blob, no active_leaf_id column.
        const string messagesJson = """
            [{"Id":"m1","ConversationId":"c1","Role":"user","Content":"hello","OriginalContent":"hello","CreatedAt":"2026-01-01T00:00:00Z","IsError":false,"ModelId":"","DurationMs":0,"AttachedFilePaths":[],"WasTruncated":false},
             {"Id":"m2","ConversationId":"c1","Role":"assistant","Content":"hi there","OriginalContent":"","CreatedAt":"2026-01-01T00:00:01Z","IsError":false,"ModelId":"m","DurationMs":5,"AttachedFilePaths":[],"WasTruncated":false},
             {"Id":"m3","ConversationId":"c1","Role":"user","Content":"and again","OriginalContent":"and again","CreatedAt":"2026-01-01T00:00:02Z","IsError":false,"ModelId":"","DurationMs":0,"AttachedFilePaths":[],"WasTruncated":false}]
            """;

        await using (var c = new SqliteConnection($"Data Source={Path.Combine(dbDir, "conversations.db")}"))
        {
            await c.OpenAsync();
            var cmd = c.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE conversations (
                    id TEXT PRIMARY KEY, title TEXT NOT NULL, model_id TEXT NOT NULL,
                    system_prompt TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
                    messages_json TEXT NOT NULL, folder TEXT NOT NULL DEFAULT '', tags_json TEXT NOT NULL DEFAULT '[]',
                    is_pinned INTEGER NOT NULL DEFAULT 0, is_archived INTEGER NOT NULL DEFAULT 0,
                    rag_dataset_id TEXT NOT NULL DEFAULT '', project_id TEXT NOT NULL DEFAULT '',
                    recall_excluded INTEGER NOT NULL DEFAULT 0
                );
                INSERT INTO conversations (id,title,model_id,system_prompt,created_at,updated_at,messages_json)
                VALUES ('c1','Old chat','m1','','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z',$mj);";
            cmd.Parameters.AddWithValue("$mj", messagesJson);
            await cmd.ExecuteNonQueryAsync();
        }

        var s = NewSettings(temp);
        s.Settings.DataManagement.DataRootDirectory = dbDir;
        var store = new ConversationStore(s);
        await store.InitializeAsync();

        var old = await store.GetByIdAsync("c1");
        Assert.NotNull(old);
        Assert.Equal("Old chat", old!.Title);
        Assert.Equal(string.Empty, old.ActiveLeafId);
        Assert.Equal(3, old.Messages.Count);

        // The chain its stored order already implied.
        Assert.Equal(string.Empty, old.Messages[0].ParentId);
        Assert.Equal("m1", old.Messages[1].ParentId);
        Assert.Equal("m2", old.Messages[2].ParentId);

        // And it renders as the same sequence 0.31.0 rendered.
        var path = ConversationTree.ActivePath(old.Messages, old.ActiveLeafId);
        Assert.Equal(["m1", "m2", "m3"], path.Select(m => m.Id));

        old.ActiveLeafId = "m2";
        await store.SaveAsync(old);
        Assert.Equal("m2", (await store.GetByIdAsync("c1"))!.ActiveLeafId);
    }

    /// <summary>
    /// Conversation search must find a message on a branch the user navigated
    /// away from. A message you wrote and cannot find again is a bug.
    /// </summary>
    [Fact]
    public async Task Search_finds_a_message_on_an_inactive_branch()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();

        await store.SaveAsync(new Conversation
        {
            Id = "c1",
            Title = "Branched",
            ActiveLeafId = "m3",
            Messages =
            [
                new Message { Id = "m1", ParentId = string.Empty, Role = "user", Content = "question" },
                new Message { Id = "m2", ParentId = "m1", Role = "assistant", Content = "pomegranate" },
                new Message { Id = "m3", ParentId = "m1", Role = "assistant", Content = "different answer" }
            ]
        });

        var hits = await store.SearchAsync("pomegranate");

        Assert.Contains(hits, c => c.Id == "c1");
    }
}
