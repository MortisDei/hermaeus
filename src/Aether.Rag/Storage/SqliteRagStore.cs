using System.Text.Json;
using Aether.Core.Services;
using Aether.Rag.Models;
using Microsoft.Data.Sqlite;

namespace Aether.Rag.Storage;

/// <summary>
/// Stores RAG datasets, chunks, embeddings and BM25 stats in SQLite.
/// Shares the same DB file as the conversation store (same data dir).
/// Embeddings are stored as raw float32 BLOBs for fast deserialisation.
/// </summary>
public sealed class SqliteRagStore
{
    private readonly ISettingsService _settings;
    private string _initializedPath = string.Empty;
    private string DbPath
    {
        get
        {
            var dir = ResolveDataRoot();
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "conversations.db");
        }
    }
    private string Cs => $"Data Source={DbPath}";

    public SqliteRagStore(ISettingsService settings)
    {
        _settings = settings;
    }

    private string ResolveDataRoot()
    {
        var configured = _settings.Settings.DataRootDirectory?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aether");
    }

    // ── Init ─────────────────────────────────────────────────────────────────

    public async Task InitializeAsync() => await EnsureInitializedAsync();

    private async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        var dbPath = DbPath;
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
        await EnsureColumnAsync(c, "rag_chunks", "source_path", "TEXT NOT NULL DEFAULT ''", ct);
        await EnsureColumnAsync(c, "rag_chunks", "source_hash", "TEXT NOT NULL DEFAULT ''", ct);
        await EnsureColumnAsync(c, "rag_chunks", "source_modified_utc", "TEXT", ct);
        _initializedPath = dbPath;
    }

    private static async Task EnsureColumnAsync(
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
                    return;
            }
        }

        await using var alter = c.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        await alter.ExecuteNonQueryAsync(ct);
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
            INSERT INTO rag_datasets (id,name,description,chunk_count,created_at,config_json)
            VALUES ($id,$name,$desc,$cc,$ca,$cfg)
            ON CONFLICT(id) DO UPDATE SET
                name=excluded.name, description=excluded.description,
                chunk_count=excluded.chunk_count, config_json=excluded.config_json";
        cmd.Parameters.AddWithValue("$id",   ds.Id);
        cmd.Parameters.AddWithValue("$name", ds.Name);
        cmd.Parameters.AddWithValue("$desc", ds.Description);
        cmd.Parameters.AddWithValue("$cc",   ds.ChunkCount);
        cmd.Parameters.AddWithValue("$ca",   ds.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$cfg",  JsonSerializer.Serialize(ds.Config));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteDatasetAsync(string datasetId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM rag_datasets WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", datasetId);
        await cmd.ExecuteNonQueryAsync(ct);
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
                 parent_id,token_count,embedding,created_at)
            VALUES ($id,$ds,$sf,$sp,$sh,$sm,$st,$ct,$ci,$ctot,$pid,$tc,$emb,$ca)";

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
        var pTc   = cmd.Parameters.Add("$tc",   SqliteType.Integer);
        var pEmb  = cmd.Parameters.Add("$emb",  SqliteType.Blob);
        var pCa   = cmd.Parameters.Add("$ca",   SqliteType.Text);

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
            pTc.Value   = chunk.TokenCount;
            pEmb.Value  = EmbeddingToBytes(chunk.Embedding);
            pCa.Value   = chunk.CreatedAt.ToString("O");
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<List<RagChunk>> GetChunksAsync(string datasetId, bool includeEmbeddings = true, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = includeEmbeddings
            ? "SELECT * FROM rag_chunks WHERE dataset_id=$ds AND parent_id IS NULL ORDER BY source_file, chunk_index"
            : "SELECT id,dataset_id,source_file,source_path,source_hash,source_modified_utc,source_title,content,chunk_index,chunk_total,parent_id,token_count,NULL AS embedding,created_at FROM rag_chunks WHERE dataset_id=$ds AND parent_id IS NULL ORDER BY source_file, chunk_index";
        cmd.Parameters.AddWithValue("$ds", datasetId);
        var list = new List<RagChunk>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapChunk(r));
        return list;
    }

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
        cmd.CommandText = "DELETE FROM rag_chunks WHERE dataset_id=$ds";
        cmd.Parameters.AddWithValue("$ds", datasetId);
        await cmd.ExecuteNonQueryAsync(ct);
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
            cmd.CommandText = "DELETE FROM rag_chunks WHERE dataset_id=$ds AND (source_path=$path OR (source_path='' AND source_file=$file))";
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

    public async Task SaveRagQueryTraceAsync(RagQueryTrace trace, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO rag_query_traces
                (id,dataset_id,question,expanded_question,model_id,retrieval_latency_ms,total_latency_ms,
                 grounding_score,grounding_mode,retrieved_chunks_json,selected_context_json,created_at)
            VALUES
                ($id,$ds,$q,$eq,$model,$retrieval,$total,$grounding,$mode,$retrieved,$selected,$created)";
        cmd.Parameters.AddWithValue("$id", trace.Id);
        cmd.Parameters.AddWithValue("$ds", trace.DatasetId);
        cmd.Parameters.AddWithValue("$q", trace.Question);
        cmd.Parameters.AddWithValue("$eq", trace.ExpandedQuestion);
        cmd.Parameters.AddWithValue("$model", trace.ModelId);
        cmd.Parameters.AddWithValue("$retrieval", trace.RetrievalLatencyMs);
        cmd.Parameters.AddWithValue("$total", trace.TotalLatencyMs);
        cmd.Parameters.AddWithValue("$grounding", trace.GroundingScore);
        cmd.Parameters.AddWithValue("$mode", trace.GroundingMode.ToString());
        cmd.Parameters.AddWithValue("$retrieved", JsonSerializer.Serialize(trace.RetrievedChunks));
        cmd.Parameters.AddWithValue("$selected", JsonSerializer.Serialize(trace.SelectedContext));
        cmd.Parameters.AddWithValue("$created", trace.CreatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
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
        Id          = r.GetString(0),
        Name        = r.GetString(1),
        Description = r.GetString(2),
        ChunkCount  = r.GetInt32(3),
        CreatedAt   = DateTime.Parse(r.GetString(4)),
        Config      = JsonSerializer.Deserialize<RagDatasetConfig>(r.GetString(5)) ?? new()
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
        TokenCount  = GetInt(r, "token_count"),
        Embedding   = r.IsDBNull(r.GetOrdinal("embedding")) ? [] : BytesToEmbedding((byte[])r.GetValue(r.GetOrdinal("embedding"))),
        CreatedAt   = DateTime.Parse(GetString(r, "created_at"))
    };

    private static string GetString(SqliteDataReader r, string name)
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? string.Empty : r.GetString(ordinal);
    }

    private static int GetInt(SqliteDataReader r, string name)
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? 0 : r.GetInt32(ordinal);
    }

    private static DateTime? TryParseDate(string value) =>
        DateTime.TryParse(value, out var parsed) ? parsed : null;
}
