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
        _initializedPath = dbPath;
    }

    public async Task<List<Conversation>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM conversations ORDER BY updated_at DESC";
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
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO conversations (id,title,model_id,system_prompt,created_at,updated_at,messages_json)
            VALUES ($id,$title,$mid,$sp,$ca,$ua,$mj)
            ON CONFLICT(id) DO UPDATE SET
                title=excluded.title, model_id=excluded.model_id,
                system_prompt=excluded.system_prompt,
                updated_at=excluded.updated_at, messages_json=excluded.messages_json";
        cmd.Parameters.AddWithValue("$id",    conv.Id);
        cmd.Parameters.AddWithValue("$title", conv.Title);
        cmd.Parameters.AddWithValue("$mid",   conv.ModelId);
        cmd.Parameters.AddWithValue("$sp",    conv.SystemPrompt);
        cmd.Parameters.AddWithValue("$ca",    conv.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$ua",    conv.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$mj",    json);
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
        cmd.CommandText = "SELECT * FROM conversations WHERE title LIKE $q OR messages_json LIKE $q ORDER BY updated_at DESC LIMIT 50";
        cmd.Parameters.AddWithValue("$q", $"%{q}%");
        var r = new List<Conversation>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) r.Add(Map(rd));
        return r;
    }

    private static Conversation Map(SqliteDataReader r) => new()
    {
        Id = r.GetString(0), Title = r.GetString(1), ModelId = r.GetString(2),
        SystemPrompt = r.GetString(3),
        CreatedAt = DateTime.Parse(r.GetString(4)),
        UpdatedAt = DateTime.Parse(r.GetString(5)),
        Messages = JsonSerializer.Deserialize<List<Message>>(r.GetString(6)) ?? []
    };
}
