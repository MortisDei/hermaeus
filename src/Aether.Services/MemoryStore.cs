using System.Text.Json;
using Aether.Core.Models;
using Aether.Core.Services;
using Microsoft.Data.Sqlite;

namespace Aether.Services;

/// <summary>
/// SQLite-based implementation of memory persistence.
/// </summary>
public sealed class MemoryStore : IMemoryStore
{
    private readonly ISettingsService _settings;
    private string _initializedPath = string.Empty;

    private string DbPath
    {
        get
        {
            var dir = SettingsService.ResolveDataRoot(_settings.Settings);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "memories.db");
        }
    }

    private string Cs => $"Data Source={DbPath}";

    public MemoryStore(ISettingsService settings)
    {
        _settings = settings;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
    }

    private async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        var dbPath = DbPath;
        if (_initializedPath == dbPath && File.Exists(dbPath)) return;

        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS memories (
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
            CREATE VIRTUAL TABLE IF NOT EXISTS memories_fts USING fts5(
                id UNINDEXED,
                category,
                content,
                tags
            );
            CREATE INDEX IF NOT EXISTS idx_category ON memories(category);
            CREATE INDEX IF NOT EXISTS idx_importance ON memories(importance_score DESC);
            CREATE INDEX IF NOT EXISTS idx_updated ON memories(updated_at DESC);
            CREATE INDEX IF NOT EXISTS idx_source_conversation ON memories(source_conversation_id);";
        await cmd.ExecuteNonQueryAsync(ct);
        await RebuildFtsAsync(c, ct);
        _initializedPath = dbPath;
    }

    private static async Task RebuildFtsAsync(SqliteConnection c, CancellationToken ct)
    {
        await using var clear = c.CreateCommand();
        clear.CommandText = "DELETE FROM memories_fts";
        await clear.ExecuteNonQueryAsync(ct);

        await using var fill = c.CreateCommand();
        fill.CommandText = @"
            INSERT INTO memories_fts (id, category, content, tags)
            SELECT id, category, content, tags_json
            FROM memories";
        await fill.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<Memory>> GetAllAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = includeArchived
            ? "SELECT * FROM memories ORDER BY is_pinned DESC, importance_score DESC, updated_at DESC"
            : "SELECT * FROM memories WHERE is_archived = 0 ORDER BY is_pinned DESC, importance_score DESC, updated_at DESC";
        var r = new List<Memory>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) r.Add(Map(rd));
        return r;
    }

    public async Task<Memory?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM memories WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct) ? Map(rd) : null;
    }

    public async Task<List<Memory>> GetByCategoryAsync(string category, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM memories WHERE category = $cat AND is_archived = 0 ORDER BY is_pinned DESC, importance_score DESC, updated_at DESC";
        cmd.Parameters.AddWithValue("$cat", category);
        var r = new List<Memory>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) r.Add(Map(rd));
        return r;
    }

    public async Task SaveAsync(Memory memory, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        memory.UpdatedAt = DateTime.UtcNow;
        var tagsJson = JsonSerializer.Serialize(NormalizeTags(memory.Tags));
        var relationshipsJson = JsonSerializer.Serialize(memory.RelatedMemoryIds);

        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO memories (id,category,content,created_at,updated_at,source_conversation_id,importance_score,tags_json,is_pinned,is_archived,frequency_count,last_merge_time,expiration_date,relationships_json,is_encrypted)
            VALUES ($id,$cat,$content,$ca,$ua,$src,$imp,$tags,$pin,$arch,$freq,$merge,$exp,$rel,$enc)
            ON CONFLICT(id) DO UPDATE SET
                category=excluded.category,
                content=excluded.content,
                updated_at=excluded.updated_at,
                importance_score=excluded.importance_score,
                tags_json=excluded.tags_json,
                is_pinned=excluded.is_pinned,
                is_archived=excluded.is_archived,
                frequency_count=excluded.frequency_count,
                last_merge_time=excluded.last_merge_time,
                expiration_date=excluded.expiration_date,
                relationships_json=excluded.relationships_json,
                is_encrypted=excluded.is_encrypted";

        cmd.Parameters.AddWithValue("$id", memory.Id);
        cmd.Parameters.AddWithValue("$cat", memory.Category);
        cmd.Parameters.AddWithValue("$content", memory.Content);
        cmd.Parameters.AddWithValue("$ca", memory.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$ua", memory.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$src", memory.SourceConversationId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$imp", memory.ImportanceScore);
        cmd.Parameters.AddWithValue("$tags", tagsJson);
        cmd.Parameters.AddWithValue("$pin", memory.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$arch", memory.IsArchived ? 1 : 0);
        cmd.Parameters.AddWithValue("$freq", memory.FrequencyCount);
        cmd.Parameters.AddWithValue("$merge", memory.LastMergeTime?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$exp", memory.ExpirationDate?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$rel", relationshipsJson);
        cmd.Parameters.AddWithValue("$enc", memory.IsEncrypted ? 1 : 0);

        await cmd.ExecuteNonQueryAsync(ct);
        await UpsertFtsAsync(c, memory, tagsJson, ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);

        var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM memories WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);

        await using var fts = c.CreateCommand();
        fts.CommandText = "DELETE FROM memories_fts WHERE id = $id";
        fts.Parameters.AddWithValue("$id", id);
        await fts.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<Memory>> SearchAsync(string q, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);

        var ftsQuery = BuildFtsQuery(q);
        if (string.IsNullOrWhiteSpace(ftsQuery))
            return await SearchLikeAsync(c, q, ct);

        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            SELECT m.*
            FROM memories m
            JOIN memories_fts f ON f.id = m.id
            WHERE memories_fts MATCH $q
            ORDER BY m.is_pinned DESC, m.importance_score DESC, m.updated_at DESC
            LIMIT 100";
        cmd.Parameters.AddWithValue("$q", ftsQuery);

        try
        {
            var r = new List<Memory>();
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) r.Add(Map(rd));
            return r;
        }
        catch (SqliteException)
        {
            return await SearchLikeAsync(c, q, ct);
        }
    }

    public async Task<List<Memory>> GetByImportanceAsync(double minScore, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM memories WHERE importance_score >= $score AND is_archived = 0 ORDER BY importance_score DESC, updated_at DESC";
        cmd.Parameters.AddWithValue("$score", minScore);
        var r = new List<Memory>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) r.Add(Map(rd));
        return r;
    }

    public async Task<List<Memory>> GetRecentAsync(int limit = 10, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM memories WHERE is_archived = 0 ORDER BY updated_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));
        var r = new List<Memory>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) r.Add(Map(rd));
        return r;
    }

    public async Task<int> GetCountByConversationAsync(string conversationId, bool includeArchived = false, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = includeArchived
            ? "SELECT COUNT(1) FROM memories WHERE source_conversation_id = $src"
            : "SELECT COUNT(1) FROM memories WHERE source_conversation_id = $src AND is_archived = 0";
        cmd.Parameters.AddWithValue("$src", conversationId ?? (object)DBNull.Value);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(scalar ?? 0);
    }

    public async Task<Dictionary<string,int>> GetCountsByConversationAsync(IEnumerable<string> conversationIds, bool includeArchived = false, CancellationToken ct = default)
    {
        var ids = conversationIds?.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().ToList() ?? new List<string>();
        var result = new Dictionary<string,int>(StringComparer.Ordinal);
        if (ids.Count == 0) return result;

        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();

        // build parameter list: $p0, $p1, ...
        var paramNames = ids.Select((id, idx) => "$p" + idx).ToList();
        var inClause = string.Join(',', paramNames);
        cmd.CommandText = includeArchived
            ? $"SELECT source_conversation_id, COUNT(1) as cnt FROM memories WHERE source_conversation_id IN ({inClause}) GROUP BY source_conversation_id"
            : $"SELECT source_conversation_id, COUNT(1) as cnt FROM memories WHERE source_conversation_id IN ({inClause}) AND is_archived = 0 GROUP BY source_conversation_id";

        for (var i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue(paramNames[i], ids[i]);

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var id = GetString(rd, "source_conversation_id", "");
            var cnt = GetInt(rd, "cnt", 0);
            if (!string.IsNullOrEmpty(id)) result[id] = cnt;
        }

        // ensure all requested ids have an entry (0 if missing)
        foreach (var id in ids)
            if (!result.ContainsKey(id)) result[id] = 0;

        return result;
    }

    private static async Task UpsertFtsAsync(
        SqliteConnection c,
        Memory memory,
        string tagsJson,
        CancellationToken ct)
    {
        await using var delete = c.CreateCommand();
        delete.CommandText = "DELETE FROM memories_fts WHERE id = $id";
        delete.Parameters.AddWithValue("$id", memory.Id);
        await delete.ExecuteNonQueryAsync(ct);

        await using var insert = c.CreateCommand();
        insert.CommandText = @"
            INSERT INTO memories_fts (id, category, content, tags)
            VALUES ($id, $cat, $content, $tags)";
        insert.Parameters.AddWithValue("$id", memory.Id);
        insert.Parameters.AddWithValue("$cat", memory.Category);
        insert.Parameters.AddWithValue("$content", memory.Content);
        insert.Parameters.AddWithValue("$tags", tagsJson);
        await insert.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<Memory>> SearchLikeAsync(SqliteConnection c, string q, CancellationToken ct)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM memories WHERE content LIKE $q OR category LIKE $q OR tags_json LIKE $q ORDER BY is_pinned DESC, importance_score DESC, updated_at DESC LIMIT 100";
        cmd.Parameters.AddWithValue("$q", $"%{q}%");
        var r = new List<Memory>();
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

    private static Memory Map(SqliteDataReader r) => new()
    {
        Id = GetString(r, "id"),
        Category = GetString(r, "category", "facts"),
        Content = GetString(r, "content"),
        CreatedAt = DateTime.Parse(GetString(r, "created_at")),
        UpdatedAt = DateTime.Parse(GetString(r, "updated_at")),
        SourceConversationId = GetStringNullable(r, "source_conversation_id"),
        ImportanceScore = GetDouble(r, "importance_score", 0.5),
        Tags = JsonSerializer.Deserialize<List<string>>(GetString(r, "tags_json", "[]")) ?? [],
        IsPinned = GetInt(r, "is_pinned") != 0,
        IsArchived = GetInt(r, "is_archived") != 0,
        FrequencyCount = GetInt(r, "frequency_count", 1),
        LastMergeTime = GetDateTimeNullable(r, "last_merge_time"),
        ExpirationDate = GetDateTimeNullable(r, "expiration_date"),
        RelatedMemoryIds = JsonSerializer.Deserialize<List<string>>(GetString(r, "relationships_json", "[]")) ?? [],
        IsEncrypted = GetInt(r, "is_encrypted") != 0
    };

    private static string GetString(SqliteDataReader r, string name, string fallback = "")
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? fallback : r.GetString(ordinal);
    }

    private static string? GetStringNullable(SqliteDataReader r, string name)
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
    }

    private static int GetInt(SqliteDataReader r, string name, int fallback = 0)
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? fallback : r.GetInt32(ordinal);
    }

    private static double GetDouble(SqliteDataReader r, string name, double fallback = 0.0)
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? fallback : r.GetDouble(ordinal);
    }

    private static DateTime? GetDateTimeNullable(SqliteDataReader r, string name)
    {
        var ordinal = r.GetOrdinal(name);
        if (r.IsDBNull(ordinal)) return null;
        var str = r.GetString(ordinal);
        return string.IsNullOrWhiteSpace(str) ? null : DateTime.Parse(str);
    }

    private static List<string> NormalizeTags(IEnumerable<string> tags) =>
        tags
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
