using Hermaeus.Core.Services;
using Hermaeus.Core.Models;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Services;
using Microsoft.Data.Sqlite;

namespace Hermaeus.Services.Recall;

public sealed class RecallEntry
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty; // "message" | "task"
    public string SourceId { get; set; } = string.Empty;
    public string SubId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Query-time only; not persisted.</summary>
    public double RelevanceScore { get; set; }
}

/// <summary>
/// r24 doc 02 2.1: {DataRoot}/recall.db, holding one row per indexed message
/// or agent task. Directly under the data root like every other store, so
/// data-root migration and backup pick it up automatically. A copy of the
/// user's own words, so every write path here must be reachable only when
/// <see cref="MemorySettings.RecallIndexingEnabled"/> is on (enforced by
/// <see cref="RecallIndexingService"/>, not this store).
/// </summary>
public sealed class RecallIndexStore
{
    private const int SchemaVersion = 1;
    private const int MaxBackfillAttemptsPerRow = 5;
    private static readonly TimeSpan QueryEmbedTimeout = TimeSpan.FromSeconds(3);

    private readonly ISettingsService _settings;
    private readonly IEmbeddingService? _embeddings;
    private readonly IResourceCoordinator? _resourceCoordinator;
    private string _initializedPath = string.Empty;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly SemaphoreSlim _backfillGate = new(1, 1);
    private readonly Dictionary<string, (DateTime NextAttemptUtc, int Attempts)> _backfillState = new(StringComparer.Ordinal);

    private string DbPath
    {
        get
        {
            var dir = SettingsService.ResolveDataRoot(_settings.Settings);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "recall.db");
        }
    }
    private string Cs => $"Data Source={DbPath}";

    public RecallIndexStore(
        ISettingsService settings,
        IEmbeddingService? embeddings = null,
        IResourceCoordinator? resourceCoordinator = null)
    {
        _settings = settings;
        _embeddings = embeddings;
        _resourceCoordinator = resourceCoordinator;
    }

    public async Task InitializeAsync(CancellationToken ct = default) => await EnsureInitializedAsync(ct);

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
            var ftsExisted = await TableExistsAsync(c, "recall_fts", ct);
            var cmd = c.CreateCommand();
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS recall_entries (
                id            TEXT PRIMARY KEY,
                kind          TEXT NOT NULL,
                source_id     TEXT NOT NULL,
                sub_id        TEXT NOT NULL,
                project_id    TEXT NOT NULL DEFAULT '',
                title         TEXT NOT NULL,
                body          TEXT NOT NULL,
                is_archived   INTEGER NOT NULL DEFAULT 0,
                created_at    TEXT NOT NULL,
                indexed_at    TEXT NOT NULL,
                embedding     BLOB,
                embedding_dim INTEGER
            );
            CREATE VIRTUAL TABLE IF NOT EXISTS recall_fts USING fts5(
                id UNINDEXED,
                title,
                body
            );
            CREATE INDEX IF NOT EXISTS idx_recall_kind_source ON recall_entries(kind, source_id);
            CREATE INDEX IF NOT EXISTS idx_recall_project ON recall_entries(project_id);";
            await cmd.ExecuteNonQueryAsync(ct);

            var schemaChanged = await SqliteMigrationRunner.ApplyAsync(c, "recall", SchemaVersion, [], ct);
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
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    private static async Task RebuildFtsAsync(SqliteConnection c, CancellationToken ct)
    {
        await using var clear = c.CreateCommand();
        clear.CommandText = "DELETE FROM recall_fts";
        await clear.ExecuteNonQueryAsync(ct);

        await using var fill = c.CreateCommand();
        fill.CommandText = "INSERT INTO recall_fts (id, title, body) SELECT id, title, body FROM recall_entries";
        await fill.ExecuteNonQueryAsync(ct);
    }

    public static string MakeId(string kind, string sourceId, string subId) =>
        string.IsNullOrEmpty(subId) ? $"{kind}:{sourceId}" : $"{kind}:{sourceId}:{subId}";

    /// <summary>Upsert is keyed by the entry's deterministic id, so re-indexing the same
    /// source is an update, never a duplicate row.</summary>
    public async Task UpsertBatchAsync(IEnumerable<RecallEntry> entries, CancellationToken ct = default)
    {
        var list = entries.ToList();
        if (list.Count == 0) return;

        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);

        foreach (var entry in list)
        {
            entry.IndexedAt = DateTime.UtcNow;

            var del = c.CreateCommand();
            del.Transaction = (SqliteTransaction)tx;
            del.CommandText = "DELETE FROM recall_fts WHERE id = $id";
            del.Parameters.AddWithValue("$id", entry.Id);
            await del.ExecuteNonQueryAsync(ct);

            var cmd = c.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = @"
                INSERT INTO recall_entries (id,kind,source_id,sub_id,project_id,title,body,is_archived,created_at,indexed_at)
                VALUES ($id,$kind,$sid,$sub,$pid,$title,$body,$archived,$ca,$ia)
                ON CONFLICT(id) DO UPDATE SET
                    kind=excluded.kind, source_id=excluded.source_id, sub_id=excluded.sub_id,
                    project_id=excluded.project_id, title=excluded.title, body=excluded.body,
                    is_archived=excluded.is_archived,
                    created_at=excluded.created_at, indexed_at=excluded.indexed_at,
                    embedding=NULL, embedding_dim=NULL";
            cmd.Parameters.AddWithValue("$id", entry.Id);
            cmd.Parameters.AddWithValue("$kind", entry.Kind);
            cmd.Parameters.AddWithValue("$sid", entry.SourceId);
            cmd.Parameters.AddWithValue("$sub", entry.SubId);
            cmd.Parameters.AddWithValue("$pid", entry.ProjectId);
            cmd.Parameters.AddWithValue("$title", entry.Title);
            cmd.Parameters.AddWithValue("$body", entry.Body);
            cmd.Parameters.AddWithValue("$archived", entry.IsArchived ? 1 : 0);
            cmd.Parameters.AddWithValue("$ca", entry.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$ia", entry.IndexedAt.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);

            var ins = c.CreateCommand();
            ins.Transaction = (SqliteTransaction)tx;
            ins.CommandText = "INSERT INTO recall_fts (id, title, body) VALUES ($id, $title, $body)";
            ins.Parameters.AddWithValue("$id", entry.Id);
            ins.Parameters.AddWithValue("$title", entry.Title);
            ins.Parameters.AddWithValue("$body", entry.Body);
            await ins.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    /// <summary>Deletes every entry for one source (a conversation or a task), used by
    /// delete-cascade, per-conversation exclusion, and task re-indexing.</summary>
    public async Task DeleteBySourceAsync(string kind, string sourceId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);

        var ids = new List<string>();
        await using (var select = c.CreateCommand())
        {
            select.CommandText = "SELECT id FROM recall_entries WHERE kind = $kind AND source_id = $sid";
            select.Parameters.AddWithValue("$kind", kind);
            select.Parameters.AddWithValue("$sid", sourceId);
            await using var rd = await select.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) ids.Add(rd.GetString(0));
        }

        if (ids.Count == 0) return;

        await using var tx = await c.BeginTransactionAsync(ct);
        await using (var del = c.CreateCommand())
        {
            del.Transaction = (SqliteTransaction)tx;
            del.CommandText = "DELETE FROM recall_entries WHERE kind = $kind AND source_id = $sid";
            del.Parameters.AddWithValue("$kind", kind);
            del.Parameters.AddWithValue("$sid", sourceId);
            await del.ExecuteNonQueryAsync(ct);
        }
        await using (var delFts = c.CreateCommand())
        {
            delFts.Transaction = (SqliteTransaction)tx;
            delFts.CommandText = "DELETE FROM recall_fts WHERE id = $id";
            var p = delFts.Parameters.Add("$id", SqliteType.Text);
            foreach (var id in ids)
            {
                p.Value = id;
                await delFts.ExecuteNonQueryAsync(ct);
            }
        }
        await tx.CommitAsync(ct);
    }

    /// <summary>2.0's destructive control: deletes every row and vacuums. Genuinely gone,
    /// not soft-deleted.</summary>
    public async Task<int> ClearAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);

        int count;
        await using (var countCmd = c.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM recall_entries";
            count = System.Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
        }

        await using (var delEntries = c.CreateCommand())
        {
            delEntries.CommandText = "DELETE FROM recall_entries";
            await delEntries.ExecuteNonQueryAsync(ct);
        }
        await using (var delFts = c.CreateCommand())
        {
            delFts.CommandText = "DELETE FROM recall_fts";
            await delFts.ExecuteNonQueryAsync(ct);
        }
        await using (var vacuum = c.CreateCommand())
        {
            vacuum.CommandText = "VACUUM";
            await vacuum.ExecuteNonQueryAsync(ct);
        }

        _backfillState.Clear();
        return count;
    }

    public async Task<(int Count, long Bytes)> GetSizeAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM recall_entries";
        var count = System.Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        var bytes = File.Exists(DbPath) ? new FileInfo(DbPath).Length : 0;
        return (count, bytes);
    }

    /// <summary>Small lookup used to show "sub task of: &lt;parent goal&gt;" (doc 02 2.3)
    /// without the caller needing its own query.</summary>
    public async Task<string?> GetTitleAsync(string kind, string sourceId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT title FROM recall_entries WHERE kind = $kind AND source_id = $sid LIMIT 1";
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$sid", sourceId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    /// <summary>True once at least one row exists for this source - a cheap way for the
    /// startup backfill to know whether a conversation/task has ever been indexed.</summary>
    public async Task<HashSet<string>> GetIndexedSourceIdsAsync(string kind, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT source_id FROM recall_entries WHERE kind = $kind";
        cmd.Parameters.AddWithValue("$kind", kind);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) ids.Add(rd.GetString(0));
        return ids;
    }

    /// <summary>
    /// Hybrid FTS-plus-cosine search over one kind ('message' or 'task'),
    /// the same shape as <c>MemoryStore.SearchAsync</c>/<c>HybridRerankAsync</c>.
    /// Falls back to FTS-only, honestly, with no embedding service configured
    /// or reachable within the query-embed timeout.
    /// </summary>
    public async Task<(List<RecallEntry> Results, bool KeywordOnly)> SearchAsync(
        string kind, string query, string projectScope, CancellationToken ct = default, bool includeArchived = false)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);

        var ftsQuery = BuildFtsQuery(query);
        List<RecallEntry> ftsResults;
        if (string.IsNullOrWhiteSpace(ftsQuery))
        {
            ftsResults = [];
        }
        else
        {
            var archivedClause = includeArchived ? "" : " AND e.is_archived = 0";
            var cmd = c.CreateCommand();
            cmd.CommandText = (string.IsNullOrEmpty(projectScope) ? @"
                SELECT e.* FROM recall_entries e
                JOIN recall_fts f ON f.id = e.id
                WHERE recall_fts MATCH $q AND e.kind = $kind" : @"
                SELECT e.* FROM recall_entries e
                JOIN recall_fts f ON f.id = e.id
                WHERE recall_fts MATCH $q AND e.kind = $kind AND e.project_id = $pid")
                + archivedClause + " ORDER BY f.rank LIMIT 100";
            cmd.Parameters.AddWithValue("$q", ftsQuery);
            cmd.Parameters.AddWithValue("$kind", kind);
            if (!string.IsNullOrEmpty(projectScope)) cmd.Parameters.AddWithValue("$pid", projectScope);

            try
            {
                var r = new List<RecallEntry>();
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct)) r.Add(Map(rd));
                ftsResults = r;
            }
            catch (SqliteException)
            {
                ftsResults = [];
            }
        }

        if (_embeddings is null)
        {
            for (var i = 0; i < ftsResults.Count; i++) ftsResults[i].RelevanceScore = 1.0 / (i + 1);
            return (ftsResults, true);
        }

        float[] queryVector;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(QueryEmbedTimeout);
            queryVector = await _embeddings.EmbedAsync(query, timeoutCts.Token);
        }
        catch (Exception)
        {
            for (var i = 0; i < ftsResults.Count; i++) ftsResults[i].RelevanceScore = 1.0 / (i + 1);
            return (ftsResults, true);
        }

        var ftsRank = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 0; i < ftsResults.Count; i++) ftsRank[ftsResults[i].Id] = 1.0 / (i + 1);
        var candidates = new Dictionary<string, RecallEntry>(StringComparer.Ordinal);
        foreach (var e in ftsResults) candidates[e.Id] = e;

        var embCmd = c.CreateCommand();
        embCmd.CommandText = string.IsNullOrEmpty(projectScope)
            ? "SELECT id, embedding, embedding_dim FROM recall_entries WHERE kind = $kind AND embedding IS NOT NULL"
            : "SELECT id, embedding, embedding_dim FROM recall_entries WHERE kind = $kind AND embedding IS NOT NULL AND project_id = $pid";
        embCmd.Parameters.AddWithValue("$kind", kind);
        if (!string.IsNullOrEmpty(projectScope)) embCmd.Parameters.AddWithValue("$pid", projectScope);

        await using (var rd = await embCmd.ExecuteReaderAsync(ct))
        {
            while (await rd.ReadAsync(ct))
            {
                var id = rd.GetString(0);
                var dim = rd.IsDBNull(2) ? 0 : rd.GetInt32(2);
                // Dimension drift (embedding model switched): skip for the semantic
                // half rather than score as garbage, exactly like MemoryStore.
                if (dim != queryVector.Length) continue;

                var vector = FromBlob((byte[])rd[1]);
                var cosine = Math.Max(0.0, CosineSimilarity(queryVector, vector));
                if (candidates.TryGetValue(id, out var e))
                {
                    var ftsScore = ftsRank.GetValueOrDefault(id, 0.0);
                    e.RelevanceScore = (0.5 * ftsScore) + (0.5 * cosine);
                }
            }
        }

        var results = candidates.Values.OrderByDescending(e => e.RelevanceScore).Take(100).ToList();
        return (results, false);
    }

    /// <summary>Same shape as <c>MemoryStore.RunEmbeddingBackfillAsync</c>: bounded batch,
    /// off the send path, gives up on rows that fail repeatedly.</summary>
    public async Task RunEmbeddingBackfillAsync(CancellationToken ct = default)
    {
        if (_embeddings is null) return;
        if (!await _backfillGate.WaitAsync(0, ct)) return;

        IResourceAdmissionLease? lease = null;
        try
        {
            lease = await AcquireBackfillLeaseAsync(ct);
            await EnsureInitializedAsync(ct);
            await using var c = new SqliteConnection(Cs);
            await c.OpenAsync(ct);

            var pending = new List<(string Id, string Text)>();
            var select = c.CreateCommand();
            select.CommandText = "SELECT id, title || ' ' || body FROM recall_entries WHERE embedding IS NULL LIMIT 200";
            await using (var rd = await select.ExecuteReaderAsync(ct))
            {
                while (await rd.ReadAsync(ct))
                    pending.Add((rd.GetString(0), rd.GetString(1)));
            }

            var now = DateTime.UtcNow;
            foreach (var (id, text) in pending)
            {
                if (_backfillState.TryGetValue(id, out var state))
                {
                    if (state.Attempts >= MaxBackfillAttemptsPerRow) continue;
                    if (now < state.NextAttemptUtc) continue;
                }

                try
                {
                    var vector = await _embeddings.EmbedAsync(text, ct);
                    var update = c.CreateCommand();
                    update.CommandText = "UPDATE recall_entries SET embedding = $emb, embedding_dim = $dim WHERE id = $id";
                    update.Parameters.AddWithValue("$emb", ToBlob(vector));
                    update.Parameters.AddWithValue("$dim", vector.Length);
                    update.Parameters.AddWithValue("$id", id);
                    await update.ExecuteNonQueryAsync(ct);
                    _backfillState.Remove(id);
                }
                catch
                {
                    var attempts = (_backfillState.TryGetValue(id, out var previous) ? previous.Attempts : 0) + 1;
                    _backfillState[id] = (now.AddMinutes(10), attempts);
                }
            }
        }
        finally
        {
            if (lease is not null && !lease.IsReleased)
                await lease.ReleaseAsync("recall embedding backfill completed");
            _backfillGate.Release();
        }
    }

    private async Task<IResourceAdmissionLease?> AcquireBackfillLeaseAsync(CancellationToken ct)
    {
        if (_resourceCoordinator is null)
            return null;
        const string consumerId = "rag.recall-backfill";
        _resourceCoordinator.RegisterConsumer(
            ResourceAllocationFactory.EmbeddingBackfillConsumer(consumerId, nameof(RecallIndexStore)));
        return await _resourceCoordinator.AcquireAsync(
            new ResourceAdmissionRequest(
                consumerId,
                ResourceAllocationFactory.EmbeddingBackfillProposal(consumerId),
                callerId: "rag.recall-backfill.start",
                allowUnknown: true), ct);
    }

    private static RecallEntry Map(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        Kind = r.GetString(r.GetOrdinal("kind")),
        SourceId = r.GetString(r.GetOrdinal("source_id")),
        SubId = r.GetString(r.GetOrdinal("sub_id")),
        ProjectId = r.GetString(r.GetOrdinal("project_id")),
        Title = r.GetString(r.GetOrdinal("title")),
        Body = r.GetString(r.GetOrdinal("body")),
        IsArchived = r.GetInt32(r.GetOrdinal("is_archived")) != 0,
        CreatedAt = SqliteDateTime.Parse(r.GetString(r.GetOrdinal("created_at"))),
        IndexedAt = SqliteDateTime.Parse(r.GetString(r.GetOrdinal("indexed_at")))
    };

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

        return terms.Count == 0 ? string.Empty : string.Join(" AND ", terms.Select(t => $"\"{t}\"*"));
    }

    private static byte[] ToBlob(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] FromBlob(byte[] bytes)
    {
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0.0;
        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        if (magA == 0 || magB == 0) return 0.0;
        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }
}
