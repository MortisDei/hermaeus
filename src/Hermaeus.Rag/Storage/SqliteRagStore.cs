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
    private const int SchemaVersion = 4;
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
        var operationId = OperationCorrelation.NewId();
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
                config_json  TEXT NOT NULL DEFAULT '{}',
                current_generation_id TEXT
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
                generation_id TEXT NOT NULL DEFAULT '',
                source_id    TEXT NOT NULL DEFAULT '',
                source_revision_id TEXT NOT NULL DEFAULT '',
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

            CREATE TABLE IF NOT EXISTS rag_sources (
                source_id TEXT PRIMARY KEY,
                dataset_id TEXT NOT NULL,
                watch_root_id TEXT,
                relative_locator TEXT NOT NULL,
                kind TEXT NOT NULL,
                root_identity TEXT
            );

            CREATE TABLE IF NOT EXISTS rag_source_revisions (
                revision_id TEXT PRIMARY KEY,
                source_id TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                source_evidence TEXT NOT NULL,
                embedding_identity TEXT NOT NULL,
                state TEXT NOT NULL,
                created_at TEXT NOT NULL,
                previous_revision_id TEXT,
                source_modified_utc TEXT
            );

            CREATE TABLE IF NOT EXISTS rag_dataset_generations (
                generation_id TEXT PRIMARY KEY,
                dataset_id TEXT NOT NULL,
                embedding_identity TEXT NOT NULL,
                embedding_dimensions INTEGER NOT NULL,
                state TEXT NOT NULL,
                created_at TEXT NOT NULL,
                published_at TEXT,
                chunk_count INTEGER NOT NULL,
                previous_generation_id TEXT
            );

            CREATE TABLE IF NOT EXISTS rag_generation_bm25_stats (
                generation_id TEXT PRIMARY KEY,
                stats_json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_rag_source_revisions_source ON rag_source_revisions(source_id, created_at);
            CREATE INDEX IF NOT EXISTS idx_rag_generations_dataset ON rag_dataset_generations(dataset_id, created_at);

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
                    await EnsureColumnAsync(db, "rag_datasets", "project_id", "TEXT NOT NULL DEFAULT ''", token)),
                new SqliteMigration(3, async (db, token) =>
                {
                    var changed = false;
                    changed |= await EnsureColumnAsync(db, "rag_datasets", "current_generation_id", "TEXT", token);
                    changed |= await EnsureColumnAsync(db, "rag_chunks", "generation_id", "TEXT NOT NULL DEFAULT ''", token);
                    changed |= await EnsureColumnAsync(db, "rag_chunks", "source_id", "TEXT NOT NULL DEFAULT ''", token);
                    changed |= await EnsureColumnAsync(db, "rag_chunks", "source_revision_id", "TEXT NOT NULL DEFAULT ''", token);
                    await EnsureLineageTablesAsync(db, token);
                    return changed;
                }),
                new SqliteMigration(4, async (db, token) =>
                {
                    await ScavengeStagingRowsAsync(db, token);
                    return false;
                })
            ], ct);

            // r10 01-rag-correctness.md 1.2: DeleteDatasetAsync used to rely on
            // ON DELETE CASCADE, but no connection ever enabled foreign key
            // enforcement, so every deleted dataset left its chunks and BM25
            // stats behind forever. One-time sweep for rows already orphaned
            // by that bug; DeleteDatasetAsync now deletes explicitly so this
            // should find nothing on a healthy store going forward.
            await CleanupOrphanedRowsAsync(c, ct);

            _logs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Info,
                RuntimeLogCategory.Rag,
                $"RAG database opened with mode=read-write, pooling=enabled, foreign_keys=enabled, journal={await ReadJournalModeAsync(c, ct)}, schema_target={SchemaVersion}.",
                operationId));

            _initializedPath = dbPath;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Error,
                RuntimeLogCategory.Rag,
                $"RAG database initialization failed: exception={ex.GetType().Name}.",
                operationId));
            throw;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private static async Task<string> ReadJournalModeAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        return Convert.ToString(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown";
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

    private static async Task EnsureLineageTablesAsync(SqliteConnection c, CancellationToken ct)
    {
        await using var command = c.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS rag_sources (
                source_id TEXT PRIMARY KEY,
                dataset_id TEXT NOT NULL,
                watch_root_id TEXT,
                relative_locator TEXT NOT NULL,
                kind TEXT NOT NULL,
                root_identity TEXT
            );
            CREATE TABLE IF NOT EXISTS rag_source_revisions (
                revision_id TEXT PRIMARY KEY,
                source_id TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                source_evidence TEXT NOT NULL,
                embedding_identity TEXT NOT NULL,
                state TEXT NOT NULL,
                created_at TEXT NOT NULL,
                previous_revision_id TEXT,
                source_modified_utc TEXT
            );
            CREATE TABLE IF NOT EXISTS rag_dataset_generations (
                generation_id TEXT PRIMARY KEY,
                dataset_id TEXT NOT NULL,
                embedding_identity TEXT NOT NULL,
                embedding_dimensions INTEGER NOT NULL,
                state TEXT NOT NULL,
                created_at TEXT NOT NULL,
                published_at TEXT,
                chunk_count INTEGER NOT NULL,
                previous_generation_id TEXT
            );
            CREATE TABLE IF NOT EXISTS rag_generation_bm25_stats (
                generation_id TEXT PRIMARY KEY,
                stats_json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_rag_chunks_generation ON rag_chunks(dataset_id, generation_id, is_parent);
            CREATE INDEX IF NOT EXISTS idx_rag_source_revisions_source ON rag_source_revisions(source_id, created_at);
            CREATE INDEX IF NOT EXISTS idx_rag_generations_dataset ON rag_dataset_generations(dataset_id, created_at);";
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task ScavengeStagingRowsAsync(SqliteConnection c, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24).ToString("O");
        await using var command = c.CreateCommand();
        command.CommandText = @"
            DELETE FROM rag_chunks_fts
            WHERE id IN (
                SELECT c.id FROM rag_chunks c
                JOIN rag_dataset_generations g ON g.generation_id = c.generation_id
                WHERE g.state = 'Staged' AND g.created_at < $cutoff);
            DELETE FROM rag_chunks
            WHERE generation_id IN (
                SELECT generation_id FROM rag_dataset_generations
                WHERE state = 'Staged' AND created_at < $cutoff);
            DELETE FROM rag_generation_bm25_stats
            WHERE generation_id IN (
                SELECT generation_id FROM rag_dataset_generations
                WHERE state = 'Staged' AND created_at < $cutoff);
            DELETE FROM rag_dataset_generations
            WHERE state = 'Staged' AND created_at < $cutoff;
            DELETE FROM rag_source_revisions
            WHERE state = 'Staged' AND created_at < $cutoff
              AND revision_id NOT IN (SELECT source_revision_id FROM rag_chunks);";
        command.Parameters.AddWithValue("$cutoff", cutoff);
        await command.ExecuteNonQueryAsync(ct);
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

    private static async Task ExecuteSqlAsync(
        SqliteConnection c,
        SqliteTransaction tx,
        string sql,
        CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await using var command = c.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string?> ScalarStringAsync(
        SqliteConnection c,
        SqliteTransaction? tx,
        string sql,
        CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await using var command = c.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        var result = await command.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    private static async Task<List<RagSourceDescriptor>> LoadSourceDescriptorsAsync(
        SqliteConnection c, string datasetId, CancellationToken ct)
    {
        await using var command = c.CreateCommand();
        command.CommandText = @"
            SELECT source_id, dataset_id, watch_root_id, relative_locator, kind, root_identity
            FROM rag_sources WHERE dataset_id = $dataset ORDER BY source_id";
        command.Parameters.AddWithValue("$dataset", datasetId);
        var result = new List<RagSourceDescriptor>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var kind = Enum.TryParse<RagSourceKind>(reader.GetString(4), out var parsed)
                ? parsed : RagSourceKind.Legacy;
            result.Add(new RagSourceDescriptor(
                reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3), kind, reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return result;
    }

    private static async Task<List<RagSourceRevision>> LoadSourceRevisionsAsync(
        SqliteConnection c, CancellationToken ct)
    {
        await using var command = c.CreateCommand();
        command.CommandText = @"
            SELECT r.revision_id, r.source_id, r.content_hash, r.source_evidence,
                   r.embedding_identity, r.state, r.created_at, r.previous_revision_id,
                   r.source_modified_utc
            FROM rag_source_revisions r
            JOIN rag_sources s ON s.source_id = r.source_id
            ORDER BY r.created_at";
        var result = new List<RagSourceRevision>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var state = Enum.TryParse<RagSourceRevisionState>(reader.GetString(5), out var parsed)
                ? parsed : RagSourceRevisionState.Superseded;
            result.Add(new RagSourceRevision(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), state, DateTime.Parse(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8))));
        }

        return result;
    }

    private static List<RagChunk> CloneForGeneration(IReadOnlyList<RagChunk> chunks)
    {
        var ids = chunks.ToDictionary(chunk => chunk.Id, _ => Guid.NewGuid().ToString(), StringComparer.Ordinal);
        return chunks.Select(chunk => new RagChunk
        {
            Id = ids[chunk.Id],
            DatasetId = chunk.DatasetId,
            SourceFile = chunk.SourceFile,
            SourcePath = chunk.SourcePath,
            SourceHash = chunk.SourceHash,
            SourceId = chunk.SourceId,
            SourceRevisionId = chunk.SourceRevisionId,
            GenerationId = string.Empty,
            SourceModifiedUtc = chunk.SourceModifiedUtc,
            SourceTitle = chunk.SourceTitle,
            Content = chunk.Content,
            ChunkIndex = chunk.ChunkIndex,
            ChunkTotal = chunk.ChunkTotal,
            ParentId = chunk.ParentId is not null && ids.TryGetValue(chunk.ParentId, out var parentId) ? parentId : null,
            IsParent = chunk.IsParent,
            TokenCount = chunk.TokenCount,
            Embedding = [.. chunk.Embedding],
            CreatedAt = chunk.CreatedAt,
            ChunkKind = chunk.ChunkKind,
            HeadingPath = chunk.HeadingPath,
            CodeSymbolInfo = chunk.CodeSymbolInfo,
            PageNumber = chunk.PageNumber,
            EventType = chunk.EventType,
            SourceUrl = chunk.SourceUrl
        }).ToList();
    }

    private static string BuildCurrentChunkSelect(bool includeEmbeddings, bool includeParents)
    {
        var embedding = includeEmbeddings ? "c.embedding" : "NULL AS embedding";
        var parents = includeParents ? string.Empty : " AND c.is_parent = 0";
        return $@"
            SELECT c.id, c.dataset_id, c.source_file, c.source_path, c.source_hash,
                   c.source_modified_utc, c.source_title, c.content, c.chunk_index,
                   c.chunk_total, c.parent_id, c.generation_id, c.source_id,
                   c.source_revision_id, c.is_parent, c.token_count, {embedding},
                   c.created_at, c.chunk_kind, c.heading_path, c.code_symbol_info,
                   c.page_number, c.event_type, c.source_url
            FROM rag_chunks c
            JOIN rag_datasets d ON d.id = c.dataset_id
            WHERE c.dataset_id = $ds AND c.generation_id = d.current_generation_id{parents}
            ORDER BY c.source_file, c.chunk_index";
    }

    private static void ValidateGeneration(
        RagDataset dataset,
        IReadOnlyList<RagChunk> chunks,
        Bm25Stats stats,
        IReadOnlyList<RagSourceDescriptor> sources,
        IReadOnlyList<RagSourceRevision> revisions,
        string embeddingIdentity,
        int embeddingDimensions)
    {
        if (string.IsNullOrWhiteSpace(dataset.Id) || string.IsNullOrWhiteSpace(dataset.Name))
            throw new ArgumentException("A dataset id and name are required.", nameof(dataset));
        if (string.IsNullOrWhiteSpace(embeddingIdentity))
            throw new ArgumentException("Embedding identity is required.", nameof(embeddingIdentity));
        if (embeddingDimensions < 0)
            throw new ArgumentOutOfRangeException(nameof(embeddingDimensions));
        if (sources.Any(s => string.IsNullOrWhiteSpace(s.SourceId) || s.DatasetId != dataset.Id)
            || sources.Select(s => s.SourceId).Distinct(StringComparer.Ordinal).Count() != sources.Count)
            throw new ArgumentException("Source descriptors must be unique and belong to the dataset.", nameof(sources));

        var sourceIds = sources.Select(s => s.SourceId).ToHashSet(StringComparer.Ordinal);
        if (revisions.Any(r => string.IsNullOrWhiteSpace(r.RevisionId) || !sourceIds.Contains(r.SourceId))
            || revisions.Select(r => r.RevisionId).Distinct(StringComparer.Ordinal).Count() != revisions.Count)
            throw new ArgumentException("Source revisions must reference known unique sources.", nameof(revisions));
        var revisionIds = revisions.Select(r => r.RevisionId).ToHashSet(StringComparer.Ordinal);
        var chunkIds = chunks.Select(c => c.Id).ToList();
        if (chunkIds.Any(string.IsNullOrWhiteSpace)
            || chunkIds.Distinct(StringComparer.Ordinal).Count() != chunkIds.Count)
            throw new ArgumentException("Generation chunks must have unique ids.", nameof(chunks));
        if (chunks.Any(c => c.DatasetId != dataset.Id || !sourceIds.Contains(c.SourceId) || !revisionIds.Contains(c.SourceRevisionId)))
            throw new ArgumentException("Every chunk must belong to the dataset and reference its source lineage.", nameof(chunks));

        var parentIds = chunks.Where(c => c.IsParent).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        if (chunks.Any(c => c.ParentId is not null && !parentIds.Contains(c.ParentId)))
            throw new ArgumentException("Every child chunk must reference a parent in the same generation.", nameof(chunks));

        var revisionsById = revisions.ToDictionary(r => r.RevisionId, StringComparer.Ordinal);
        foreach (var chunk in chunks)
        {
            var revision = revisionsById[chunk.SourceRevisionId];
            if (!string.Equals(revision.SourceId, chunk.SourceId, StringComparison.Ordinal)
                || !string.Equals(revision.ContentHash, chunk.SourceHash, StringComparison.Ordinal))
            {
                throw new ArgumentException("Every chunk must match its source revision identity and content hash.", nameof(chunks));
            }
        }

        var embedded = chunks.Where(c => !c.IsParent).ToList();
        if (embedded.Any(c => c.Embedding.Length == 0
            || c.Embedding.Any(value => !float.IsFinite(value))))
            throw new ArgumentException("Every non-parent chunk requires one non-empty finite embedding.", nameof(chunks));
        var dimensions = embedded.Select(c => c.Embedding.Length).Distinct().ToList();
        if (dimensions.Count > 1 || dimensions.Count == 1 && dimensions[0] != embeddingDimensions)
            throw new ArgumentException("All non-parent embeddings must have the selected identical dimension.", nameof(chunks));
        if (embedded.Count == 0 && embeddingDimensions != 0)
            throw new ArgumentException("An empty generation cannot claim an embedding dimension.", nameof(embeddingDimensions));
        if (embedded.Count != 0 && embedded.Any(c => c.Embedding.Length != embeddingDimensions))
            throw new ArgumentException("Embedding cardinality or dimensions do not match the generation.", nameof(chunks));
        if (embedded.Count != stats.TotalDocuments)
            throw new ArgumentException("BM25 statistics must cover every non-parent chunk in the generation.", nameof(stats));

        var usedRevisionIds = chunks.Select(c => c.SourceRevisionId).ToHashSet(StringComparer.Ordinal);
        if (revisions.Any(revision => usedRevisionIds.Contains(revision.RevisionId)
                && !string.Equals(revision.EmbeddingIdentity, embeddingIdentity, StringComparison.Ordinal)))
        {
            throw new ArgumentException("Every source revision in the generation must use the selected embedding identity.", nameof(revisions));
        }
    }

    private static void NormalizeWatchedSources(RagDataset dataset)
    {
        foreach (var watched in dataset.Config.WatchedSources)
        {
            if (string.IsNullOrWhiteSpace(watched.WatchRootId))
                watched.WatchRootId = RagSourceIdentity.ForWatchedRoot(dataset.Id, watched.Root);
            var identity = RagSourceIdentity.TryGetRootIdentity(watched.Root);
            if (string.IsNullOrWhiteSpace(watched.LastConfirmedRootIdentity) && identity is not null)
                watched.LastConfirmedRootIdentity = identity;
        }
    }

    private async Task EnsureAllLegacyLineageAsync(SqliteConnection c, CancellationToken ct)
    {
        var ids = new List<string>();
        await using (var command = c.CreateCommand())
        {
            command.CommandText = "SELECT id FROM rag_datasets WHERE current_generation_id IS NULL";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) ids.Add(reader.GetString(0));
        }

        foreach (var id in ids)
            await EnsureDatasetLineageAsync(c, id, ct);
    }

    private async Task EnsureDatasetLineageAsync(SqliteConnection c, string datasetId, CancellationToken ct)
    {
        var current = await ScalarStringAsync(c, null,
            "SELECT current_generation_id FROM rag_datasets WHERE id = $id", ct, ("$id", datasetId));
        if (current is not null)
            return;

        var dataset = new RagDataset();
        await using (var command = c.CreateCommand())
        {
            command.CommandText = "SELECT name, description, chunk_count, created_at, config_json, last_ingest_path, last_ingest_utc, project_id FROM rag_datasets WHERE id = $id";
            command.Parameters.AddWithValue("$id", datasetId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return;
            dataset.Id = datasetId;
            dataset.Name = reader.GetString(0);
            dataset.Description = reader.GetString(1);
            dataset.ChunkCount = reader.GetInt32(2);
            dataset.CreatedAt = DateTime.Parse(reader.GetString(3));
            dataset.Config = JsonSerializer.Deserialize<RagDatasetConfig>(reader.GetString(4)) ?? new();
            dataset.LastIngestPath = reader.GetString(5);
            dataset.LastIngestUtc = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6));
            dataset.ProjectId = reader.GetString(7);
        }

        var rows = new List<(string Id, string SourcePath, string SourceFile, string Hash, string? Modified, bool IsParent)>();
        await using (var command = c.CreateCommand())
        {
            command.CommandText = "SELECT id, source_path, source_file, source_hash, source_modified_utc, is_parent FROM rag_chunks WHERE dataset_id = $id";
            command.Parameters.AddWithValue("$id", datasetId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt32(5) != 0));
            }
        }

        var created = dataset.CreatedAt;
        var generationId = $"legacy-generation:{HashIdentity(datasetId)}";
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        await ExecuteSqlAsync(c, tx, @"
            INSERT OR IGNORE INTO rag_dataset_generations
                (generation_id, dataset_id, embedding_identity, embedding_dimensions, state, created_at, published_at, chunk_count, previous_generation_id)
            VALUES ($generation, $dataset, $embedding, $dimensions, 'Current', $created, $published, $count, NULL)", ct,
            ("$generation", generationId), ("$dataset", datasetId),
            ("$embedding", string.IsNullOrWhiteSpace(dataset.Config.EmbeddingModel) ? "Unknown" : dataset.Config.EmbeddingModel),
            ("$dimensions", Math.Max(0, dataset.Config.EmbeddingDimensions)), ("$created", created.ToString("O")),
            ("$published", created.ToString("O")), ("$count", rows.Count(r => !r.IsParent)));

        var sourceIds = new Dictionary<string, (string SourceId, string RevisionId, string Hash, string? Modified)>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var sourcePath = string.IsNullOrWhiteSpace(row.SourcePath) ? row.SourceFile : row.SourcePath;
            if (!sourceIds.TryGetValue(sourcePath, out var lineage))
            {
                var identity = RagSourceIdentity.ForSource(datasetId, null, sourcePath);
                lineage = ($"legacy-source:{HashIdentity(identity)}", $"legacy-revision:{HashIdentity(identity)}", row.Hash, row.Modified);
                sourceIds[sourcePath] = lineage;
                await ExecuteSqlAsync(c, tx, @"
                    INSERT OR IGNORE INTO rag_sources
                        (source_id, dataset_id, watch_root_id, relative_locator, kind, root_identity)
                    VALUES ($source, $dataset, NULL, $locator, 'Legacy', NULL)", ct,
                    ("$source", lineage.SourceId), ("$dataset", datasetId), ("$locator", sourcePath));
                await ExecuteSqlAsync(c, tx, @"
                    INSERT OR IGNORE INTO rag_source_revisions
                        (revision_id, source_id, content_hash, source_evidence, embedding_identity, state, created_at, previous_revision_id, source_modified_utc)
                    VALUES ($revision, $source, $hash, 'Legacy source identity is Unknown.', $embedding, 'Current', $created, NULL, $modified)", ct,
                    ("$revision", lineage.RevisionId), ("$source", lineage.SourceId), ("$hash", lineage.Hash),
                    ("$embedding", string.IsNullOrWhiteSpace(dataset.Config.EmbeddingModel) ? "Unknown" : dataset.Config.EmbeddingModel),
                    ("$created", created.ToString("O")), ("$modified", (object?)lineage.Modified ?? DBNull.Value));
            }

            await ExecuteSqlAsync(c, tx,
                "UPDATE rag_chunks SET generation_id = $generation, source_id = $source, source_revision_id = $revision WHERE id = $id",
                ct, ("$generation", generationId), ("$source", lineage.SourceId),
                ("$revision", lineage.RevisionId), ("$id", row.Id));
        }

        await ExecuteSqlAsync(c, tx,
            "UPDATE rag_datasets SET current_generation_id = $generation WHERE id = $dataset AND current_generation_id IS NULL",
            ct, ("$generation", generationId), ("$dataset", datasetId));
        await tx.CommitAsync(ct);
    }

    private static string HashIdentity(string value) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..32];

    // ── Datasets ─────────────────────────────────────────────────────────────

    public async Task<List<RagDataset>> GetDatasetsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        await EnsureAllLegacyLineageAsync(c, ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM rag_datasets ORDER BY name";
        var list = new List<RagDataset>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(MapDataset(r));
        return list;
    }

    public async Task SaveDatasetAsync(RagDataset ds, CancellationToken ct = default)
    {
        NormalizeWatchedSources(ds);
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO rag_datasets (id,name,description,chunk_count,created_at,config_json,last_ingest_path,last_ingest_utc,project_id,current_generation_id)
            VALUES ($id,$name,$desc,$cc,$ca,$cfg,$lip,$liu,$pid,$generation)
            ON CONFLICT(id) DO UPDATE SET
                name=excluded.name, description=excluded.description,
                chunk_count=excluded.chunk_count, config_json=excluded.config_json,
                last_ingest_path=excluded.last_ingest_path, last_ingest_utc=excluded.last_ingest_utc,
                project_id=excluded.project_id,
                current_generation_id=COALESCE(excluded.current_generation_id, rag_datasets.current_generation_id)";
        cmd.Parameters.AddWithValue("$id",   ds.Id);
        cmd.Parameters.AddWithValue("$name", ds.Name);
        cmd.Parameters.AddWithValue("$desc", ds.Description);
        cmd.Parameters.AddWithValue("$cc",   ds.ChunkCount);
        cmd.Parameters.AddWithValue("$ca",   ds.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$cfg",  JsonSerializer.Serialize(ds.Config));
        cmd.Parameters.AddWithValue("$lip",  ds.LastIngestPath);
        cmd.Parameters.AddWithValue("$liu",  (object?)ds.LastIngestUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pid",  ds.ProjectId.Trim());
        cmd.Parameters.AddWithValue("$generation", (object?)ds.CurrentGenerationId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Publishes one complete dataset generation. All staged rows and the
    /// current pointer are committed together, so a failed or cancelled
    /// publication leaves the prior generation query-visible.
    /// </summary>
    public async Task<RagDatasetGeneration> PublishGenerationAsync(
        RagDataset dataset,
        IReadOnlyList<RagChunk> chunks,
        Bm25Stats stats,
        IReadOnlyList<RagSourceDescriptor> sources,
        IReadOnlyList<RagSourceRevision> revisions,
        string embeddingIdentity,
        int embeddingDimensions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(revisions);

        ValidateGeneration(dataset, chunks, stats, sources, revisions, embeddingIdentity, embeddingDimensions);
        NormalizeWatchedSources(dataset);
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureDatasetLineageAsync(c, dataset.Id, ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);

        var created = DateTime.UtcNow;
        var generationId = $"generation:{Guid.NewGuid():N}";
        await ExecuteSqlAsync(c, tx, @"
            INSERT OR IGNORE INTO rag_datasets
                (id, name, description, chunk_count, created_at, config_json, last_ingest_path, last_ingest_utc, project_id, current_generation_id)
            VALUES ($id, $name, $description, 0, $created, $config, $path, $ingest, $project, NULL)", ct,
            ("$id", dataset.Id), ("$name", dataset.Name), ("$description", dataset.Description),
            ("$created", dataset.CreatedAt.ToString("O")), ("$config", JsonSerializer.Serialize(dataset.Config)),
            ("$path", dataset.LastIngestPath), ("$ingest", (object?)dataset.LastIngestUtc?.ToString("O") ?? DBNull.Value),
            ("$project", dataset.ProjectId.Trim()));
        var previousGenerationId = await ScalarStringAsync(c, tx,
            "SELECT current_generation_id FROM rag_datasets WHERE id = $id", ct, ("$id", dataset.Id));
        await ExecuteSqlAsync(c, tx, @"
            INSERT INTO rag_dataset_generations
                (generation_id, dataset_id, embedding_identity, embedding_dimensions, state, created_at, published_at, chunk_count, previous_generation_id)
            VALUES ($generation, $dataset, $embedding, $dimensions, 'Staged', $created, NULL, $count, $previous)", ct,
            ("$generation", generationId), ("$dataset", dataset.Id), ("$embedding", embeddingIdentity),
            ("$dimensions", embeddingDimensions), ("$created", created.ToString("O")),
            ("$count", chunks.Count(c => !c.IsParent)), ("$previous", (object?)previousGenerationId ?? DBNull.Value));

        foreach (var source in sources)
        {
            await ExecuteSqlAsync(c, tx, @"
                INSERT INTO rag_sources (source_id, dataset_id, watch_root_id, relative_locator, kind, root_identity)
                VALUES ($source, $dataset, $watch, $locator, $kind, $identity)
                ON CONFLICT(source_id) DO UPDATE SET
                    dataset_id = excluded.dataset_id,
                    watch_root_id = excluded.watch_root_id,
                    relative_locator = excluded.relative_locator,
                    kind = excluded.kind,
                    root_identity = excluded.root_identity", ct,
                ("$source", source.SourceId), ("$dataset", source.DatasetId),
                ("$watch", (object?)source.WatchRootId ?? DBNull.Value),
                ("$locator", source.RelativeLocator), ("$kind", source.Kind.ToString()),
                ("$identity", (object?)source.RootIdentity ?? DBNull.Value));
        }

        foreach (var revision in revisions)
        {
            await ExecuteSqlAsync(c, tx, @"
                INSERT INTO rag_source_revisions
                    (revision_id, source_id, content_hash, source_evidence, embedding_identity, state, created_at, previous_revision_id, source_modified_utc)
                VALUES ($revision, $source, $hash, $evidence, $embedding, 'Staged', $created, $previous, $modified)
                ON CONFLICT(revision_id) DO UPDATE SET
                    content_hash = excluded.content_hash,
                    source_evidence = excluded.source_evidence,
                    embedding_identity = excluded.embedding_identity,
                    source_modified_utc = excluded.source_modified_utc", ct,
                ("$revision", revision.RevisionId), ("$source", revision.SourceId),
                ("$hash", revision.ContentHash), ("$evidence", revision.SourceEvidence),
                ("$embedding", revision.EmbeddingIdentity), ("$created", revision.CreatedAtUtc.ToString("O")),
                ("$previous", (object?)revision.PreviousRevisionId ?? DBNull.Value),
                ("$modified", (object?)revision.SourceModifiedUtc?.ToString("O") ?? DBNull.Value));
        }

        foreach (var chunk in chunks)
        {
            await ExecuteSqlAsync(c, tx, @"
                INSERT INTO rag_chunks
                    (id, dataset_id, source_file, source_path, source_hash, source_modified_utc, source_title, content,
                     chunk_index, chunk_total, parent_id, generation_id, source_id, source_revision_id, is_parent,
                     token_count, embedding, created_at, chunk_kind, heading_path, code_symbol_info, page_number, event_type, source_url)
                VALUES ($id, $dataset, $file, $path, $hash, $modified, $title, $content,
                        $index, $total, $parent, $generation, $source, $revision, $isParent,
                        $tokens, $embedding, $created, $kind, $heading, $symbol, $page, $event, $url)", ct,
                ("$id", chunk.Id), ("$dataset", dataset.Id), ("$file", chunk.SourceFile),
                ("$path", chunk.SourcePath), ("$hash", chunk.SourceHash),
                ("$modified", (object?)chunk.SourceModifiedUtc?.ToString("O") ?? DBNull.Value),
                ("$title", chunk.SourceTitle), ("$content", chunk.Content), ("$index", chunk.ChunkIndex),
                ("$total", chunk.ChunkTotal), ("$parent", (object?)chunk.ParentId ?? DBNull.Value),
                ("$generation", generationId), ("$source", chunk.SourceId), ("$revision", chunk.SourceRevisionId),
                ("$isParent", chunk.IsParent ? 1 : 0), ("$tokens", chunk.TokenCount),
                ("$embedding", EmbeddingToBytes(chunk.Embedding)), ("$created", chunk.CreatedAt.ToString("O")),
                ("$kind", chunk.ChunkKind.ToString()), ("$heading", (object?)chunk.HeadingPath ?? DBNull.Value),
                ("$symbol", (object?)chunk.CodeSymbolInfo ?? DBNull.Value), ("$page", (object?)chunk.PageNumber ?? DBNull.Value),
                ("$event", (object?)chunk.EventType ?? DBNull.Value), ("$url", (object?)chunk.SourceUrl ?? DBNull.Value));

            if (!chunk.IsParent)
            {
                await ExecuteSqlAsync(c, tx,
                    "INSERT INTO rag_chunks_fts (id, dataset_id, content) VALUES ($id, $dataset, $content)", ct,
                    ("$id", chunk.Id), ("$dataset", dataset.Id), ("$content", chunk.Content));
            }
        }

        await ExecuteSqlAsync(c, tx,
            "INSERT INTO rag_generation_bm25_stats (generation_id, stats_json, updated_at) VALUES ($generation, $stats, $updated)", ct,
            ("$generation", generationId), ("$stats", JsonSerializer.Serialize(stats)), ("$updated", created.ToString("O")));

        if (previousGenerationId is not null)
        {
            await ExecuteSqlAsync(c, tx,
                "UPDATE rag_dataset_generations SET state = 'Superseded' WHERE generation_id = $generation", ct,
                ("$generation", previousGenerationId));
        }

        foreach (var source in sources)
        {
            await ExecuteSqlAsync(c, tx, @"
                UPDATE rag_source_revisions SET state = 'Superseded'
                WHERE source_id = $source AND revision_id NOT IN (
                    SELECT source_revision_id FROM rag_chunks WHERE generation_id = $generation)", ct,
                ("$source", source.SourceId), ("$generation", generationId));
        }

        await ExecuteSqlAsync(c, tx, @"
            UPDATE rag_source_revisions SET state = 'Current'
            WHERE revision_id IN (SELECT source_revision_id FROM rag_chunks WHERE generation_id = $generation)", ct,
            ("$generation", generationId));
        await ExecuteSqlAsync(c, tx, @"
            UPDATE rag_dataset_generations
            SET state = 'Current', published_at = $published
            WHERE generation_id = $generation", ct,
            ("$generation", generationId), ("$published", DateTime.UtcNow.ToString("O")));

        await ExecuteSqlAsync(c, tx, @"
            INSERT INTO rag_datasets
                (id, name, description, chunk_count, created_at, config_json, last_ingest_path, last_ingest_utc, project_id, current_generation_id)
            VALUES ($id, $name, $description, $count, $created, $config, $path, $ingest, $project, $generation)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name, description = excluded.description, chunk_count = excluded.chunk_count,
                config_json = excluded.config_json, last_ingest_path = excluded.last_ingest_path,
                last_ingest_utc = excluded.last_ingest_utc, project_id = excluded.project_id,
                current_generation_id = excluded.current_generation_id", ct,
            ("$id", dataset.Id), ("$name", dataset.Name), ("$description", dataset.Description),
            ("$count", chunks.Count(c => !c.IsParent)), ("$created", dataset.CreatedAt.ToString("O")),
            ("$config", JsonSerializer.Serialize(dataset.Config)), ("$path", dataset.LastIngestPath),
            ("$ingest", (object?)dataset.LastIngestUtc?.ToString("O") ?? DBNull.Value),
            ("$project", dataset.ProjectId.Trim()), ("$generation", generationId));

        await ExecuteSqlAsync(c, tx, @"
            INSERT INTO rag_bm25_stats (dataset_id, stats_json, updated_at) VALUES ($dataset, $stats, $updated)
            ON CONFLICT(dataset_id) DO UPDATE SET stats_json = excluded.stats_json, updated_at = excluded.updated_at", ct,
            ("$dataset", dataset.Id), ("$stats", JsonSerializer.Serialize(stats)), ("$updated", created.ToString("O")));

        await tx.CommitAsync(ct);
        dataset.CurrentGenerationId = generationId;
        dataset.ChunkCount = chunks.Count(c => !c.IsParent);
        return new RagDatasetGeneration(generationId, dataset.Id, embeddingIdentity, embeddingDimensions,
            RagDatasetGenerationState.Current, created, DateTime.UtcNow, dataset.ChunkCount, previousGenerationId);
    }

    public async Task<List<RagChunk>> GetStoredChunksAsync(
        string datasetId, bool includeEmbeddings = true, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureDatasetLineageAsync(c, datasetId, ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = BuildCurrentChunkSelect(includeEmbeddings, includeParents: true);
        cmd.Parameters.AddWithValue("$ds", datasetId);
        var list = new List<RagChunk>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapChunk(reader));
        return list;
    }

    public async Task<List<RagDatasetGeneration>> GetGenerationHistoryAsync(
        string datasetId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureDatasetLineageAsync(c, datasetId, ct);
        await using var command = c.CreateCommand();
        command.CommandText = @"
            SELECT generation_id, dataset_id, embedding_identity, embedding_dimensions,
                   state, created_at, published_at, chunk_count, previous_generation_id
            FROM rag_dataset_generations
            WHERE dataset_id = $dataset
            ORDER BY created_at DESC";
        command.Parameters.AddWithValue("$dataset", datasetId);
        var result = new List<RagDatasetGeneration>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(MapGeneration(reader));
        return result;
    }

    public async Task DeleteDatasetAsync(string datasetId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);

        foreach (var table in new[] { "rag_generation_bm25_stats", "rag_dataset_generations", "rag_source_revisions", "rag_sources" })
        {
            await using var lineageCmd = c.CreateCommand();
            lineageCmd.Transaction = (SqliteTransaction)tx;
            lineageCmd.CommandText = table switch
            {
                "rag_generation_bm25_stats" => "DELETE FROM rag_generation_bm25_stats WHERE generation_id IN (SELECT generation_id FROM rag_dataset_generations WHERE dataset_id = $id)",
                "rag_dataset_generations" => "DELETE FROM rag_dataset_generations WHERE dataset_id = $id",
                "rag_source_revisions" => "DELETE FROM rag_source_revisions WHERE source_id IN (SELECT source_id FROM rag_sources WHERE dataset_id = $id)",
                _ => "DELETE FROM rag_sources WHERE dataset_id = $id"
            };
            lineageCmd.Parameters.AddWithValue("$id", datasetId);
            await lineageCmd.ExecuteNonQueryAsync(ct);
        }

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
        await EnsureDatasetLineageAsync(c, datasetId, ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = BuildCurrentChunkSelect(includeEmbeddings, includeParents: false);
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
        await EnsureDatasetLineageAsync(c, datasetId, ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            SELECT c.source_path, c.chunk_index, c.source_modified_utc
            FROM rag_chunks c
            JOIN rag_datasets d ON d.id = c.dataset_id
            WHERE c.dataset_id=$ds AND c.generation_id = d.current_generation_id AND c.is_parent = 0";
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
        await EnsureDatasetLineageAsync(c, datasetId, ct);
        var generationId = await ScalarStringAsync(c, null,
            "SELECT current_generation_id FROM rag_datasets WHERE id = $id", ct, ("$id", datasetId)) ?? string.Empty;
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            SELECT c.id, c.embedding
            FROM rag_chunks c
            JOIN rag_datasets d ON d.id = c.dataset_id
            WHERE c.dataset_id=$ds AND c.generation_id = d.current_generation_id AND c.is_parent = 0
            ORDER BY c.source_file, c.chunk_index";
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

        return BuildScanIndex(ids, vectors, embeddingModel, generationId);
    }

    private static RagScanIndex BuildScanIndex(
        List<string> ids, List<float[]> vectors, string embeddingModel, string generationId)
    {
        if (vectors.Count == 0)
            return new RagScanIndex([], [], 0, embeddingModel, generationId);

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

        return new RagScanIndex([.. keptIds], block, dimension, embeddingModel, generationId);
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
        await EnsureAllLegacyLineageAsync(c, ct);
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
            "SELECT c.id,c.dataset_id,c.source_file,c.source_path,c.source_hash,c.source_modified_utc,c.source_title,c.content,c.chunk_index,c.chunk_total," +
            "c.parent_id,c.generation_id,c.source_id,c.source_revision_id,c.is_parent,c.token_count,NULL AS embedding,c.created_at,c.chunk_kind,c.heading_path,c.code_symbol_info,c.page_number,c.event_type,c.source_url " +
            $"FROM rag_chunks c JOIN rag_datasets d ON d.id = c.dataset_id WHERE c.id IN ({string.Join(",", names)}) AND c.generation_id = d.current_generation_id";

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
        await using (var lineageConnection = new SqliteConnection(Cs))
        {
            await lineageConnection.OpenAsync(ct);
            await EnsureDatasetLineageAsync(lineageConnection, datasetId, ct);
        }
        await EnsureFtsBackfilledAsync(datasetId, ct);

        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            SELECT f.id
            FROM rag_chunks_fts f
            JOIN rag_chunks c ON c.id = f.id
            JOIN rag_datasets d ON d.id = c.dataset_id
            WHERE f.dataset_id = $ds AND c.generation_id = d.current_generation_id
              AND c.is_parent = 0 AND rag_chunks_fts MATCH $q LIMIT $limit";
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
        cmd.CommandText = @"
            SELECT c.id FROM rag_chunks c
            JOIN rag_datasets d ON d.id = c.dataset_id
            WHERE c.dataset_id = $ds AND c.generation_id = d.current_generation_id
              AND c.is_parent = 0 AND c.content LIKE $like LIMIT $limit";
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
                "SELECT (SELECT COUNT(*) FROM rag_chunks c JOIN rag_datasets d ON d.id = c.dataset_id " +
                "WHERE c.dataset_id=$ds AND c.generation_id = d.current_generation_id AND c.is_parent = 0) - " +
                "(SELECT COUNT(*) FROM rag_chunks_fts f JOIN rag_chunks c ON c.id = f.id JOIN rag_datasets d ON d.id = c.dataset_id " +
                "WHERE f.dataset_id=$ds AND c.generation_id = d.current_generation_id AND c.is_parent = 0)";
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
                "SELECT c.id, c.dataset_id, c.content FROM rag_chunks c JOIN rag_datasets d ON d.id = c.dataset_id " +
                "WHERE c.dataset_id = $ds AND c.generation_id = d.current_generation_id AND c.is_parent = 0;";
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
        await EnsureAllLegacyLineageAsync(c, ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            SELECT c.id, c.dataset_id, c.source_file, c.source_path, c.source_hash,
                   c.source_modified_utc, c.source_title, c.content, c.chunk_index,
                   c.chunk_total, c.parent_id, c.generation_id, c.source_id,
                   c.source_revision_id, c.is_parent, c.token_count, c.embedding,
                   c.created_at, c.chunk_kind, c.heading_path, c.code_symbol_info,
                   c.page_number, c.event_type, c.source_url
            FROM rag_chunks c JOIN rag_datasets d ON d.id = c.dataset_id
            WHERE c.id=$id AND c.generation_id = d.current_generation_id";
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
        await RemoveSourcesByPublishingGenerationAsync(datasetId, sourcePaths, ct);
    }

    /// <summary>
    /// Removes sources by publishing a new complete generation. The previous
    /// generation remains query-visible until the new pointer commits.
    /// </summary>
    public async Task<int> RemoveSourcesByPublishingGenerationAsync(
        string datasetId, IEnumerable<string> sourcePaths, CancellationToken ct = default)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var paths = sourcePaths.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct(comparer).ToHashSet(comparer);
        if (paths.Count == 0)
            return (await GetChunksAsync(datasetId, includeEmbeddings: false, ct)).Count;

        var existing = await GetStoredChunksAsync(datasetId, includeEmbeddings: true, ct);
        var removed = existing.Where(chunk => paths.Contains(chunk.SourcePath)
            || (string.IsNullOrWhiteSpace(chunk.SourcePath) && paths.Contains(chunk.SourceFile))).ToList();
        if (removed.Count == 0)
            return existing.Count(chunk => !chunk.IsParent);

        var dataset = (await GetDatasetsAsync(ct)).FirstOrDefault(item => item.Id == datasetId)
            ?? throw new InvalidOperationException($"RAG dataset '{datasetId}' was not found.");
        var retained = existing.Except(removed).ToList();
        var cloned = CloneForGeneration(retained);
        var embedded = cloned.Where(chunk => !chunk.IsParent).ToList();
        var stats = Bm25Scorer.BuildStats(embedded);
        var embeddingDimensions = embedded.Count == 0 ? 0 : embedded[0].Embedding.Length;
        dataset.Config.EmbeddingDimensions = embeddingDimensions;

        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureDatasetLineageAsync(c, datasetId, ct);
        var sources = await LoadSourceDescriptorsAsync(c, datasetId, ct);
        var revisions = await LoadSourceRevisionsAsync(c, ct);
        await PublishGenerationAsync(dataset, cloned, stats, sources, revisions,
            string.IsNullOrWhiteSpace(dataset.Config.EmbeddingModel) ? "Unknown" : dataset.Config.EmbeddingModel.Trim(),
            embeddingDimensions, ct);
        return cloned.Count(chunk => !chunk.IsParent);
    }

    public async Task<Dictionary<string,string>> GetSourceHashesAsync(string datasetId, IEnumerable<string> sourcePaths, CancellationToken ct = default)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var paths = sourcePaths.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct(comparer).ToList();
        var result = new Dictionary<string,string>(comparer);
        if (paths.Count == 0) return result;

        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        await EnsureDatasetLineageAsync(c, datasetId, ct);

        foreach (var path in paths)
        {
            var file = Path.GetFileName(path);
            var cmd = c.CreateCommand();
            cmd.CommandText = @"
                SELECT c.source_hash FROM rag_chunks c
                JOIN rag_datasets d ON d.id = c.dataset_id
                WHERE c.dataset_id=$ds AND c.generation_id = d.current_generation_id
                  AND (c.source_path=$path OR (c.source_path='' AND c.source_file=$file)) LIMIT 1";
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
        await EnsureDatasetLineageAsync(c, datasetId, ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            SELECT s.stats_json
            FROM rag_generation_bm25_stats s
            JOIN rag_datasets d ON d.current_generation_id = s.generation_id
            WHERE d.id = $ds
            UNION ALL
            SELECT legacy.stats_json FROM rag_bm25_stats legacy
            WHERE legacy.dataset_id = $ds
            LIMIT 1";
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
        ProjectId      = GetString(r, "project_id"),
        CurrentGenerationId = GetNullableString(r, "current_generation_id")
    };

    private static RagChunk MapChunk(SqliteDataReader r) => new()
    {
        Id          = r.GetString(0),
        DatasetId   = r.GetString(1),
        SourceFile  = r.GetString(2),
        SourcePath  = GetString(r, "source_path"),
        SourceHash  = GetString(r, "source_hash"),
        SourceId    = GetString(r, "source_id"),
        SourceRevisionId = GetString(r, "source_revision_id"),
        GenerationId = GetString(r, "generation_id"),
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

    private static RagDatasetGeneration MapGeneration(SqliteDataReader r)
    {
        var state = Enum.TryParse<RagDatasetGenerationState>(r.GetString(4), out var parsed)
            ? parsed : RagDatasetGenerationState.Superseded;
        return new RagDatasetGeneration(
            r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), state,
            DateTime.Parse(r.GetString(5)), r.IsDBNull(6) ? null : DateTime.Parse(r.GetString(6)),
            r.GetInt32(7), r.IsDBNull(8) ? null : r.GetString(8));
    }

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
