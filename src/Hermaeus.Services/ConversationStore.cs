using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Microsoft.Data.Sqlite;

namespace Hermaeus.Services;

public sealed class ConversationStore : IConversationStore
{
    private const int SchemaVersion = 3;
    private readonly ISettingsService _settings;
    private string _initializedPath = string.Empty;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private string DbPath
    {
        get
        {
            var dir = SettingsService.ResolveDataRoot(_settings.Settings);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "conversations.db");
        }
    }
    private string Cs => $"Data Source={DbPath}";

    public ConversationStore(ISettingsService settings)
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
            var ftsExisted = await TableExistsAsync(c, "conversations_fts", ct);
            var cmd = c.CreateCommand();
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS conversations (
                id            TEXT PRIMARY KEY,
                title         TEXT NOT NULL,
                model_id      TEXT NOT NULL,
                system_prompt TEXT NOT NULL,
                created_at    TEXT NOT NULL,
                updated_at    TEXT NOT NULL,
                messages_json TEXT NOT NULL
            );
            CREATE VIRTUAL TABLE IF NOT EXISTS conversations_fts USING fts5(
                id UNINDEXED,
                title,
                messages,
                folder,
                tags
            );
            CREATE INDEX IF NOT EXISTS idx_updated ON conversations(updated_at DESC);";
            await cmd.ExecuteNonQueryAsync(ct);
            var schemaChanged = await SqliteMigrationRunner.ApplyAsync(c, "conversations", SchemaVersion,
            [
                new SqliteMigration(1, async (db, token) =>
                {
                    var changed = false;
                    changed |= await EnsureColumnAsync(db, "folder", "TEXT NOT NULL DEFAULT ''", token);
                    changed |= await EnsureColumnAsync(db, "tags_json", "TEXT NOT NULL DEFAULT '[]'", token);
                    changed |= await EnsureColumnAsync(db, "is_pinned", "INTEGER NOT NULL DEFAULT 0", token);
                    changed |= await EnsureColumnAsync(db, "is_archived", "INTEGER NOT NULL DEFAULT 0", token);
                    return changed;
                }),
                new SqliteMigration(2, async (db, token) =>
                    await EnsureColumnAsync(db, "rag_dataset_id", "TEXT NOT NULL DEFAULT ''", token)),
                new SqliteMigration(3, async (db, token) =>
                    await EnsureColumnAsync(db, "project_id", "TEXT NOT NULL DEFAULT ''", token))
            ], ct);
            if (!ftsExisted || schemaChanged)
                await RebuildFtsAsync(c, ct);
            _initializedPath = dbPath;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection c, string table, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $table";
        cmd.Parameters.AddWithValue("$table", table);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is not null;
    }

    private static async Task RebuildFtsAsync(SqliteConnection c, CancellationToken ct)
    {
        await using var clear = c.CreateCommand();
        clear.CommandText = "DELETE FROM conversations_fts";
        await clear.ExecuteNonQueryAsync(ct);

        await using var fill = c.CreateCommand();
        fill.CommandText = @"
            INSERT INTO conversations_fts (id, title, messages, folder, tags)
            SELECT id, title, messages_json, folder, tags_json
            FROM conversations";
        await fill.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> EnsureColumnAsync(SqliteConnection c, string column, string definition, CancellationToken ct)
    {
        var exists = false;
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(conversations)";
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
        alter.CommandText = $"ALTER TABLE conversations ADD COLUMN {column} {definition}";
        await alter.ExecuteNonQueryAsync(ct);
        return true;
    }

    public async Task<List<Conversation>> GetAllAsync(bool includeArchived = true, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = includeArchived
            ? "SELECT * FROM conversations ORDER BY is_archived ASC, is_pinned DESC, updated_at DESC"
            : "SELECT * FROM conversations WHERE is_archived = 0 ORDER BY is_archived ASC, is_pinned DESC, updated_at DESC";
        var r = new List<Conversation>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) r.Add(Map(rd));
        return r;
    }

    public async Task<Conversation?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM conversations WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct) ? Map(rd) : null;
    }

    public async Task SaveAsync(Conversation conv, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        conv.UpdatedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(conv.Messages);
        var tagsJson = JsonSerializer.Serialize(NormalizeTags(conv.Tags));
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO conversations (id,title,model_id,system_prompt,created_at,updated_at,messages_json,folder,tags_json,is_pinned,is_archived,rag_dataset_id,project_id)
            VALUES ($id,$title,$mid,$sp,$ca,$ua,$mj,$folder,$tags,$pin,$archived,$ragDatasetId,$projectId)
            ON CONFLICT(id) DO UPDATE SET
                title=excluded.title, model_id=excluded.model_id,
                system_prompt=excluded.system_prompt,
                updated_at=excluded.updated_at, messages_json=excluded.messages_json,
                folder=excluded.folder, tags_json=excluded.tags_json,
                is_pinned=excluded.is_pinned,
                is_archived=excluded.is_archived,
                rag_dataset_id=excluded.rag_dataset_id,
                project_id=excluded.project_id";
        cmd.Parameters.AddWithValue("$id",    conv.Id);
        cmd.Parameters.AddWithValue("$title", conv.Title);
        cmd.Parameters.AddWithValue("$mid",   conv.ModelId);
        cmd.Parameters.AddWithValue("$sp",    conv.SystemPrompt);
        cmd.Parameters.AddWithValue("$ca",    conv.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$ua",    conv.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$mj",    json);
        cmd.Parameters.AddWithValue("$folder", conv.Folder.Trim());
        cmd.Parameters.AddWithValue("$tags", tagsJson);
        cmd.Parameters.AddWithValue("$pin", conv.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$archived", conv.IsArchived ? 1 : 0);
        cmd.Parameters.AddWithValue("$ragDatasetId", conv.RagDatasetId.Trim());
        cmd.Parameters.AddWithValue("$projectId", conv.ProjectId.Trim());
        await cmd.ExecuteNonQueryAsync(ct);

        await UpsertFtsAsync(c, conv, json, tagsJson, ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM conversations WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);

        await using var fts = c.CreateCommand();
        fts.CommandText = "DELETE FROM conversations_fts WHERE id = $id";
        fts.Parameters.AddWithValue("$id", id);
        await fts.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<Conversation>> SearchAsync(string q, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);

        var ftsQuery = BuildFtsQuery(q);
        if (string.IsNullOrWhiteSpace(ftsQuery))
            return await SearchLikeAsync(c, q, ct);

        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            SELECT c.*
            FROM conversations c
            JOIN conversations_fts f ON f.id = c.id
            WHERE conversations_fts MATCH $q
            ORDER BY c.is_archived ASC, c.is_pinned DESC, c.updated_at DESC
            LIMIT 50";
        cmd.Parameters.AddWithValue("$q", ftsQuery);

        try
        {
            var r = new List<Conversation>();
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) r.Add(Map(rd));
            return r;
        }
        catch (SqliteException)
        {
            // Fall back to LIKE for malformed user input or unsupported MATCH syntax.
            return await SearchLikeAsync(c, q, ct);
        }
    }

    private static async Task UpsertFtsAsync(
        SqliteConnection c,
        Conversation conv,
        string messagesJson,
        string tagsJson,
        CancellationToken ct)
    {
        await using var delete = c.CreateCommand();
        delete.CommandText = "DELETE FROM conversations_fts WHERE id = $id";
        delete.Parameters.AddWithValue("$id", conv.Id);
        await delete.ExecuteNonQueryAsync(ct);

        await using var insert = c.CreateCommand();
        insert.CommandText = @"
            INSERT INTO conversations_fts (id, title, messages, folder, tags)
            VALUES ($id, $title, $messages, $folder, $tags)";
        insert.Parameters.AddWithValue("$id", conv.Id);
        insert.Parameters.AddWithValue("$title", conv.Title);
        insert.Parameters.AddWithValue("$messages", messagesJson);
        insert.Parameters.AddWithValue("$folder", conv.Folder.Trim());
        insert.Parameters.AddWithValue("$tags", tagsJson);
        await insert.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<Conversation>> SearchLikeAsync(SqliteConnection c, string q, CancellationToken ct)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM conversations WHERE title LIKE $q OR messages_json LIKE $q OR folder LIKE $q OR tags_json LIKE $q ORDER BY is_archived ASC, is_pinned DESC, updated_at DESC LIMIT 50";
        cmd.Parameters.AddWithValue("$q", $"%{q}%");
        var r = new List<Conversation>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) r.Add(Map(rd));
        return r;
    }

    private static string BuildFtsQuery(string query)
    {
        var terms = query
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length >= 2)
            .Select(t => t.Replace("\"", "\"\"", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        if (terms.Count == 0)
            return string.Empty;

        return string.Join(" AND ", terms.Select(t => $"\"{t}\"*"));
    }

    private static Conversation Map(SqliteDataReader r) => new()
    {
        Id = GetString(r, "id"),
        Title = GetString(r, "title"),
        ModelId = GetString(r, "model_id"),
        SystemPrompt = GetString(r, "system_prompt"),
        CreatedAt = SqliteDateTime.Parse(GetString(r, "created_at")),
        UpdatedAt = SqliteDateTime.Parse(GetString(r, "updated_at")),
        Messages = JsonSerializer.Deserialize<List<Message>>(GetString(r, "messages_json")) ?? [],
        Folder = GetString(r, "folder"),
        Tags = JsonSerializer.Deserialize<List<string>>(GetString(r, "tags_json", "[]")) ?? [],
        IsPinned = GetInt(r, "is_pinned") != 0,
        IsArchived = GetInt(r, "is_archived") != 0,
        RagDatasetId = GetString(r, "rag_dataset_id"),
        ProjectId = GetString(r, "project_id")
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

    private static List<string> NormalizeTags(IEnumerable<string> tags) =>
        tags
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
