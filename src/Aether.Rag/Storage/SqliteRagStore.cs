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
    private readonly string _db;
    private string Cs => $"Data Source={_db}";

    public SqliteRagStore(ISettingsService _)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether");
        Directory.CreateDirectory(dir);
        _db = Path.Combine(dir, "conversations.db");
    }

    // ── Init ─────────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync();
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

            CREATE TABLE IF NOT EXISTS rag_bm25_stats (
                dataset_id TEXT PRIMARY KEY REFERENCES rag_datasets(id) ON DELETE CASCADE,
                stats_json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );";
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Datasets ─────────────────────────────────────────────────────────────

    public async Task<List<RagDataset>> GetDatasetsAsync(CancellationToken ct = default)
    {
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
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM rag_datasets WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", datasetId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Chunks ────────────────────────────────────────────────────────────────

    public async Task SaveChunksBatchAsync(IEnumerable<RagChunk> chunks, CancellationToken ct = default)
    {
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var cmd = c.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = @"
            INSERT OR REPLACE INTO rag_chunks
                (id,dataset_id,source_file,source_title,content,chunk_index,chunk_total,
                 parent_id,token_count,embedding,created_at)
            VALUES ($id,$ds,$sf,$st,$ct,$ci,$ctot,$pid,$tc,$emb,$ca)";

        var pId   = cmd.Parameters.Add("$id",   SqliteType.Text);
        var pDs   = cmd.Parameters.Add("$ds",   SqliteType.Text);
        var pSf   = cmd.Parameters.Add("$sf",   SqliteType.Text);
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
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = includeEmbeddings
            ? "SELECT * FROM rag_chunks WHERE dataset_id=$ds AND parent_id IS NULL ORDER BY source_file, chunk_index"
            : "SELECT id,dataset_id,source_file,source_title,content,chunk_index,chunk_total,parent_id,token_count,NULL,created_at FROM rag_chunks WHERE dataset_id=$ds AND parent_id IS NULL ORDER BY source_file, chunk_index";
        cmd.Parameters.AddWithValue("$ds", datasetId);
        var list = new List<RagChunk>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapChunk(r));
        return list;
    }

    public async Task<RagChunk?> GetParentChunkAsync(string parentId, CancellationToken ct = default)
    {
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM rag_chunks WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", parentId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapChunk(r) : null;
    }

    public async Task DeleteChunksForDatasetAsync(string datasetId, CancellationToken ct = default)
    {
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM rag_chunks WHERE dataset_id=$ds";
        cmd.Parameters.AddWithValue("$ds", datasetId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── BM25 stats ────────────────────────────────────────────────────────────

    public async Task SaveBm25StatsAsync(string datasetId, Bm25Stats stats, CancellationToken ct = default)
    {
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
        SourceTitle = r.GetString(3),
        Content     = r.GetString(4),
        ChunkIndex  = r.GetInt32(5),
        ChunkTotal  = r.GetInt32(6),
        ParentId    = r.IsDBNull(7) ? null : r.GetString(7),
        TokenCount  = r.GetInt32(8),
        Embedding   = r.IsDBNull(9) ? [] : BytesToEmbedding((byte[])r.GetValue(9)),
        CreatedAt   = DateTime.Parse(r.GetString(10))
    };
}
