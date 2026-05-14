using System.Text.Json;
using Aether.Core.Models;
using Aether.Core.Services;
using Microsoft.Data.Sqlite;

namespace Aether.Services;

public sealed class ConversationStore : IConversationStore
{
    private readonly ISettingsService _settings;
    private string _initializedPath = string.Empty;
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

        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
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
            CREATE INDEX IF NOT EXISTS idx_updated ON conversations(updated_at DESC);";
        await cmd.ExecuteNonQueryAsync(ct);
        await EnsureColumnAsync(c, "folder", "TEXT NOT NULL DEFAULT ''", ct);
        await EnsureColumnAsync(c, "tags_json", "TEXT NOT NULL DEFAULT '[]'", ct);
        await EnsureColumnAsync(c, "is_pinned", "INTEGER NOT NULL DEFAULT 0", ct);
        await EnsureColumnAsync(c, "is_archived", "INTEGER NOT NULL DEFAULT 0", ct);
        _initializedPath = dbPath;
    }

    private static async Task EnsureColumnAsync(SqliteConnection c, string column, string definition, CancellationToken ct)
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

        if (exists) return;
        await using var alter = c.CreateCommand();
        alter.CommandText = $"ALTER TABLE conversations ADD COLUMN {column} {definition}";
        await alter.ExecuteNonQueryAsync(ct);
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
            INSERT INTO conversations (id,title,model_id,system_prompt,created_at,updated_at,messages_json,folder,tags_json,is_pinned,is_archived)
            VALUES ($id,$title,$mid,$sp,$ca,$ua,$mj,$folder,$tags,$pin,$archived)
            ON CONFLICT(id) DO UPDATE SET
                title=excluded.title, model_id=excluded.model_id,
                system_prompt=excluded.system_prompt,
                updated_at=excluded.updated_at, messages_json=excluded.messages_json,
                folder=excluded.folder, tags_json=excluded.tags_json,
                is_pinned=excluded.is_pinned,
                is_archived=excluded.is_archived";
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
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM conversations WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<Conversation>> SearchAsync(string q, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM conversations WHERE title LIKE $q OR messages_json LIKE $q OR folder LIKE $q OR tags_json LIKE $q ORDER BY is_archived ASC, is_pinned DESC, updated_at DESC LIMIT 50";
        cmd.Parameters.AddWithValue("$q", $"%{q}%");
        var r = new List<Conversation>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) r.Add(Map(rd));
        return r;
    }

    private static Conversation Map(SqliteDataReader r) => new()
    {
        Id = GetString(r, "id"),
        Title = GetString(r, "title"),
        ModelId = GetString(r, "model_id"),
        SystemPrompt = GetString(r, "system_prompt"),
        CreatedAt = DateTime.Parse(GetString(r, "created_at")),
        UpdatedAt = DateTime.Parse(GetString(r, "updated_at")),
        Messages = JsonSerializer.Deserialize<List<Message>>(GetString(r, "messages_json")) ?? [],
        Folder = GetString(r, "folder"),
        Tags = JsonSerializer.Deserialize<List<string>>(GetString(r, "tags_json", "[]")) ?? [],
        IsPinned = GetInt(r, "is_pinned") != 0,
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

    private static List<string> NormalizeTags(IEnumerable<string> tags) =>
        tags
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
