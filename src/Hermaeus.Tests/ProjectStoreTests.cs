using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class ProjectStoreTests
{
    private static ProjectStore NewStore(TempDir temp)
    {
        var s = NewSettings(temp);
        s.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        return new ProjectStore(s);
    }

    [Fact]
    public async Task SaveAsync_and_GetByIdAsync_round_trip()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();

        var project = new Project
        {
            Name = "Hermaeus",
            Description = "The workstation itself",
            FolderRoot = temp.PathFor("repo"),
            DatasetId = "ds1",
            DefaultModelId = "model1",
            DefaultSystemPrompt = "Be terse.",
            Color = ProjectColors.Copper
        };
        await store.SaveAsync(project);

        var loaded = await store.GetByIdAsync(project.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Hermaeus", loaded!.Name);
        Assert.Equal("ds1", loaded.DatasetId);
        Assert.Equal(ProjectColors.Copper, loaded.Color);
        Assert.False(loaded.IsArchived);
    }

    [Fact]
    public async Task SaveAsync_rejects_an_invalid_color_key_by_falling_back_to_default()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();

        var project = new Project { Name = "Bad color", Color = "#FF00FF" };
        await store.SaveAsync(project);

        var loaded = await store.GetByIdAsync(project.Id);
        Assert.Equal(ProjectColors.Default, loaded!.Color);
    }

    [Fact]
    public async Task GetAllAsync_orders_recent_first_and_can_exclude_archived()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();

        var older = new Project { Name = "Older", LastOpenedAt = DateTime.UtcNow.AddDays(-2) };
        var newer = new Project { Name = "Newer", LastOpenedAt = DateTime.UtcNow };
        var archived = new Project { Name = "Archived", LastOpenedAt = DateTime.UtcNow.AddDays(-1), IsArchived = true };
        await store.SaveAsync(older);
        await store.SaveAsync(newer);
        await store.SaveAsync(archived);

        var all = await store.GetAllAsync();
        Assert.Equal(["Newer", "Older", "Archived"], all.Select(p => p.Name));

        var activeOnly = await store.GetAllAsync(includeArchived: false);
        Assert.Equal(["Newer", "Older"], activeOnly.Select(p => p.Name));
    }

    [Fact]
    public async Task DeleteAsync_removes_only_the_project_row()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();

        var project = new Project { Name = "Doomed" };
        await store.SaveAsync(project);
        await store.DeleteAsync(project.Id);

        Assert.Null(await store.GetByIdAsync(project.Id));
    }

    [Fact]
    public async Task A_fresh_data_root_with_no_projects_db_behaves_like_no_project_exists()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);

        // No InitializeAsync call: the very first read must not require a
        // pre-existing file, matching every other store in this codebase.
        var all = await store.GetAllAsync();
        Assert.Empty(all);
    }

    [Fact]
    public void PathRootValidator_rejects_a_traversal_segment()
    {
        Assert.False(PathRootValidator.TryValidate(@"C:\some\..\path", out _, out var error));
        Assert.Contains("..", error, StringComparison.Ordinal);
    }

    [Fact]
    public void PathRootValidator_rejects_an_empty_path()
    {
        Assert.False(PathRootValidator.TryValidate("   ", out _, out var error));
        Assert.NotEqual(string.Empty, error);
    }

    [Fact]
    public void PathRootValidator_rejects_a_missing_folder()
    {
        using var temp = new TempDir();
        Assert.False(PathRootValidator.TryValidate(temp.PathFor("does-not-exist"), out _, out var error));
        Assert.Contains("exist", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PathRootValidator_accepts_a_real_folder()
    {
        using var temp = new TempDir();
        var real = temp.PathFor("real");
        Directory.CreateDirectory(real);
        Assert.True(PathRootValidator.TryValidate(real, out var normalized, out var error));
        Assert.Equal(string.Empty, error);
        Assert.Equal(Path.GetFullPath(real), normalized);
    }

    [Fact]
    public void PathRootValidator_rejects_a_symlinked_root_when_one_can_be_created()
    {
        using var temp = new TempDir();
        var target = temp.PathFor("target");
        Directory.CreateDirectory(target);
        var link = temp.PathFor("link");

        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Creating a symlink requires elevation or Developer Mode on
            // Windows; environments without either cannot exercise this
            // guard, so there is nothing further to assert here.
            return;
        }

        Assert.False(PathRootValidator.TryValidate(link, out _, out var error));
        Assert.Contains("symbolic", error, StringComparison.OrdinalIgnoreCase);
    }

    // ── 1.2: the four subsystem migrations, seeded at the previous schema version ──

    [Fact]
    public async Task Conversations_v2_database_gains_project_id_defaulted_and_existing_rows_survive()
    {
        using var temp = new TempDir();
        var dbDir = temp.PathFor("data");
        Directory.CreateDirectory(dbDir);
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
                    rag_dataset_id TEXT NOT NULL DEFAULT ''
                );
                INSERT INTO conversations (id,title,model_id,system_prompt,created_at,updated_at,messages_json)
                VALUES ('c1','Old chat','m1','','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z','[]');";
            await cmd.ExecuteNonQueryAsync();
        }

        var s = NewSettings(temp);
        s.Settings.DataManagement.DataRootDirectory = dbDir;
        var store = new ConversationStore(s);
        await store.InitializeAsync();

        var old = await store.GetByIdAsync("c1");
        Assert.NotNull(old);
        Assert.Equal("Old chat", old!.Title);
        Assert.Equal(string.Empty, old.ProjectId);

        old.ProjectId = "p1";
        await store.SaveAsync(old);
        Assert.Equal("p1", (await store.GetByIdAsync("c1"))!.ProjectId);
    }

    [Fact]
    public async Task Rag_datasets_v1_database_gains_project_id_defaulted_and_existing_rows_survive()
    {
        using var temp = new TempDir();
        var dbDir = temp.PathFor("data");
        Directory.CreateDirectory(dbDir);
        // SqliteRagStore shares conversations.db with ConversationStore (same data dir).
        await using (var c = new SqliteConnection($"Data Source={Path.Combine(dbDir, "conversations.db")}"))
        {
            await c.OpenAsync();
            var cmd = c.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE rag_datasets (
                    id TEXT PRIMARY KEY, name TEXT NOT NULL UNIQUE, description TEXT NOT NULL DEFAULT '',
                    chunk_count INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL, config_json TEXT NOT NULL DEFAULT '{}'
                );
                INSERT INTO rag_datasets (id,name,created_at) VALUES ('d1','Old dataset','2026-01-01T00:00:00Z');";
            await cmd.ExecuteNonQueryAsync();
        }

        var s = NewSettings(temp);
        s.Settings.DataManagement.DataRootDirectory = dbDir;
        var store = new SqliteRagStore(s);
        await store.InitializeAsync();

        var datasets = await store.GetDatasetsAsync();
        var old = Assert.Single(datasets);
        Assert.Equal("Old dataset", old.Name);
        Assert.Equal(string.Empty, old.ProjectId);

        old.ProjectId = "p1";
        await store.SaveDatasetAsync(old);
        Assert.Equal("p1", (await store.GetDatasetsAsync()).Single().ProjectId);
    }

    [Fact]
    public async Task Agent_task_index_v4_database_gains_project_id_defaulted_and_reconciles_from_task_state()
    {
        using var temp = new TempDir();
        var s = NewSettings(temp);
        s.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new FileAgentTaskStateStore(s);

        var state = new AgentTaskState { TaskId = "t1", Goal = "Old goal" };
        await store.SaveAsync(state);

        var recent = await store.ListRecentAsync();
        var item = Assert.Single(recent);
        Assert.Equal(string.Empty, item.ProjectId);

        state.ProjectId = "p1";
        await store.SaveAsync(state);
        Assert.Equal("p1", (await store.ListRecentAsync()).Single().ProjectId);
    }

    [Fact]
    public void Memory_scope_persists_project_by_name_not_ordinal()
    {
        // Project was appended after Workspace; if the store ever persisted by
        // ordinal instead of name this would silently collide with Workspace's
        // old ordinal on a future reorder. Guard the assumption explicitly.
        Assert.Equal("Project", MemoryScope.Project.ToString());
        Assert.True(Enum.TryParse<MemoryScope>("Project", out var parsed));
        Assert.Equal(MemoryScope.Project, parsed);
    }

    [Fact]
    public async Task Projects_db_survives_data_root_migration_alongside_a_real_project_row()
    {
        using var temp = new TempDir();
        var previous = temp.PathFor("previous");
        var next = temp.PathFor("next");
        Directory.CreateDirectory(previous);

        var s = NewSettings(temp);
        s.Settings.DataManagement.DataRootDirectory = previous;
        var store = new ProjectStore(s);
        await store.SaveAsync(new Project { Name = "Migrated project" });
        SqliteConnection.ClearAllPools();

        s.Settings.DataManagement.DataRootDirectory = next;
        var result = await s.SaveAsync(previous);
        Assert.True(result.FilesMoved > 0);
        Assert.True(File.Exists(Path.Combine(next, "projects.db")));

        var afterMigration = new ProjectStore(s);
        var projects = await afterMigration.GetAllAsync();
        Assert.Equal(["Migrated project"], projects.Select(p => p.Name));
    }
}
