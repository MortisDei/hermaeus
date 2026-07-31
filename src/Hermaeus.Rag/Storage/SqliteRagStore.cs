using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Retrieval;
using Microsoft.Data.Sqlite;

namespace Hermaeus.Rag.Storage;

/// <summary>
/// Stores RAG datasets, chunks, embeddings and BM25 stats in SQLite.
/// Shares the same DB file as the conversation store (same data dir).
/// Embeddings are stored as raw float32 BLOBs for fast deserialisation.
/// </summary>
public sealed class SqliteRagStore
{
    private const int SchemaVersion = 2;
    private readonly ISettingsService _settings;
    private readonly IRuntimeLogService? _logs;
    private string _initializedPath = string.Empty;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private string _cachedConnectionString = string.Empty;
    private string _cachedConnectionPath = string.Empty;
    private string DbPath
    {
        get
        {
            var dir = ResolveDataRoot();
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "conversations.db");
        }
    }
    private string Cs
    {
        get
        {
            var path = DbPath;
            if (!string.Equals(_cachedConnectionPath, path, StringComparison.Ordinal))
            {
                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Pooling = true,
                    ForeignKeys = true
                };

                _cachedConnectionString = builder.ToString();
                _cachedConnectionPath = path;
            }

            return _cachedConnectionString;
        }
    }

    public SqliteRagStore(ISettingsService settings, IRuntimeLogService? logs = null)
    {
        _settings = settings;
        _logs = logs;
    }

    private string ResolveDataRoot()
    {
        var configured = _settings.Settings.DataManagement.DataRootDirectory?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hermaeus");
    }

    // ── Init ─────────────────────────────────────────────────────────────────

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
            PRAGMA journal_mode=WAL;

            CREATE TABLE IF NOT EXISTS rag_datasets (
                id           TEXT PRIMARY KEY,
                name         TEXT NOT NULL UNIQUE,
                description  TEXT NOT NULL DEFAULT '',
                chunk_count  INTEGER NOT NULL DEFAULT 0,
                created_at   TEXT NOT NULL,
                config_json  TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS rag_chunks (
                id           TEXT PRIMARY KEY,
                dataset_id   TEXT NOT NULL REFERENCES rag_datasets(id) ON DELETE CASCADE,
                source_file  TEXT NOT NULL,
                source_path  TEXT NOT NULL DEFAULT '',
                source_hash  TEXT NOT NULL DEFAULT '',
                source_modified_utc TEXT,
                source_title TEXT NOT NULL,
                content      TEXT NOT NULL,
                chunk_index  INTEGER NOT NULL DEFAULT 0,
                chunk_total  INTEGER NOT NULL DEFAULT 1,
                parent_id    TEXT,
                token_count  INTEGER NOT NULL DEFAULT 0,
                embedding    BLOB,
                created_at   TEXT NOT NULL
            );

            -- r27 02-retrieval-that-scales.md 2.2: candidate generation for BM25.
            -- Mirrors what ConversationStore already does for conversations.
            -- FTS5 finds the candidates; Bm25Scorer still scores them, so the
            -- ranking among chunks that matter does not move.
            CREATE VIRTUAL TABLE IF NOT EXISTS rag_chunks_fts USING fts5(
                id UNINDEXED,
                dataset_id UNINDEXED,
                content
            );

            CREATE INDEX IF NOT EXISTS idx_rag_chunks_ds     ON rag_chunks(dataset_id);
            CREATE INDEX IF NOT EXISTS idx_rag_chunks_parent ON rag_chunks(parent_id);
            CREATE INDEX IF NOT EXISTS idx_rag_chunks_source ON rag_chunks(dataset_id, source_path);

            CREATE TABLE IF NOT EXISTS rag_bm25_stats (
                dataset_id TEXT PRIMARY KEY REFERENCES rag_datasets(id) ON DELETE CASCADE,
                stats_json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS rag_query_traces (
                id TEXT PRIMARY KEY,
                dataset_id TEXT NOT NULL,
                question TEXT NOT NULL,
                expanded_question TEXT NOT NULL,
                query_variants_json TEXT NOT NULL DEFAULT '[]',
                planner_notes TEXT NOT NULL DEFAULT '',
                context_token_budget INTEGER NOT NULL DEFAULT 0,
                context_packing_summary TEXT NOT NULL DEFAULT '',
                refused INTEGER NOT NULL DEFAULT 0,
                refusal_reason TEXT NOT NULL DEFAULT '',
                model_id TEXT NOT NULL,
                retrieval_latency_ms INTEGER NOT NULL DEFAULT 0,
                total_latency_ms INTEGER NOT NULL DEFAULT 0,
                grounding_score REAL NOT NULL DEFAULT 0,
                grounding_mode TEXT NOT NULL DEFAULT 'TokenOverlap',
                retrieved_chunks_json TEXT NOT NULL DEFAULT '[]',
                selected_context_json TEXT NOT NULL DEFAULT '[]',
                created_at TEXT NOT NULL
            );";
            await cmd.ExecuteNonQueryAsync(ct);
            await SqliteMigrationRunner.ApplyAsync(c, "rag", SchemaVersion,
            [
                new SqliteMigration(1, async (db, token) =>
                {
                    var changed = false;
                    changed |= await EnsureColumnAsync(db, "rag_chunks", "source_path", "TEXT NOT NULL DEFAULT ''", token);
                    changed |= await EnsureColumnAsync(db, "rag_chunks", "source_hash", "TEXT NOT NULL DEFAULT ''", token);
                    changed |= await EnsureColumnAsync(db, "rag_chunks", "source_modified_utc", "TEXT", token);
                    changed |= await EnsureColumnAsync(db, "rag_chunks", "chunk_kind", "TEXT NOT NULL DEFAULT 'PlainText'", token);
                    changed |= await EnsureColumnAsync(db, "rag_chunks", "heading_path", "TEXT", token);
                    changed |= await EnsureColumnAsync(db, "rag_chunks", "code_symbol_info", "TEXT", token);
                    changed |= await EnsureColumnAsync(db, "rag_chunks", "page_number", "INTEGER", token);
                    changed |= await EnsureColumnAsync(db, "rag_chunks", "event_type", "TEXT", token);
                    changed |= await EnsureColumnAsync(db, "rag_chunks", "source_url", "TEXT", token);
                    changed |= await EnsureColumnAsync(db, "rag_query_traces", "query_variants_json", "TEXT NOT NULL DEFAULT '[]'", token);
                    changed |= await EnsureColumnAsync(db, "rag_query_traces", "planner_notes", "TEXT NOT NULL DEFAULT ''", token);
                    changed |= await EnsureColumnAsync(db, "rag_query_traces", "context_token_budget", "INTEGER NOT NULL DEFAULT 0", token);
                    changed |= await EnsureColumnAsync(db, "rag_query_traces", "context_packing_summary", "TEXT NOT NULL DEFAULT ''", token);
                    changed |= await EnsureColumnAsync(db, "rag_query_traces", "refused", "INTEGER NOT NULL DEFAULT 0", token);
                    changed |= await EnsureColumnAsync(db, "rag_query_traces", "refusal_reason", "TEXT NOT NULL DEFAULT ''", token);

                    // r10 01-rag-correctness.md 1.1: parent-child retrieval filtered
                    // on parent_id IS NULL, which excludes every embedded child
                    // instead of the unembedded parent bodies. is_parent is an
                    // explicit flag so the filter's intent cannot invert again.
                    var addedIsParent = await EnsureColumnAsync(db, "rag_chunks", "is_parent", "INTEGER NOT NULL DEFAULT 0", token);
                    changed |= addedIsParent;
                    if (addedIsParent)
                    {
                        await using var backfill = db.CreateCommand();
                        backfill.CommandText =
                            "UPDATE rag_chunks SET is_parent = 1 WHERE id IN " +
                            "(SELECT parent_id FROM rag_chunks WHERE parent_id IS NOT NULL)";
                        await backfill.ExecuteNonQueryAsync(token);
                    }

                    // r10 01-rag-correctness.md 1.7: SaveDatasetAsync never
                    // wrote these, so the Add-to-dataset folder pre-fill only
                    // worked within one session.
                    changed |= await EnsureColumnAsync(db, "rag_datasets", "last_ingest_path", "TEXT NOT NULL DEFAULT ''", token);
                    changed |= await EnsureColumnAsync(db, "rag_datasets", "last_ingest_utc", "TEXT", token);

                    return changed;
                }),
                new SqliteMigration(2, async (db, token) =>
                    await EnsureColumnAsync(db, "rag_datasets", "project_id", "TEXT NOT NULL DEFAULT ''", token))
            ], ct);

            // r10 01-rag-correctness.md 1.2: DeleteDatasetAsync used to rely on
            // ON DELETE CASCADE, but no connection ever enabled foreign key
            // enforcement, so every deleted dataset left its chunks and BM25
            // stats behind forever. One-time sweep for rows already orphaned
            // by that bug; DeleteDatasetAsync now deletes explicitly so this
            // should find nothing on a healthy store going forward.
            await CleanupOrphanedRowsAsync(c, ct);

            _initializedPath = dbPath;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private async Task CleanupOrphanedRowsAsync(SqliteConnection c, CancellationToken ct)
    {
        await using var tx = await c.BeginTransactionAsync(ct);

        await using var chunksCmd = c.CreateCommand();
        chunksCmd.Transaction = (SqliteTransaction)tx;
        chunksCmd.CommandText = "DELETE FROM rag_chunks WHERE dataset_id NOT IN (SELECT id FROM rag_datasets)";
        var orphanedChunks = await chunksCmd.ExecuteNonQueryAsync(ct);

        await using var ftsCmd = c.CreateCommand();
        ftsCmd.Transaction = (SqliteTransaction)tx;
        ftsCmd.CommandText = "DELETE FROM rag_chunks_fts WHERE id NOT IN (SELECT id FROM rag_chunks)";
        await ftsCmd.ExecuteNonQueryAsync(ct);

        await using var statsCmd = c.CreateCommand();
        statsCmd.Transaction = (SqliteTransaction)tx;
        statsCmd.CommandText = "DELETE FROM rag_bm25_stats WHERE dataset_id NOT IN (SELECT id FROM rag_datasets)";
        var orphanedStats = await statsCmd.ExecuteNonQueryAsync(ct);

        await tx.CommitAsync(ct);

        if (orphanedChunks > 0 || orphanedStats > 0)
            _logs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
                $"RAG store cleanup: removed {orphanedChunks} orphaned chunk row(s) and {orphanedStats} orphaned BM25 stats row(s) left behind by deleted datasets."));
    }

    private static async Task<bool> EnsureColumnAsync(
        SqliteConnection c,
        string table,
        string column,
        string definition,
        CancellationToken ct)
    {
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA table_info({table})";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        await using var alter = c.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        await alter.ExecuteNonQueryAsync(ct);
        return true;
    }

    // ── Datasets ─────────────────────────────────────────────────────────────

    public async Task<List<RagDataset>> GetDatasetsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM rag_datasets ORDER BY name";
        var list = new List<RagDataset>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapDataset(r));
        return list;
    }

    public async Task SaveDatasetAsync(RagDataset ds, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO rag_datasets (id,name,description,chunk_count,created_at,config_json,last_ingest_path,last_ingest_utc,project_id)
            VALUES ($id,$name,$desc,$cc,$ca,$cfg,$lip,$liu,$pid)
            ON CONFLICT(id) DO UPDATE SET
                name=excluded.name, description=excluded.description,
                chunk_count=excluded.chunk_count, config_json=excluded.config_json,
                last_ingest_path=excluded.last_ingest_path, last_ingest_utc=excluded.last_ingest_utc,
                project_id=excluded.project_id";
        cmd.Parameters.AddWithValue("$id",   ds.Id);
        cmd.Parameters.AddWithValue("$name", ds.Name);
        cmd.Parameters.AddWithValue("$desc", ds.Description);
        cmd.Parameters.AddWithValue("$cc",   ds.ChunkCount);
        cmd.Parameters.AddWithValue("$ca",   ds.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$cfg",  JsonSerializer.Serialize(ds.Config));
        cmd.Parameters.AddWithValue("$lip",  ds.LastIngestPath);
        cmd.Parameters.AddWithValue("$liu",  (object?)ds.LastIngestUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pid",  ds.ProjectId.Trim());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteDatasetAsync(string datasetId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);

        // Explicit deletes rather than relying on ON DELETE CASCADE: correctness
        // must not depend on every connection remembering to enable the
        // foreign_keys pragma (r10 01-rag-correctness.md 1.2).
        await using (var chunksCmd = c.CreateCommand())
        {
            chunksCmd.Transaction = (SqliteTransaction)tx;
            chunksCmd.CommandText = "DELETE FROM rag_chunks WHERE dataset_id = $id";
            chunksCmd.Parameters.AddWithValue("$id", datasetId);
            await chunksCmd.ExecuteNonQueryAsync(ct);
        }

        await using (var ftsCmd = c.CreateCommand())
        {
            ftsCmd.Transaction = (SqliteTransaction)tx;
            ftsCmd.CommandText = "DELETE FROM rag_chunks_fts WHERE dataset_id = $id";
            ftsCmd.Parameters.AddWithValue("$id", datasetId);
            await ftsCmd.ExecuteNonQueryAsync(ct);
        }

        await using (var statsCmd = c.CreateCommand())
        {
            statsCmd.Transaction = (SqliteTransaction)tx;
            statsCmd.CommandText = "DELETE FROM rag_bm25_stats WHERE dataset_id = $id";
            statsCmd.Parameters.AddWithValue("$id", datasetId);
            await statsCmd.ExecuteNonQueryAsync(ct);
        }

        await using (var datasetCmd = c.CreateCommand())
        {
            datasetCmd.Transaction = (SqliteTransaction)tx;
            datasetCmd.CommandText = "DELETE FROM rag_datasets WHERE id = $id";
            datasetCmd.Parameters.AddWithValue("$id", datasetId);
            await datasetCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    // ── Chunks ────────────────────────────────────────────────────────────────

    public async Task SaveChunksBatchAsync(IEnumerable<RagChunk> chunks, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var cmd = c.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = @"
            INSERT OR REPLACE INTO rag_chunks
                (id,dataset_id,source_file,source_path,source_hash,source_modified_utc,source_title,content,chunk_index,chunk_total,
                 parent_id,is_parent,token_count,embedding,created_at,chunk_kind,heading_path,code_symbol_info,page_number,event_type,source_url)
            VALUES ($id,$ds,$sf,$sp,$sh,$sm,$st,$ct,$ci,$ctot,$pid,$isp,$tc,$emb,$ca,$ck,$hp,$csi,$pn,$et,$su)";

        var pId   = cmd.Parameters.Add("$id",   SqliteType.Text);
        var pDs   = cmd.Parameters.Add("$ds",   SqliteType.Text);
        var pSf   = cmd.Parameters.Add("$sf",   SqliteType.Text);
        var pSp   = cmd.Parameters.Add("$sp",   SqliteType.Text);
        var pSh   = cmd.Parameters.Add("$sh",   SqliteType.Text);
        var pSm   = cmd.Parameters.Add("$sm",   SqliteType.Text);
        var pSt   = cmd.Parameters.Add("$st",   SqliteType.Text);
        var pCt   = cmd.Parameters.Add("$ct",   SqliteType.Text);
        var pCi   = cmd.Parameters.Add("$ci",   SqliteType.Integer);
        var pCtot = cmd.Parameters.Add("$ctot", SqliteType.Integer);
        var pPid  = cmd.Parameters.Add("$pid",  SqliteType.Text);
        var pIsp  = cmd.Parameters.Add("$isp",  SqliteType.Integer);
        var pTc   = cmd.Parameters.Add("$tc",   SqliteType.Integer);
        var pEmb  = cmd.Parameters.Add("$emb",  SqliteType.Blob);
        var pCa   = cmd.Parameters.Add("$ca",   SqliteType.Text);
        var pCk   = cmd.Parameters.Add("$ck",   SqliteType.Text);
        var pHp   = cmd.Parameters.Add("$hp",   SqliteType.Text);
        var pCsi  = cmd.Parameters.Add("$csi",  SqliteType.Text);
        var pPn   = cmd.Parameters.Add("$pn",   SqliteType.Integer);
        var pEt   = cmd.Parameters.Add("$et",   SqliteType.Text);
        var pSu   = cmd.Parameters.Add("$su",   SqliteType.Text);

        foreach (var chunk in chunks)
        {
            pId.Value   = chunk.Id;
            pDs.Value   = chunk.DatasetId;
            pSf.Value   = chunk.SourceFile;
            pSp.Value   = chunk.SourcePath;
            pSh.Value   = chunk.SourceHash;
            pSm.Value   = (object?)chunk.SourceModifiedUtc?.ToString("O") ?? DBNull.Value;
            pSt.Value   = chunk.SourceTitle;
            pCt.Value   = chunk.Content;
            pCi.Value   = chunk.ChunkIndex;
            pCtot.Value = chunk.ChunkTotal;
            pPid.Value  = (object?)chunk.ParentId ?? DBNull.Value;
            pIsp.Value  = chunk.IsParent ? 1 : 0;
            pTc.Value   = chunk.TokenCount;
            pEmb.Value  = EmbeddingToBytes(chunk.Embedding);
            pCa.Value   = chunk.CreatedAt.ToString("O");
            pCk.Value   = chunk.ChunkKind.ToString();
            pHp.Value   = (object?)chunk.HeadingPath ?? DBNull.Value;
            pCsi.Value  = (object?)chunk.CodeSymbolInfo ?? DBNull.Value;
            pPn.Value   = (object?)chunk.PageNumber ?? DBNull.Value;
            pEt.Value   = (object?)chunk.EventType ?? DBNull.Value;
            pSu.Value   = (object?)chunk.SourceUrl ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync(ct);
            // r27 2.2: the FTS mirror is written in the same transaction as the
            // row it mirrors, delete-then-insert so a re-ingest of the same
            // chunk id replaces rather than duplicates.
            if (!chunk.IsParent)
                await UpsertChunkFtsAsync(c, (SqliteTransaction)tx, chunk.Id, chunk.DatasetId, chunk.Content, ct);
        }
        await tx.CommitAsync(ct);
    }

    private static async Task UpsertChunkFtsAsync(
        SqliteConnection c, SqliteTransaction tx, string id, string datasetId, string content, CancellationToken ct)
    {
        await using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM rag_chunks_fts WHERE id = $id";
            del.Parameters.AddWithValue("$id", id);
            await del.ExecuteNonQueryAsync(ct);
        }

        await using var ins = c.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = "INSERT INTO rag_chunks_fts (id, dataset_id, content) VALUES ($id, $ds, $content)";
        ins.Parameters.AddWithValue("$id", id);
        ins.Parameters.AddWithValue("$ds", datasetId);
        ins.Parameters.AddWithValue("$content", content);
        await ins.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<RagChunk>> GetChunksAsync(string datasetId, bool includeEmbeddings = true, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = includeEmbeddings
            ? "SELECT * FROM rag_chunks WHERE dataset_id=$ds AND is_parent = 0 ORDER BY source_file, chunk_index"
            : "SELECT id,dataset_id,source_file,source_path,source_hash,source_modified_utc,source_title,content,chunk_index,chunk_total,parent_id,is_parent,token_count,NULL AS embedding,created_at,chunk_kind,heading_path,code_symbol_info,page_number,event_type,source_url FROM rag_chunks WHERE dataset_id=$ds AND is_parent = 0 ORDER BY source_file, chunk_index";
        cmd.Parameters.AddWithValue("$ds", datasetId);
        var list = new List<RagChunk>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapChunk(r));
        return list;
    }

    /// <summary>
    /// r10 02-rag-quality.md 2.5: RagDatasetHealthService only needs source
    /// path, chunk index, and modified timestamp. This runs after every
    /// ingest, delete, and app load, so loading full chunk content
    /// (GetChunksAsync) made the RAG tab slow to open on big corpora.
    /// </summary>
    public async Task<List<RagChunkHealthInfo>> GetChunkHealthInfoAsync(string datasetId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT source_path, chunk_index, source_modified_utc FROM rag_chunks WHERE dataset_id=$ds AND is_parent = 0";
        cmd.Parameters.AddWithValue("$ds", datasetId);
        var list = new List<RagChunkHealthInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var sourcePath = r.IsDBNull(0) ? string.Empty : r.GetString(0);
            var chunkIndex = r.IsDBNull(1) ? 0 : r.GetInt32(1);
            var sourceModifiedUtc = r.IsDBNull(2) ? (DateTime?)null : TryParseDate(r.GetString(2));
            list.Add(new RagChunkHealthInfo(sourcePath, chunkIndex, sourceModifiedUtc));
        }
        return list;
    }

    /// <summary>
    /// r27 02-retrieval-that-scales.md 2.3: the semantic scan index for a
    /// dataset: chunk ids and one contiguous embedding block, without content.
    /// This is the only read the in-memory cache needs.
    /// </summary>
    public async Task<RagScanIndex> GetScanIndexAsync(string datasetId, string embeddingModel, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, embedding FROM rag_chunks WHERE dataset_id=$ds AND is_parent = 0 ORDER BY source_file, chunk_index";
        cmd.Parameters.AddWithValue("$ds", datasetId);

        var ids = new List<string>();
        var vectors = new List<float[]>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                if (r.IsDBNull(1))
                    continue;
                var vector = BytesToEmbedding((byte[])r[1]);
                if (vector.Length == 0)
                    continue;
                ids.Add(r.GetString(0));
                vectors.Add(vector);
            }
        }

        return BuildScanIndex(ids, vectors, embeddingModel);
    }

    private static RagScanIndex BuildScanIndex(List<string> ids, List<float[]> vectors, string embeddingModel)
    {
        if (vectors.Count == 0)
            return new RagScanIndex([], [], 0, embeddingModel);

        // One contiguous block means one dimension per dataset by construction,
        // which is what lets the mismatch check move from the chunk to the block.
        var dimension = vectors
            .GroupBy(v => v.Length)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First().Key;

        var keptIds = new List<string>(ids.Count);
        var kept = new List<float[]>(vectors.Count);
        for (var i = 0; i < vectors.Count; i++)
        {
            if (vectors[i].Length != dimension)
                continue;
            keptIds.Add(ids[i]);
            kept.Add(vectors[i]);
        }

        var block = new float[(long)kept.Count * dimension];
        for (var i = 0; i < kept.Count; i++)
            kept[i].CopyTo(block, i * dimension);

        return new RagScanIndex([.. keptIds], block, dimension, embeddingModel);
    }

    /// <summary>
    /// r27 2.5: content for the handful of chunks that survived ranking, read by
    /// id in one query. Same shape as GetChunksAsync's includeEmbeddings: false
    /// projection: everything except the embedding blob.
    /// </summary>
    public async Task<List<RagChunk>> GetChunksByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return [];

        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        var names = new List<string>(ids.Count);
        var index = 0;
        foreach (var id in ids)
        {
            var name = $"$id{index++}";
            names.Add(name);
            cmd.Parameters.AddWithValue(name, id);
        }

        cmd.CommandText =
            "SELECT id,dataset_id,source_file,source_path,source_hash,source_modified_utc,source_title,content,chunk_index,chunk_total," +
            "parent_id,is_parent,token_count,NULL AS embedding,created_at,chunk_kind,heading_path,code_symbol_info,page_number,event_type,source_url " +
            $"FROM rag_chunks WHERE id IN ({string.Join(",", names)})";

        var list = new List<RagChunk>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapChunk(reader));
        return list;
    }

    /// <summary>
    /// r27 2.2: BM25 candidate ids from the FTS index rather than by tokenising
    /// the whole corpus once per query variant. Falls back to LIKE for malformed
    /// user input or unsupported MATCH syntax, exactly as ConversationStore does.
    /// </summary>
    public async Task<List<string>> SearchChunkIdsAsync(string datasetId, string query, int limit, CancellationToken ct = default)
    {
        var terms = query?.Trim() ?? string.Empty;
        if (terms.Length == 0 || limit <= 0)
            return [];

        await EnsureInitializedAsync(ct);
        await EnsureFtsBackfilledAsync(datasetId, ct);

        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id FROM rag_chunks_fts WHERE dataset_id = $ds AND rag_chunks_fts MATCH $q LIMIT $limit";
        cmd.Parameters.AddWithValue("$ds", datasetId);
        cmd.Parameters.AddWithValue("$q", BuildMatchQuery(terms));
        cmd.Parameters.AddWithValue("$limit", limit);

        try
        {
            var ids = new List<string>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) ids.Add(r.GetString(0));
            return ids;
        }
        catch (SqliteException)
        {
            return await SearchChunkIdsLikeAsync(c, datasetId, terms, limit, ct);
        }
    }

    /// <summary>
    /// Each token as its own OR term, quoted so punctuation cannot be read as
    /// FTS5 syntax. Candidate generation wants recall; Bm25Scorer does the
    /// ranking, so an over-wide candidate set costs time, not quality.
    /// </summary>
    private static string BuildMatchQuery(string query)
    {
        var tokens = Bm25Scorer.Tokenize(query)
            .Where(t => t.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .Select(t => $"\"{t}\"")
            .ToList();

        return tokens.Count == 0 ? $"\"{query.Replace("\"", string.Empty)}\"" : string.Join(" OR ", tokens);
    }

    private static async Task<List<string>> SearchChunkIdsLikeAsync(
        SqliteConnection c, string datasetId, string query, int limit, CancellationToken ct)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id FROM rag_chunks WHERE dataset_id = $ds AND is_parent = 0 AND content LIKE $like LIMIT $limit";
        cmd.Parameters.AddWithValue("$ds", datasetId);
        cmd.Parameters.AddWithValue("$like", $"%{query}%");
        cmd.Parameters.AddWithValue("$limit", limit);

        var ids = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) ids.Add(r.GetString(0));
        return ids;
    }

    /// <summary>
    /// r27 2.2: existing datasets predate the FTS table. Backfilled lazily on
    /// first search of a dataset rather than at startup, so an install that
    /// never opens the RAG panel never pays for it. Idempotent: rows are keyed
    /// by chunk id and deleted before insert, so running it twice cannot
    /// duplicate anything.
    /// </summary>
    private async Task EnsureFtsBackfilledAsync(string datasetId, CancellationToken ct)
    {
        if (_ftsBackfilled.ContainsKey(datasetId))
            return;

        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);

        await using (var check = c.CreateCommand())
        {
            check.CommandText =
                "SELECT (SELECT COUNT(*) FROM rag_chunks WHERE dataset_id=$ds AND is_parent = 0) - " +
                "(SELECT COUNT(*) FROM rag_chunks_fts WHERE dataset_id=$ds)";
            check.Parameters.AddWithValue("$ds", datasetId);
            var missing = Convert.ToInt64(await check.ExecuteScalarAsync(ct) ?? 0L);
            if (missing <= 0)
            {
                _ftsBackfilled[datasetId] = true;
                return;
            }
        }

        await using (var tx = await c.BeginTransactionAsync(ct))
        {
            await using var backfill = c.CreateCommand();
            backfill.Transaction = (SqliteTransaction)tx;
            backfill.CommandText =
                "DELETE FROM rag_chunks_fts WHERE dataset_id = $ds;" +
                "INSERT INTO rag_chunks_fts (id, dataset_id, content) " +
                "SELECT id, dataset_id, content FROM rag_chunks WHERE dataset_id = $ds AND is_parent = 0;";
            backfill.Parameters.AddWithValue("$ds", datasetId);
            await backfill.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }

        _ftsBackfilled[datasetId] = true;
        _logs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
            $"RAG search index backfilled for dataset {datasetId}."));
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _ftsBackfilled = new();

    public async Task<RagChunk?> GetParentChunkAsync(string parentId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM rag_chunks WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", parentId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapChunk(r) : null;
    }

    public async Task DeleteChunksForDatasetAsync(string datasetId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM rag_chunks WHERE dataset_id=$ds; DELETE FROM rag_chunks_fts WHERE dataset_id=$ds;";
        cmd.Parameters.AddWithValue("$ds", datasetId);
        await cmd.ExecuteNonQueryAsync(ct);
        _ftsBackfilled.TryRemove(datasetId, out _);
    }

    public async Task DeleteChunksForSourcesAsync(string datasetId, IEnumerable<string> sourcePaths, CancellationToken ct = default)
    {
        var paths = sourcePaths.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0) return;

        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        foreach (var path in paths)
        {
            var cmd = c.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText =
                "DELETE FROM rag_chunks_fts WHERE id IN (SELECT id FROM rag_chunks WHERE dataset_id=$ds AND (source_path=$path OR (source_path='' AND source_file=$file)));" +
                "DELETE FROM rag_chunks WHERE dataset_id=$ds AND (source_path=$path OR (source_path='' AND source_file=$file));";
            cmd.Parameters.AddWithValue("$ds", datasetId);
            cmd.Parameters.AddWithValue("$path", path);
            cmd.Parameters.AddWithValue("$file", Path.GetFileName(path));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<Dictionary<string,string>> GetSourceHashesAsync(string datasetId, IEnumerable<string> sourcePaths, CancellationToken ct = default)
    {
        var paths = sourcePaths.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var result = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        if (paths.Count == 0) return result;

        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);

        foreach (var path in paths)
        {
            var file = Path.GetFileName(path);
            var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT source_hash FROM rag_chunks WHERE dataset_id=$ds AND (source_path=$path OR (source_path='' AND source_file=$file)) LIMIT 1";
            cmd.Parameters.AddWithValue("$ds", datasetId);
            cmd.Parameters.AddWithValue("$path", path);
            cmd.Parameters.AddWithValue("$file", file);
            var val = await cmd.ExecuteScalarAsync(ct);
            if (val is string s && !string.IsNullOrWhiteSpace(s))
                result[path] = s;
        }

        return result;
    }

    // ── BM25 stats ────────────────────────────────────────────────────────────

    public async Task SaveBm25StatsAsync(string datasetId, Bm25Stats stats, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO rag_bm25_stats (dataset_id,stats_json,updated_at)
            VALUES ($ds,$json,$ua)
            ON CONFLICT(dataset_id) DO UPDATE SET stats_json=excluded.stats_json, updated_at=excluded.updated_at";
        cmd.Parameters.AddWithValue("$ds",   datasetId);
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(stats));
        cmd.Parameters.AddWithValue("$ua",   DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Bm25Stats?> GetBm25StatsAsync(string datasetId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT stats_json FROM rag_bm25_stats WHERE dataset_id=$ds";
        cmd.Parameters.AddWithValue("$ds", datasetId);
        var val = await cmd.ExecuteScalarAsync(ct);
        return val is string json ? JsonSerializer.Deserialize<Bm25Stats>(json) : null;
    }

    // ── Serialisation helpers ─────────────────────────────────────────────────

    internal static byte[] EmbeddingToBytes(float[] emb)
    {
        var bytes = new byte[emb.Length * sizeof(float)];
        Buffer.BlockCopy(emb, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    internal static float[] BytesToEmbedding(byte[] bytes)
    {
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    // ── Mappers ──────────────────────────────────────────────────────────────

    private static RagDataset MapDataset(SqliteDataReader r) => new()
    {
        Id             = r.GetString(0),
        Name           = r.GetString(1),
        Description    = r.GetString(2),
        ChunkCount     = r.GetInt32(3),
        CreatedAt      = DateTime.Parse(r.GetString(4)),
        Config         = JsonSerializer.Deserialize<RagDatasetConfig>(r.GetString(5)) ?? new(),
        LastIngestPath = GetString(r, "last_ingest_path"),
        LastIngestUtc  = TryParseDate(GetString(r, "last_ingest_utc")),
        ProjectId      = GetString(r, "project_id")
    };

    private static RagChunk MapChunk(SqliteDataReader r) => new()
    {
        Id          = r.GetString(0),
        DatasetId   = r.GetString(1),
        SourceFile  = r.GetString(2),
        SourcePath  = GetString(r, "source_path"),
        SourceHash  = GetString(r, "source_hash"),
        SourceModifiedUtc = TryParseDate(GetString(r, "source_modified_utc")),
        SourceTitle = GetString(r, "source_title"),
        Content     = GetString(r, "content"),
        ChunkIndex  = GetInt(r, "chunk_index"),
        ChunkTotal  = GetInt(r, "chunk_total"),
        ParentId    = r.IsDBNull(r.GetOrdinal("parent_id")) ? null : r.GetString(r.GetOrdinal("parent_id")),
        IsParent    = GetInt(r, "is_parent") != 0,
        TokenCount  = GetInt(r, "token_count"),
        Embedding   = r.IsDBNull(r.GetOrdinal("embedding")) ? [] : BytesToEmbedding((byte[])r.GetValue(r.GetOrdinal("embedding"))),
        CreatedAt   = DateTime.Parse(GetString(r, "created_at")),
        ChunkKind   = Enum.TryParse<RagChunkKind>(GetString(r, "chunk_kind"), out var ck) ? ck : RagChunkKind.PlainText,
        HeadingPath = GetNullableString(r, "heading_path"),
        CodeSymbolInfo = GetNullableString(r, "code_symbol_info"),
        PageNumber  = GetNullableInt(r, "page_number"),
        EventType   = GetNullableString(r, "event_type"),
        SourceUrl   = GetNullableString(r, "source_url")
    };

    private static string GetString(SqliteDataReader r, string name)
    {
        if (!TryGetOrdinal(r, name, out var ordinal))
            return string.Empty;

        return r.IsDBNull(ordinal) ? string.Empty : r.GetString(ordinal);
    }

    private static int GetInt(SqliteDataReader r, string name)
    {
        if (!TryGetOrdinal(r, name, out var ordinal))
            return 0;

        return r.IsDBNull(ordinal) ? 0 : r.GetInt32(ordinal);
    }

    private static string? GetNullableString(SqliteDataReader r, string name)
    {
        if (!TryGetOrdinal(r, name, out var ordinal))
            return null;

        return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
    }

    private static int? GetNullableInt(SqliteDataReader r, string name)
    {
        if (!TryGetOrdinal(r, name, out var ordinal))
            return null;

        return r.IsDBNull(ordinal) ? null : r.GetInt32(ordinal);
    }

    private static bool TryGetOrdinal(SqliteDataReader r, string name, out int ordinal)
    {
        try
        {
            ordinal = r.GetOrdinal(name);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            ordinal = -1;
            return false;
        }
    }

    private static DateTime? TryParseDate(string value) =>
        DateTime.TryParse(value, out var parsed) ? parsed : null;
}
