using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Embeddings;
using Microsoft.Data.Sqlite;

namespace Hermaeus.Services;

/// <summary>
/// SQLite-based implementation of memory persistence.
/// </summary>
public sealed class MemoryStore : IMemoryStore
{
    private const int SchemaVersion = 5;
    private const int MaxBackfillAttemptsPerRow = 5;
    private static readonly TimeSpan QueryEmbedTimeout = TimeSpan.FromSeconds(3);

    private readonly ISettingsService _settings;
    private readonly IEmbeddingService? _embeddings;
    private readonly IRuntimeLogService? _runtimeLogs;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _backfillCooldown;
    private string _initializedPath = string.Empty;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly SemaphoreSlim _backfillGate = new(1, 1);
    private readonly Dictionary<string, (DateTime NextAttemptUtc, int Attempts)> _backfillState = new(StringComparer.Ordinal);
    private bool _queryEmbedFallbackLogged;

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

    public MemoryStore(
        ISettingsService settings,
        IEmbeddingService? embeddings = null,
        IRuntimeLogService? runtimeLogs = null,
        TimeProvider? timeProvider = null,
        TimeSpan? backfillCooldown = null)
    {
        _settings = settings;
        _embeddings = embeddings;
        _runtimeLogs = runtimeLogs;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _backfillCooldown = backfillCooldown ?? TimeSpan.FromMinutes(10);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
    }

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
            var ftsExisted = await TableExistsAsync(c, "memories_fts", ct);
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
                is_encrypted INTEGER DEFAULT 0,
                scope TEXT NOT NULL DEFAULT 'Global',
                scope_id TEXT NOT NULL DEFAULT '',
                title TEXT NOT NULL DEFAULT ''
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
            await SqliteMigrationRunner.ApplyAsync(c, "memories", SchemaVersion,
            [
                new SqliteMigration(1, (_, _) => Task.FromResult(false)),
                new SqliteMigration(2, async (db, token) =>
                {
                    var changed = false;
                    changed |= await EnsureColumnAsync(db, "scope", "TEXT NOT NULL DEFAULT 'Global'", token);
                    changed |= await EnsureColumnAsync(db, "scope_id", "TEXT NOT NULL DEFAULT ''", token);
                    changed |= await EnsureColumnAsync(db, "title", "TEXT NOT NULL DEFAULT ''", token);
                    return changed;
                }),
                new SqliteMigration(3, (db, token) => EnsureColumnAsync(db, "source_json", "TEXT", token)),
                new SqliteMigration(4, async (db, token) =>
                {
                    var changed = false;
                    changed |= await EnsureColumnAsync(db, "embedding", "BLOB", token);
                    changed |= await EnsureColumnAsync(db, "recall_count", "INTEGER DEFAULT 0", token);
                    changed |= await EnsureColumnAsync(db, "last_recalled_at", "TEXT", token);
                    return changed;
                }),
                new SqliteMigration(5, (db, token) => EnsureColumnAsync(db, "embedding_dim", "INTEGER", token))
            ], ct);
            if (!ftsExisted)
                await RebuildFtsAsync(c, ct);
            _initializedPath = dbPath;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private static async Task<bool> EnsureColumnAsync(SqliteConnection c, string column, string definition, CancellationToken ct)
    {
        await using var check = c.CreateCommand();
        check.CommandText = "SELECT COUNT(1) FROM pragma_table_info('memories') WHERE name = $name";
        check.Parameters.AddWithValue("$name", column);
        if (Convert.ToInt32(await check.ExecuteScalarAsync(ct)) > 0)
            return false;

        await using var alter = c.CreateCommand();
        alter.CommandText = $"ALTER TABLE memories ADD COLUMN {column} {definition}";
        await alter.ExecuteNonQueryAsync(ct);
        return true;
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

    public async Task<List<Memory>> GetByScopeAsync(MemoryScope scope, string? scopeId = null, bool includeArchived = false, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        var archived = includeArchived ? "" : " AND is_archived = 0";
        cmd.CommandText = scopeId is null
            ? $"SELECT * FROM memories WHERE scope = $scope{archived} ORDER BY is_pinned DESC, updated_at DESC"
            : $"SELECT * FROM memories WHERE scope = $scope AND scope_id = $scopeId{archived} ORDER BY is_pinned DESC, updated_at DESC";
        cmd.Parameters.AddWithValue("$scope", scope.ToString());
        if (scopeId is not null)
            cmd.Parameters.AddWithValue("$scopeId", scopeId);
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
        var sourceJson = memory.Source is null ? null : JsonSerializer.Serialize(memory.Source);

        // Embedding is a recall-quality enhancement, not a correctness
        // requirement: if no embedding model is configured or the call
        // fails, save proceeds with a null blob and COALESCE below keeps
        // whatever embedding (if any) the row already had rather than
        // clobbering it. Bounded by the same QueryEmbedTimeout the query path
        // uses (r11 3.2): this save runs on the post-response path
        // (ConversationMemoryService.ApplyInjectedMemoryMarkersAsync /
        // MergeAndSaveAsync), so a hung embedding endpoint must not stall it
        // for the full HTTP timeout; the backfill path already handles rows
        // saved without an embedding.
        byte[]? embeddingBlob = null;
        int? embeddingDim = null;
        if (_embeddings is not null && !string.IsNullOrWhiteSpace(memory.Content))
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(QueryEmbedTimeout);
                var vector = await _embeddings.EmbedAsync(memory.Content, timeoutCts.Token);
                embeddingBlob = ToBlob(vector);
                embeddingDim = vector.Length;
            }
            catch { }
        }

        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO memories (id,category,content,created_at,updated_at,source_conversation_id,importance_score,tags_json,is_pinned,is_archived,frequency_count,last_merge_time,expiration_date,relationships_json,is_encrypted,scope,scope_id,title,source_json,embedding,embedding_dim)
            VALUES ($id,$cat,$content,$ca,$ua,$src,$imp,$tags,$pin,$arch,$freq,$merge,$exp,$rel,$enc,$scope,$scopeId,$title,$sourceJson,$embedding,$embeddingDim)
            ON CONFLICT(id) DO UPDATE SET
                category=excluded.category,
                content=excluded.content,
                scope=excluded.scope,
                scope_id=excluded.scope_id,
                title=excluded.title,
                updated_at=excluded.updated_at,
                importance_score=excluded.importance_score,
                tags_json=excluded.tags_json,
                is_pinned=excluded.is_pinned,
                is_archived=excluded.is_archived,
                frequency_count=excluded.frequency_count,
                last_merge_time=excluded.last_merge_time,
                expiration_date=excluded.expiration_date,
                relationships_json=excluded.relationships_json,
                is_encrypted=excluded.is_encrypted,
                source_json=excluded.source_json,
                embedding=COALESCE(excluded.embedding, memories.embedding),
                embedding_dim=COALESCE(excluded.embedding_dim, memories.embedding_dim)";

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
        cmd.Parameters.AddWithValue("$scope", memory.Scope.ToString());
        cmd.Parameters.AddWithValue("$scopeId", memory.ScopeId);
        cmd.Parameters.AddWithValue("$title", memory.Title);
        cmd.Parameters.AddWithValue("$sourceJson", sourceJson ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$embedding", (object?)embeddingBlob ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$embeddingDim", (object?)embeddingDim ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
        await UpsertFtsAsync(c, memory, tagsJson, ct);

        // Backfill runs off the send path (r9 01-send-path-latency.md 1.2): a
        // write is the other trigger point besides the startup pass, so a
        // freshly-created row without its own embedding (e.g. no embedding
        // service configured at save time) still becomes vector-recallable
        // once one is available, without taxing the next chat send.
        if (embeddingBlob is null && _embeddings is not null)
            _ = Task.Run(() => RunEmbeddingBackfillAsync(CancellationToken.None));
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

        List<Memory> ftsResults;
        var ftsQuery = BuildFtsQuery(q);
        if (string.IsNullOrWhiteSpace(ftsQuery))
        {
            ftsResults = await SearchLikeAsync(c, q, ct);
        }
        else
        {
            var cmd = c.CreateCommand();
            // r11 3.3: ordering by is_pinned/importance_score/updated_at made
            // the "FTS rank" half of hybrid scoring (HybridRerankAsync's
            // ftsRank[id] = 1/(i+1)) measure importance, not how well the
            // text matched - FTS5's own bm25-backed rank column was never
            // consulted. Pinned/importance influence still applies, just
            // downstream (MemoryInjectionService.EffectiveScore and its
            // pinned-first ordering), where it belongs.
            cmd.CommandText = @"
                SELECT m.*
                FROM memories m
                JOIN memories_fts f ON f.id = m.id
                WHERE memories_fts MATCH $q AND m.is_archived = 0
                ORDER BY f.rank
                LIMIT 100";
            cmd.Parameters.AddWithValue("$q", ftsQuery);

            try
            {
                var r = new List<Memory>();
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct)) r.Add(Map(rd));
                ftsResults = r;
            }
            catch (SqliteException)
            {
                ftsResults = await SearchLikeAsync(c, q, ct);
            }
        }

        // Belt and braces alongside the archive sweep (r16
        // 02-memory-integrity.md 2.3): a row whose ExpirationDate has
        // already passed but has not yet been swept by
        // ArchiveStaleMemoriesAsync must not inject either.
        var now = DateTime.UtcNow;
        ftsResults.RemoveAll(m => !m.IsPinned && m.ExpirationDate is { } expiration && expiration <= now);

        if (_embeddings is null || string.IsNullOrWhiteSpace(q))
        {
            // No embedding model configured: keep pure-FTS/LIKE ordering, but
            // still attach a rank-based relevance score so downstream memory
            // injection selection always has a real score to weigh, whether
            // or not hybrid recall is available.
            for (var i = 0; i < ftsResults.Count; i++)
                ftsResults[i].RelevanceScore = 1.0 / (i + 1);
            return ftsResults;
        }

        return await HybridRerankAsync(c, q, ftsResults, ct);
    }

    /// <summary>
    /// Blends the FTS rank with cosine similarity against a query embedding
    /// so a paraphrase with no lexical overlap can still surface. Small
    /// memory counts (hundreds, not millions) make an in-process scan over
    /// every embedded row cheap enough that no vector index is needed.
    /// </summary>
    private async Task<List<Memory>> HybridRerankAsync(SqliteConnection c, string q, List<Memory> ftsResults, CancellationToken ct)
    {
        float[] queryVector;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(QueryEmbedTimeout);
            queryVector = await _embeddings!.EmbedAsync(q, timeoutCts.Token);
        }
        catch (Exception ex)
        {
            LogQueryEmbedFallbackOnce(ex);
            for (var i = 0; i < ftsResults.Count; i++)
                ftsResults[i].RelevanceScore = 1.0 / (i + 1);
            return ftsResults;
        }

        var ftsRank = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 0; i < ftsResults.Count; i++)
            ftsRank[ftsResults[i].Id] = 1.0 / (i + 1);

        var candidates = new Dictionary<string, Memory>(StringComparer.Ordinal);
        foreach (var m in ftsResults) candidates[m.Id] = m;

        const int resultLimit = 100;

        // First pass stays a lightweight id+embedding projection (the aea2326
        // optimization was sound); every embedded row is scored here, not
        // just ones above an arbitrary cosine cutoff, so a paraphrase with no
        // keyword overlap can still surface (that is the entire point of
        // hybrid recall).
        var nonFtsScores = new List<(string Id, double Score)>();
        var cmd = c.CreateCommand();
        // Excludes an expired-but-not-yet-archived row too (r16
        // 02-memory-integrity.md 2.3), pinned rows exempt like everywhere
        // else in the lifecycle.
        cmd.CommandText = "SELECT id, embedding FROM memories WHERE is_archived = 0 AND embedding IS NOT NULL AND (is_pinned = 1 OR expiration_date IS NULL OR expiration_date > $now)";
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        var mismatchCount = 0;
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
        {
            while (await rd.ReadAsync(ct))
            {
                var id = rd.GetString(0);
                var vector = FromBlob((byte[])rd[1]);
                // A dimension mismatch (r16 02-memory-integrity.md 2.4,
                // usually a switched embedding model) makes CosineSimilarity
                // silently return 0.0 - indistinguishable from "no semantic
                // match" unless tracked separately.
                if (vector.Length != queryVector.Length)
                    mismatchCount++;
                var cosine = Math.Max(0.0, CosineSimilarity(queryVector, vector));

                if (candidates.TryGetValue(id, out var m))
                {
                    var ftsScore = ftsRank.GetValueOrDefault(id, 0.0);
                    m.RelevanceScore = (0.5 * ftsScore) + (0.5 * cosine);
                }
                else
                {
                    nonFtsScores.Add((id, 0.5 * cosine));
                }
            }
        }

        if (mismatchCount > 0)
            LogEmbeddingMismatchOnce(mismatchCount);

        // Only the ids that could plausibly make the final cut are hydrated,
        // and in a single batched query rather than one round trip per row.
        var idsToHydrate = nonFtsScores
            .OrderByDescending(s => s.Score)
            .Take(resultLimit)
            .Select(s => s.Id)
            .ToList();
        if (idsToHydrate.Count > 0)
        {
            var scoreById = nonFtsScores.ToDictionary(s => s.Id, s => s.Score, StringComparer.Ordinal);
            var hydrateCmd = c.CreateCommand();
            var paramNames = idsToHydrate.Select((_, i) => $"$p{i}").ToList();
            hydrateCmd.CommandText = $"SELECT * FROM memories WHERE id IN ({string.Join(",", paramNames)})";
            for (var i = 0; i < idsToHydrate.Count; i++)
                hydrateCmd.Parameters.AddWithValue(paramNames[i], idsToHydrate[i]);

            await using var hydrateRd = await hydrateCmd.ExecuteReaderAsync(ct);
            while (await hydrateRd.ReadAsync(ct))
            {
                var hydrated = Map(hydrateRd);
                hydrated.RelevanceScore = scoreById.GetValueOrDefault(hydrated.Id, 0.0);
                candidates[hydrated.Id] = hydrated;
            }
        }

        // An FTS hit whose row has no embedding yet (not backfilled, or the
        // embed call failed) keeps its rank-only score instead of dropping out.
        foreach (var m in candidates.Values.Where(m => m.RelevanceScore is null))
            m.RelevanceScore = ftsRank.GetValueOrDefault(m.Id, 0.0);

        return candidates.Values
            .OrderByDescending(m => m.IsPinned)
            .ThenByDescending(m => m.RelevanceScore)
            .Take(resultLimit)
            .ToList();
    }

    /// <summary>
    /// Embeds up to 200 rows without a vector, off the send path (r9
    /// 01-send-path-latency.md 1.2). Called once shortly after startup (after
    /// the embedding model warm-up) and after memory writes. Rows that fail
    /// to embed are not retried more than once per <see cref="_backfillCooldown"/>
    /// and are dropped entirely after <see cref="MaxBackfillAttemptsPerRow"/>
    /// failures for the rest of the process's life, so a down or
    /// misconfigured embedding endpoint cannot tax every write forever.
    /// </summary>
    public async Task RunEmbeddingBackfillAsync(CancellationToken ct = default)
    {
        if (_embeddings is null) return;
        if (!await _backfillGate.WaitAsync(0, ct)) return;

        try
        {
            await EnsureInitializedAsync(ct);
            await using var c = new SqliteConnection(Cs);
            await c.OpenAsync(ct);

            var pending = new List<(string Id, string Content)>();
            var select = c.CreateCommand();
            select.CommandText = "SELECT id, content FROM memories WHERE embedding IS NULL AND is_archived = 0 AND length(content) > 0 LIMIT 200";
            await using (var rd = await select.ExecuteReaderAsync(ct))
            {
                while (await rd.ReadAsync(ct))
                    pending.Add((rd.GetString(0), rd.GetString(1)));
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var failures = 0;
            foreach (var (id, content) in pending)
            {
                if (_backfillState.TryGetValue(id, out var state))
                {
                    if (state.Attempts >= MaxBackfillAttemptsPerRow) continue;
                    if (now < state.NextAttemptUtc) continue;
                }

                try
                {
                    var vector = await _embeddings.EmbedAsync(content, ct);
                    var update = c.CreateCommand();
                    update.CommandText = "UPDATE memories SET embedding = $embedding, embedding_dim = $dim WHERE id = $id";
                    update.Parameters.AddWithValue("$embedding", ToBlob(vector));
                    update.Parameters.AddWithValue("$dim", vector.Length);
                    update.Parameters.AddWithValue("$id", id);
                    await update.ExecuteNonQueryAsync(ct);
                    _backfillState.Remove(id);
                }
                catch
                {
                    var attempts = (_backfillState.TryGetValue(id, out var previous) ? previous.Attempts : 0) + 1;
                    _backfillState[id] = (now.Add(_backfillCooldown), attempts);
                    failures++;
                }
            }

            if (failures > 0)
                _runtimeLogs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Rag,
                    $"Embedding backfill: {failures} row(s) failed to embed; will retry after a cooldown."));
        }
        finally
        {
            _backfillGate.Release();
        }
    }

    private void LogQueryEmbedFallbackOnce(Exception ex)
    {
        if (_queryEmbedFallbackLogged) return;
        _queryEmbedFallbackLogged = true;
        _runtimeLogs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Rag,
            $"Query embedding at {ResolveEmbeddingEndpoint()} did not complete in time; memory recall falls back to FTS-ranked results ({ex.GetType().Name}: {ex.Message})."));
    }

    private bool _embeddingMismatchLogged;

    /// <summary>
    /// One warning per process (r16 02-memory-integrity.md 2.4), same
    /// pattern as <see cref="LogQueryEmbedFallbackOnce"/>: an embedding
    /// model switch leaves every old row's vector at the wrong
    /// dimensionality, silently zeroing its semantic score forever unless
    /// surfaced somewhere.
    /// </summary>
    private void LogEmbeddingMismatchOnce(int mismatchCount)
    {
        if (_embeddingMismatchLogged) return;
        _embeddingMismatchLogged = true;
        _runtimeLogs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Rag,
            $"{mismatchCount} memory embedding(s) have a different dimensionality than the current embedding model; their semantic recall score is 0 until re-embedded from the Memories page."));
    }

    /// <summary>
    /// Counts memory rows whose stored embedding no longer matches the
    /// currently configured embedding model's dimensionality (r16
    /// 02-memory-integrity.md 2.4) - the signal that a model switch has
    /// silently zeroed hybrid recall for those rows. A live probe embed
    /// determines the current dimensionality; 0 when no embedding service
    /// is configured or the probe fails.
    /// </summary>
    public async Task<int> GetEmbeddingMismatchCountAsync(CancellationToken ct = default)
    {
        if (_embeddings is null) return 0;
        await EnsureInitializedAsync(ct);

        var currentDim = await ProbeCurrentDimensionAsync(ct);
        if (currentDim is null) return 0;

        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM memories WHERE embedding IS NOT NULL AND length(embedding) != $bytes";
        cmd.Parameters.AddWithValue("$bytes", currentDim.Value * sizeof(float));
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
        if (count > 0)
            LogEmbeddingMismatchOnce(count);
        return count;
    }

    /// <summary>
    /// Clears the embedding (and its dimension) on every row that mismatches
    /// the current embedding model, then kicks off a background re-embed.
    /// User-clicked only from the Memories page (r16 02-memory-integrity.md
    /// 2.4 explicit rejection: a settings/model change must never trigger
    /// this automatically). Returns how many rows were cleared.
    /// </summary>
    public async Task<int> ClearMismatchedEmbeddingsAsync(CancellationToken ct = default)
    {
        if (_embeddings is null) return 0;
        await EnsureInitializedAsync(ct);

        var currentDim = await ProbeCurrentDimensionAsync(ct);
        if (currentDim is null) return 0;

        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE memories SET embedding = NULL, embedding_dim = NULL WHERE embedding IS NOT NULL AND length(embedding) != $bytes";
        cmd.Parameters.AddWithValue("$bytes", currentDim.Value * sizeof(float));
        var cleared = await cmd.ExecuteNonQueryAsync(ct);

        if (cleared > 0)
            _ = Task.Run(() => RunEmbeddingBackfillAsync(CancellationToken.None));

        return cleared;
    }

    private async Task<int?> ProbeCurrentDimensionAsync(CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(QueryEmbedTimeout);
            var probe = await _embeddings!.EmbedAsync("hermaeus-memory-dimension-probe", timeoutCts.Token);
            return probe.Length;
        }
        catch
        {
            return null;
        }
    }

    private string ResolveEmbeddingEndpoint()
    {
        var configured = _settings.Settings.Rag.EmbeddingBaseUrl?.Trim();
        return !string.IsNullOrWhiteSpace(configured)
            ? configured.TrimEnd('/')
            : _settings.Settings.Llm.LlamaCppBaseUrl.TrimEnd('/');
    }

    public async Task MarkRecalledAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var idList = ids?.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal).ToList() ?? [];
        if (idList.Count == 0) return;

        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var now = DateTime.UtcNow.ToString("O");
        foreach (var id in idList)
        {
            var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE memories SET recall_count = recall_count + 1, last_recalled_at = $now WHERE id = $id";
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<int> ArchiveStaleMemoriesAsync(double importanceFloor = 0.05, int unrecalledForDays = 180, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var candidates = await GetAllAsync(includeArchived: false, ct);
        var now = DateTime.UtcNow;
        var archived = 0;

        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);

        foreach (var memory in candidates)
        {
            if (memory.IsPinned) continue;

            // ExpirationDate (r16 02-memory-integrity.md 2.3): set by
            // auto-summary when Memory.AutoArchiveAfterDays > 0, but never
            // enforced anywhere before this - a placebo setting. A past
            // expiry archives regardless of the staleness/importance rule
            // below, same as every other lifecycle rule pinning overrides.
            var expired = memory.ExpirationDate is { } expiration && expiration <= now;
            if (!expired)
            {
                var reference = memory.LastRecalledAt ?? memory.UpdatedAt;
                if ((now - reference).TotalDays < unrecalledForDays) continue;
                if (MemoryLifecycle.ComputeEffectiveImportance(memory, now) >= importanceFloor) continue;
            }

            // Narrow update (r11 3.5): the full SaveAsync re-embeds unchanged
            // content (one HTTP call per archived row) purely to flip a
            // status flag. Archiving touches only is_archived/updated_at;
            // content/tags/title are unchanged, so the FTS index needs no
            // rewrite either.
            var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE memories SET is_archived = 1, updated_at = $ua WHERE id = $id";
            cmd.Parameters.AddWithValue("$ua", now.ToString("O"));
            cmd.Parameters.AddWithValue("$id", memory.Id);
            await cmd.ExecuteNonQueryAsync(ct);
            archived++;
        }

        return archived;
    }

    private static byte[] ToBlob(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] FromBlob(byte[] blob)
    {
        var vector = new float[blob.Length / sizeof(float)];
        Buffer.BlockCopy(blob, 0, vector, 0, blob.Length);
        return vector;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0.0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        return na <= 0 || nb <= 0 ? 0.0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
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

    public async Task<List<Memory>> GetRecentByConversationAsync(string conversationId, int limit = 10, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM memories WHERE source_conversation_id = $src AND is_archived = 0 ORDER BY updated_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$src", conversationId);
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

    private static async Task<bool> TableExistsAsync(SqliteConnection c, string table, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $table";
        cmd.Parameters.AddWithValue("$table", table);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is not null;
    }

    private static async Task<List<Memory>> SearchLikeAsync(SqliteConnection c, string q, CancellationToken ct)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM memories WHERE (content LIKE $q OR category LIKE $q OR tags_json LIKE $q) AND is_archived = 0 ORDER BY is_pinned DESC, importance_score DESC, updated_at DESC LIMIT 100";
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

    private static Memory Map(SqliteDataReader r)
    {
        var sourceConversationId = GetStringNullable(r, "source_conversation_id");
        return new Memory
        {
            Id = GetString(r, "id"),
            Category = GetString(r, "category", "facts"),
            Content = GetString(r, "content"),
            CreatedAt = SqliteDateTime.Parse(GetString(r, "created_at")),
            UpdatedAt = SqliteDateTime.Parse(GetString(r, "updated_at")),
            SourceConversationId = sourceConversationId,
            Source = ResolveSource(GetStringNullable(r, "source_json"), sourceConversationId),
            ImportanceScore = GetDouble(r, "importance_score", 0.5),
            Tags = JsonSerializer.Deserialize<List<string>>(GetString(r, "tags_json", "[]")) ?? [],
            IsPinned = GetInt(r, "is_pinned") != 0,
            IsArchived = GetInt(r, "is_archived") != 0,
            FrequencyCount = GetInt(r, "frequency_count", 1),
            LastMergeTime = GetDateTimeNullable(r, "last_merge_time"),
            ExpirationDate = GetDateTimeNullable(r, "expiration_date"),
            RelatedMemoryIds = JsonSerializer.Deserialize<List<string>>(GetString(r, "relationships_json", "[]")) ?? [],
            IsEncrypted = GetInt(r, "is_encrypted") != 0,
            Scope = Enum.TryParse<MemoryScope>(GetString(r, "scope", "Global"), out var scope) ? scope : MemoryScope.Global,
            ScopeId = GetString(r, "scope_id"),
            Title = GetString(r, "title"),
            RecallCount = GetInt(r, "recall_count"),
            LastRecalledAt = GetDateTimeNullable(r, "last_recalled_at")
        };
    }

    /// <summary>
    /// Rows written before <c>source_json</c> existed (or written by a path
    /// that only ever set <c>SourceConversationId</c>) backfill a
    /// <see cref="SourceReference"/> from that conversation id at read time
    /// instead of a data rewrite migration (docs/review/03-next-level-roadmap.md
    /// Phase 1).
    /// </summary>
    private static SourceReference? ResolveSource(string? sourceJson, string? sourceConversationId)
    {
        if (!string.IsNullOrWhiteSpace(sourceJson))
        {
            try
            {
                var deserialized = JsonSerializer.Deserialize<SourceReference>(sourceJson);
                if (deserialized is not null)
                    return deserialized;
            }
            catch (JsonException) { }
        }

        return string.IsNullOrWhiteSpace(sourceConversationId)
            ? null
            : new SourceReference(ProvenanceKind.Memory, "Conversation", Locator: sourceConversationId);
    }

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
        return r.IsDBNull(ordinal) ? null : SqliteDateTime.ParseNullable(r.GetString(ordinal));
    }

    private static List<string> NormalizeTags(IEnumerable<string> tags) =>
        tags
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
