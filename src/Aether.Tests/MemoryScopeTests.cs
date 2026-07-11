using System.Text.Json;
using Aether.Agent.Models;
using Aether.Agent.Services;
using Aether.Core.Models;
using Aether.Services;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

public sealed class MemoryScopeTests
{
    private static MemoryStore NewStore(TempDir temp, out Aether.Core.Services.ISettingsService settings)
    {
        var s = NewSettings(temp);
        s.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings = s;
        return new MemoryStore(s);
    }

    [Fact]
    public async Task Scope_and_title_roundtrip_and_filter()
    {
        using var temp = new TempDir();
        var store = NewStore(temp, out _);
        await store.InitializeAsync();

        var root = Path.GetFullPath(temp.PathFor("ws"));
        await store.SaveAsync(new Memory { Id = "g1", Content = "global fact" });
        await store.SaveAsync(new Memory
        {
            Id = "w1",
            Scope = MemoryScope.Workspace,
            ScopeId = root,
            Title = "Build notes",
            Content = "Use dotnet build -v q",
            Category = "workspace"
        });

        var w1 = await store.GetByIdAsync("w1");
        Assert.NotNull(w1);
        Assert.Equal(MemoryScope.Workspace, w1!.Scope);
        Assert.Equal(root, w1.ScopeId);
        Assert.Equal("Build notes", w1.Title);

        var workspaceRows = await store.GetByScopeAsync(MemoryScope.Workspace, root);
        Assert.Equal(["w1"], workspaceRows.Select(m => m.Id));

        var globals = await store.GetByScopeAsync(MemoryScope.Global);
        Assert.Equal(["g1"], globals.Select(m => m.Id));
    }

    [Fact]
    public async Task Existing_v1_database_gains_scope_columns_on_initialize()
    {
        using var temp = new TempDir();
        var store = NewStore(temp, out _);

        // Hand-build a v1 memories table (no scope/scope_id/title) with an existing row.
        var dbDir = temp.PathFor("data");
        Directory.CreateDirectory(dbDir);
        await using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(dbDir, "memories.db")}"))
        {
            await c.OpenAsync();
            var cmd = c.CreateCommand();
            cmd.CommandText = @"
            CREATE TABLE memories (
                id TEXT PRIMARY KEY,
                category TEXT NOT NULL,
                content TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                source_conversation_id TEXT,
                importance_score REAL DEFAULT 0.5,
                tags_json TEXT DEFAULT '[]',
                is_pinned INTEGER DEFAULT 0,
                is_archived INTEGER DEFAULT 0,
                frequency_count INTEGER DEFAULT 1,
                last_merge_time TEXT,
                expiration_date TEXT,
                relationships_json TEXT DEFAULT '[]',
                is_encrypted INTEGER DEFAULT 0
            );
            INSERT INTO memories (id, category, content, created_at, updated_at)
            VALUES ('old1', 'facts', 'pre-scope row', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');";
            await cmd.ExecuteNonQueryAsync();
        }

        await store.InitializeAsync();
        var old = await store.GetByIdAsync("old1");
        Assert.NotNull(old);
        Assert.Equal(MemoryScope.Global, old!.Scope);
        Assert.Equal("", old.ScopeId);
        Assert.Equal("", old.Title);

        // New scoped rows work against the migrated table.
        await store.SaveAsync(new Memory { Id = "w1", Scope = MemoryScope.Workspace, ScopeId = "x", Title = "t", Content = "c" });
        Assert.Equal(["w1"], (await store.GetByScopeAsync(MemoryScope.Workspace, "x")).Select(m => m.Id));
    }

    [Fact]
    public async Task Workspace_memory_store_upserts_lists_and_deletes_through_shared_store()
    {
        using var temp = new TempDir();
        var memories = NewStore(temp, out var settings);
        var store = new WorkspaceMemoryStore(memories, settings);
        var root = temp.PathFor("ws");

        var entry = await store.UpsertAsync(new AgentWorkspaceMemoryEntry
        {
            WorkspaceRoot = root,
            Title = "  Deploy steps  ",
            Body = " run publish script ",
            Tags = ["deploy"]
        });

        var listed = Assert.Single(await store.ListAsync(root));
        Assert.Equal(entry.Id, listed.Id);
        Assert.Equal("Deploy steps", listed.Title);
        Assert.Equal("run publish script", listed.Body);
        Assert.Equal(Path.GetFullPath(root), listed.WorkspaceRoot);
        Assert.Equal(["deploy"], listed.Tags);

        // Other workspaces see nothing.
        Assert.Empty(await store.ListAsync(temp.PathFor("other")));

        await store.DeleteAsync(root, entry.Id);
        Assert.Empty(await store.ListAsync(root));
    }

    [Fact]
    public async Task Legacy_memory_json_files_are_imported_once_and_renamed()
    {
        using var temp = new TempDir();
        var memories = NewStore(temp, out var settings);
        var store = new WorkspaceMemoryStore(memories, settings);
        var root = Path.GetFullPath(temp.PathFor("ws"));

        // Write a legacy memory.json directly in the pre-migration file
        // format (snake_case, one JSON array per workspace) that the old
        // file-backed store used to produce, without depending on that
        // store's class (removed once WorkspaceMemoryStore replaced it).
        var legacyDir = store.GetWorkspaceDirectory(root);
        Directory.CreateDirectory(legacyDir);
        var legacyPath = Path.Combine(legacyDir, "memory.json");
        var legacyEntry = new AgentWorkspaceMemoryEntry { Id = "legacy1", WorkspaceRoot = root, Title = "Old note", Body = "kept" };
        var legacyOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        await File.WriteAllTextAsync(legacyPath, JsonSerializer.Serialize(new List<AgentWorkspaceMemoryEntry> { legacyEntry }, legacyOptions));
        Assert.True(File.Exists(legacyPath));

        var listed = Assert.Single(await store.ListAsync(root));
        Assert.Equal("legacy1", listed.Id);
        Assert.Equal("Old note", listed.Title);
        Assert.False(File.Exists(legacyPath));
        Assert.True(File.Exists(legacyPath + ".migrated"));
    }
}
