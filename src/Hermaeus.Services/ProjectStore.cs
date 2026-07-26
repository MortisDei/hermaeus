using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Microsoft.Data.Sqlite;

namespace Hermaeus.Services;

/// <summary>
/// Backed by {DataRoot}/projects.db, directly under the data root like every
/// other store, so data-root migration and backup pick it up automatically
/// through <see cref="DataRootManifest"/> with no per-file registration step.
/// </summary>
public sealed class ProjectStore : IProjectStore
{
    private const int SchemaVersion = 1;
    private readonly ISettingsService _settings;
    private string _initializedPath = string.Empty;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private string DbPath
    {
        get
        {
            var dir = SettingsService.ResolveDataRoot(_settings.Settings);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "projects.db");
        }
    }
    private string Cs => $"Data Source={DbPath}";

    public ProjectStore(ISettingsService settings)
    {
        _settings = settings;
    }

    public async Task InitializeAsync() => await EnsureInitializedAsync();

    private async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        var dbPath = DbPath;
        if (_initializedPath == dbPath && File.Exists(dbPath)) return;

        await _initGate.WaitAsync(ct);
        try
        {
            if (_initializedPath == dbPath && File.Exists(dbPath)) return;

            await using var c = new SqliteConnection(Cs);
            await c.OpenAsync(ct);
            var cmd = c.CreateCommand();
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS projects (
                id                    TEXT PRIMARY KEY,
                name                  TEXT NOT NULL,
                description           TEXT NOT NULL,
                folder_root           TEXT NOT NULL,
                dataset_id            TEXT NOT NULL,
                default_model_id      TEXT NOT NULL,
                default_system_prompt TEXT NOT NULL,
                color                 TEXT NOT NULL,
                created_at            TEXT NOT NULL,
                updated_at            TEXT NOT NULL,
                last_opened_at        TEXT NOT NULL,
                is_archived           INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_projects_last_opened ON projects(last_opened_at DESC);";
            await cmd.ExecuteNonQueryAsync(ct);

            await SqliteMigrationRunner.ApplyAsync(c, "projects", SchemaVersion, [], ct);
            _initializedPath = dbPath;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private static async Task<bool> EnsureColumnAsync(SqliteConnection c, string column, string definition, CancellationToken ct)
    {
        var exists = false;
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(projects)";
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                if (string.Equals(rd.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists) return false;
        await using var alter = c.CreateCommand();
        alter.CommandText = $"ALTER TABLE projects ADD COLUMN {column} {definition}";
        await alter.ExecuteNonQueryAsync(ct);
        return true;
    }

    public async Task<List<Project>> GetAllAsync(bool includeArchived = true, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = includeArchived
            ? "SELECT * FROM projects ORDER BY is_archived ASC, last_opened_at DESC"
            : "SELECT * FROM projects WHERE is_archived = 0 ORDER BY last_opened_at DESC";
        var r = new List<Project>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) r.Add(Map(rd));
        return r;
    }

    public async Task<Project?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM projects WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct) ? Map(rd) : null;
    }

    public async Task SaveAsync(Project project, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        project.UpdatedAt = DateTime.UtcNow;
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO projects (id,name,description,folder_root,dataset_id,default_model_id,default_system_prompt,color,created_at,updated_at,last_opened_at,is_archived)
            VALUES ($id,$name,$desc,$folder,$dataset,$model,$prompt,$color,$ca,$ua,$loa,$archived)
            ON CONFLICT(id) DO UPDATE SET
                name=excluded.name, description=excluded.description,
                folder_root=excluded.folder_root, dataset_id=excluded.dataset_id,
                default_model_id=excluded.default_model_id, default_system_prompt=excluded.default_system_prompt,
                color=excluded.color, updated_at=excluded.updated_at,
                last_opened_at=excluded.last_opened_at, is_archived=excluded.is_archived";
        cmd.Parameters.AddWithValue("$id", project.Id);
        cmd.Parameters.AddWithValue("$name", project.Name.Trim());
        cmd.Parameters.AddWithValue("$desc", project.Description);
        cmd.Parameters.AddWithValue("$folder", project.FolderRoot);
        cmd.Parameters.AddWithValue("$dataset", project.DatasetId);
        cmd.Parameters.AddWithValue("$model", project.DefaultModelId);
        cmd.Parameters.AddWithValue("$prompt", project.DefaultSystemPrompt);
        cmd.Parameters.AddWithValue("$color", ProjectColors.IsValid(project.Color) ? project.Color : ProjectColors.Default);
        cmd.Parameters.AddWithValue("$ca", project.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$ua", project.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$loa", project.LastOpenedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$archived", project.IsArchived ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM projects WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Project Map(SqliteDataReader r) => new()
    {
        Id = GetString(r, "id"),
        Name = GetString(r, "name"),
        Description = GetString(r, "description"),
        FolderRoot = GetString(r, "folder_root"),
        DatasetId = GetString(r, "dataset_id"),
        DefaultModelId = GetString(r, "default_model_id"),
        DefaultSystemPrompt = GetString(r, "default_system_prompt"),
        Color = GetString(r, "color", ProjectColors.Default),
        CreatedAt = SqliteDateTime.Parse(GetString(r, "created_at")),
        UpdatedAt = SqliteDateTime.Parse(GetString(r, "updated_at")),
        LastOpenedAt = SqliteDateTime.Parse(GetString(r, "last_opened_at")),
        IsArchived = GetInt(r, "is_archived") != 0
    };

    private static string GetString(SqliteDataReader r, string name, string fallback = "")
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? fallback : r.GetString(ordinal);
    }

    private static int GetInt(SqliteDataReader r, string name)
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? 0 : r.GetInt32(ordinal);
    }
}
