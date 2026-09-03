using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Embeddings;
using Microsoft.Data.Sqlite;

namespace Hermaeus.Services;

/// <summary>
/// SQLite-based implementation of memory persistence.
/// </summary>
public sealed class MemoryStore : IMemoryStore, IKnowledgeRevisionStore
{
    // A hybrid scan currently scores every embedded row so paraphrases can be
    // found. It must still refuse weak semantic matches before they reach the
    // prompt, otherwise an unrelated history row becomes context merely
    // because it has an embedding.
    private const double MinimumRecallRelevance = 0.40;
    private const int SchemaVersion = 6;
    private const int KnowledgeSchemaVersion = 2;
    private const int MaxBackfillAttemptsPerRow = 5;
    /// <summary>
    /// r9 01-send-path-latency.md 1.3: how long the query and save paths wait
    /// on the embedder before falling back, rather than inheriting the HTTP
    /// client's 60 s timeout.
    ///
    /// r29 doc 04 4.5: injectable, because the two tests that prove the fallback
    /// happens proved it by waiting out the real three seconds, on both CI legs,
    /// every run. Production behaviour is unchanged: the default is this value.
    /// </summary>
    private static readonly TimeSpan DefaultQueryEmbedTimeout = TimeSpan.FromSeconds(3);

    private readonly ISettingsService _settings;
    private readonly IEmbeddingService? _embeddings;
    private readonly IRuntimeLogService? _runtimeLogs;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _backfillCooldown;
    private readonly TimeSpan _queryEmbedTimeout;
    private readonly IResourceCoordinator? _resourceCoordinator;
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
        TimeSpan? backfillCooldown = null,
        TimeSpan? queryEmbedTimeout = null,
        IResourceCoordinator? resourceCoordinator = null)
    {
        _settings = settings;
        _embeddings = embeddings;
        _runtimeLogs = runtimeLogs;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _backfillCooldown = backfillCooldown ?? TimeSpan.FromMinutes(10);
        _queryEmbedTimeout = queryEmbedTimeout ?? DefaultQueryEmbedTimeout;
        _resourceCoordinator = resourceCoordinator;
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
                typed_relationships_json TEXT DEFAULT '[]',
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
                new SqliteMigration(5, (db, token) => EnsureColumnAsync(db, "embedding_dim", "INTEGER", token)),
                new SqliteMigration(6, (db, token) => EnsureColumnAsync(db, "typed_relationships_json", "TEXT DEFAULT '[]'", token))
            ], ct);
            await SqliteMigrationRunner.ApplyAsync(c, "knowledge-revisions", KnowledgeSchemaVersion,
            [
                new SqliteMigration(1, async (db, token) =>
                {
                    await using var knowledge = db.CreateCommand();
                    knowledge.CommandText = @"
                        CREATE TABLE IF NOT EXISTS knowledge_assertions (
                            assertion_id TEXT PRIMARY KEY,
                            current_revision_id TEXT,
                            created_at TEXT NOT NULL
                        );
                        CREATE TABLE IF NOT EXISTS knowledge_revisions (
                            assertion_id TEXT NOT NULL,
                            revision_id TEXT PRIMARY KEY,
                            previous_revision_id TEXT,
                            content TEXT NOT NULL,
                            scope TEXT NOT NULL,
                            scope_id TEXT NOT NULL,
                            category TEXT NOT NULL,
                            recorded_at TEXT NOT NULL,
                            effective_from TEXT,
                            effective_to TEXT,
                            temporal_origin TEXT NOT NULL,
                            status TEXT NOT NULL,
                            metadata_json TEXT NOT NULL,
                            embedding BLOB,
                            embedding_dim INTEGER
                        );
                        CREATE INDEX IF NOT EXISTS idx_knowledge_current
                            ON knowledge_assertions(current_revision_id);
                        CREATE INDEX IF NOT EXISTS idx_knowledge_revision_assertion
                            ON knowledge_revisions(assertion_id, recorded_at DESC);
                        CREATE TABLE IF NOT EXISTS knowledge_revision_sources (
                            revision_id TEXT NOT NULL,
                            ordinal INTEGER NOT NULL,
                            kind TEXT NOT NULL,
                            title TEXT NOT NULL,
                            locator TEXT,
                            snippet TEXT,
                            score REAL,
                            timestamp TEXT,
                            evidence_origin TEXT NOT NULL,
                            PRIMARY KEY (revision_id, ordinal)
                        );
                        CREATE TABLE IF NOT EXISTS knowledge_revision_decisions (
                            decision_id TEXT PRIMARY KEY,
                            assertion_id TEXT NOT NULL,
                            revision_id TEXT NOT NULL,
                            kind TEXT NOT NULL,
                            actor TEXT NOT NULL,
                            reason TEXT NOT NULL,
                            recorded_at TEXT NOT NULL
                        );";
                    await knowledge.ExecuteNonQueryAsync(token);
                    return true;
                }),
                new SqliteMigration(2, async (db, token) =>
                {
                    await using var proposals = db.CreateCommand();
                    proposals.CommandText = @"
                        CREATE TABLE IF NOT EXISTS knowledge_contradiction_proposals (
                            proposal_id TEXT PRIMARY KEY,
                            left_assertion_id TEXT NOT NULL,
                            left_revision_id TEXT NOT NULL,
                            right_assertion_id TEXT NOT NULL,
                            right_revision_id TEXT NOT NULL,
                            explanation TEXT NOT NULL,
                            origin TEXT NOT NULL,
                            source_comparison TEXT NOT NULL,
                            effective_time_comparison TEXT NOT NULL,
                            proposed_disposition TEXT NOT NULL,
                            missing_evidence TEXT NOT NULL,
                            status TEXT NOT NULL,
                            created_at TEXT NOT NULL
                        );
                        CREATE INDEX IF NOT EXISTS idx_knowledge_contradiction_status
                            ON knowledge_contradiction_proposals(status, created_at DESC);
                        CREATE INDEX IF NOT EXISTS idx_knowledge_contradiction_assertions
                            ON knowledge_contradiction_proposals(left_assertion_id, right_assertion_id);
                        CREATE TABLE IF NOT EXISTS knowledge_contradiction_decisions (
                            proposal_id TEXT PRIMARY KEY,
                            decision_id TEXT NOT NULL,
                            kind TEXT NOT NULL,
                            actor TEXT NOT NULL,
                            reason TEXT NOT NULL,
                            recorded_at TEXT NOT NULL
                        );";
                    await proposals.ExecuteNonQueryAsync(token);
                    return true;
                })
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
        await EnsureLegacyAssertionsAsync(c, ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = includeArchived
            ? CurrentProjectionSql("ORDER BY m.is_pinned DESC, m.importance_score DESC, m.updated_at DESC")
            : CurrentProjectionSql("AND m.is_archived = 0 ORDER BY m.is_pinned DESC, m.importance_score DESC, m.updated_at DESC");
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
        await EnsureLegacyAssertionsAsync(c, ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            SELECT m.*, r.revision_id AS current_revision_id
            FROM memories m
            JOIN knowledge_assertions a ON a.assertion_id = m.id
            JOIN knowledge_revisions r ON r.revision_id = a.current_revision_id
            WHERE r.status IN ('Current', 'Archived', 'Disputed') AND m.id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct) ? Map(rd) : null;
    }

    public async Task<List<Memory>> GetByCategoryAsync(string category, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureLegacyAssertionsAsync(c, ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = CurrentProjectionSql("AND m.category = $cat AND m.is_archived = 0 ORDER BY m.is_pinned DESC, m.importance_score DESC, m.updated_at DESC");
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
        await EnsureLegacyAssertionsAsync(c, ct);
        var cmd = c.CreateCommand();
        var archived = includeArchived ? "" : " AND m.is_archived = 0";
        cmd.CommandText = scopeId is null
            ? CurrentProjectionSql($"AND m.scope = $scope{archived} ORDER BY m.is_pinned DESC, m.updated_at DESC")
            : CurrentProjectionSql($"AND m.scope = $scope AND m.scope_id = $scopeId{archived} ORDER BY m.is_pinned DESC, m.updated_at DESC");
        cmd.Parameters.AddWithValue("$scope", scope.ToString());
        if (scopeId is not null)
            cmd.Parameters.AddWithValue("$scopeId", scopeId);
        var r = new List<Memory>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) r.Add(Map(rd));
        return r;
    }

    public async Task<KnowledgeAssertionRevision> CreateAssertionAsync(
        KnowledgeRevisionDraft draft,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(draft.Memory);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var memory = PrepareMemoryForWrite(draft.Memory, now);
        var sources = NormalizeSources(ResolveSources(draft.SourceReferences, memory));
        memory.Source = sources.FirstOrDefault();
        var (embedding, embeddingDim) = await TryEmbedAsync(memory.Content, ct);

        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureLegacyAssertionsAsync(c, ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);

        var existing = await ScalarStringAsync(c, tx,
            "SELECT current_revision_id FROM knowledge_assertions WHERE assertion_id = $id",
            ct, ("$id", memory.Id));
        if (existing is not null)
            throw new InvalidOperationException($"Knowledge assertion '{memory.Id}' already exists.");

        var revisionId = NewRevisionId();
        var decision = NormalizeDecision(draft.Decision, "create", now);
        await InsertRevisionAsync(c, tx, memory.Id, revisionId, null, memory,
            now, draft.EffectiveFromUtc, draft.EffectiveToUtc,
            draft.TemporalOrigin, sources, decision, embedding, embeddingDim, KnowledgeRevisionStatus.Current, ct);
        await InsertAssertionAsync(c, tx, memory.Id, revisionId, memory.CreatedAt, ct);
        await UpsertMemoryProjectionAsync(c, tx, memory, embedding, embeddingDim, replaceEmbedding: true, ct);
        await tx.CommitAsync(ct);

        return ToPublicRevision(memory.Id, revisionId, null, memory, now,
            draft.EffectiveFromUtc, draft.EffectiveToUtc, draft.TemporalOrigin, sources,
            KnowledgeRevisionStatus.Current, decision);
    }

    public Task<KnowledgeAssertionRevision> ReviseAssertionAsync(
        string assertionId,
        string expectedCurrentRevisionId,
        KnowledgeRevisionDraft draft,
        CancellationToken ct = default) =>
        CreateSuccessorAsync(assertionId, expectedCurrentRevisionId, draft, "revise", ct);

    public Task<KnowledgeAssertionRevision> CorrectAssertionAsync(
        string assertionId,
        string expectedCurrentRevisionId,
        KnowledgeRevisionDraft draft,
        CancellationToken ct = default) =>
        CreateSuccessorAsync(assertionId, expectedCurrentRevisionId, draft, "correct", ct);

    public async Task<KnowledgeAssertionRevision> SetDisputeAsync(
        string assertionId,
        string expectedCurrentRevisionId,
        bool disputed,
        KnowledgeRevisionDecision decision,
        CancellationToken ct = default)
    {
        var normalizedDecision = NormalizeDecision(decision, disputed ? "mark-disputed" : "clear-dispute",
            _timeProvider.GetUtcNow().UtcDateTime);
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureLegacyAssertionsAsync(c, ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        var currentId = await RequireExpectedCurrentAsync(c, tx, assertionId, expectedCurrentRevisionId, ct);
        var memory = await LoadMemoryAsync(c, tx, assertionId, ct)
            ?? throw new InvalidOperationException($"Knowledge assertion '{assertionId}' has no projection.");
        var status = disputed
            ? KnowledgeRevisionStatus.Disputed
            : memory.IsArchived ? KnowledgeRevisionStatus.Archived : KnowledgeRevisionStatus.Current;
        await UpdateRevisionStatusAsync(c, tx, currentId, status, ct);
        await InsertDecisionAsync(c, tx, assertionId, currentId, normalizedDecision, ct);
        await tx.CommitAsync(ct);

        return await GetCurrentRevisionAsync(assertionId, ct)
            ?? throw new InvalidOperationException($"Knowledge assertion '{assertionId}' disappeared after dispute update.");
    }

    public async Task<KnowledgeAssertionRevision> MutatePresentationAsync(
        string assertionId,
        string expectedCurrentRevisionId,
        KnowledgePresentationMutation mutation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureLegacyAssertionsAsync(c, ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        var currentId = await RequireExpectedCurrentAsync(c, tx, assertionId, expectedCurrentRevisionId, ct);
        var existing = await LoadMemoryAsync(c, tx, assertionId, ct)
            ?? throw new InvalidOperationException($"Knowledge assertion '{assertionId}' has no projection.");
        var updated = ApplyPresentation(existing, mutation, _timeProvider.GetUtcNow().UtcDateTime);
        await UpsertMemoryProjectionAsync(c, tx, updated, null, null, replaceEmbedding: false, ct);

        var currentRevision = await LoadStoredRevisionAsync(c, tx, currentId, ct)
            ?? throw new InvalidOperationException($"Current revision '{currentId}' was not found.");
        var status = currentRevision.Public.Status == KnowledgeRevisionStatus.Disputed
            ? KnowledgeRevisionStatus.Disputed
            : updated.IsArchived ? KnowledgeRevisionStatus.Archived : KnowledgeRevisionStatus.Current;
        if (status != currentRevision.Public.Status)
            await UpdateRevisionStatusAsync(c, tx, currentId, status, ct);
        await tx.CommitAsync(ct);

        return (await GetCurrentRevisionAsync(assertionId, ct))!;
    }

    public async Task<KnowledgeAssertionRevision> RestoreRevisionAsync(
        string assertionId,
        string expectedCurrentRevisionId,
        string revisionId,
        KnowledgeRevisionDecision decision,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureLegacyAssertionsAsync(c, ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        var currentId = await RequireExpectedCurrentAsync(c, tx, assertionId, expectedCurrentRevisionId, ct);
        var source = await LoadStoredRevisionAsync(c, tx, revisionId, ct)
            ?? throw new InvalidOperationException($"Revision '{revisionId}' was not found.");
        if (!string.Equals(source.Public.AssertionId, assertionId, StringComparison.Ordinal))
            throw new InvalidOperationException("A revision can only be restored into its own assertion.");

        var currentMemory = await LoadMemoryAsync(c, tx, assertionId, ct)
            ?? throw new InvalidOperationException($"Knowledge assertion '{assertionId}' has no projection.");
        var restored = source.Metadata.ToMemory(assertionId, source.Public.Content, source.Public.Scope,
            source.Public.ScopeId, source.Public.Category, currentMemory.CreatedAt,
            _timeProvider.GetUtcNow().UtcDateTime);
        var sources = NormalizeSources(source.Public.SourceReferences);
        restored.Source ??= sources.FirstOrDefault();
        var (embedding, embeddingDim) = await TryEmbedAsync(restored.Content, ct);
        var normalizedDecision = NormalizeDecision(decision, "restore", _timeProvider.GetUtcNow().UtcDateTime);
        var newRevisionId = NewRevisionId();
        await UpdateRevisionStatusAsync(c, tx, currentId, KnowledgeRevisionStatus.Superseded, ct);
        await InsertRevisionAsync(c, tx, assertionId, newRevisionId, currentId, restored,
            _timeProvider.GetUtcNow().UtcDateTime, source.Public.EffectiveFromUtc, source.Public.EffectiveToUtc,
            source.Public.TemporalOrigin, sources, normalizedDecision, embedding, embeddingDim,
            KnowledgeRevisionStatus.Current, ct);
        await UpdateCurrentRevisionAsync(c, tx, assertionId, newRevisionId, ct);
        await UpsertMemoryProjectionAsync(c, tx, restored, embedding, embeddingDim, replaceEmbedding: true, ct);
        await tx.CommitAsync(ct);

        return ToPublicRevision(assertionId, newRevisionId, currentId, restored,
            _timeProvider.GetUtcNow().UtcDateTime, source.Public.EffectiveFromUtc, source.Public.EffectiveToUtc,
            source.Public.TemporalOrigin, sources, KnowledgeRevisionStatus.Current, normalizedDecision);
    }

    public async Task HardDeleteAsync(
        string assertionId,
        string expectedCurrentRevisionId,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureLegacyAssertionsAsync(c, ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        _ = await RequireExpectedCurrentAsync(c, tx, assertionId, expectedCurrentRevisionId, ct);
        foreach (var sql in new[]
        {
            "DELETE FROM knowledge_contradiction_decisions WHERE proposal_id IN (SELECT proposal_id FROM knowledge_contradiction_proposals WHERE left_assertion_id = $id OR right_assertion_id = $id)",
            "DELETE FROM knowledge_contradiction_proposals WHERE left_assertion_id = $id OR right_assertion_id = $id",
            "DELETE FROM knowledge_revision_sources WHERE revision_id IN (SELECT revision_id FROM knowledge_revisions WHERE assertion_id = $id)",
            "DELETE FROM knowledge_revision_decisions WHERE assertion_id = $id",
            "DELETE FROM knowledge_revisions WHERE assertion_id = $id",
            "DELETE FROM knowledge_assertions WHERE assertion_id = $id",
            "DELETE FROM memories_fts WHERE id = $id",
            "DELETE FROM memories WHERE id = $id"
        })
        {
            await ExecuteAsync(c, tx, sql, ct, ("$id", assertionId));
        }

        await tx.CommitAsync(ct);
    }

    public async Task<KnowledgeAssertionRevision?> GetCurrentRevisionAsync(
        string assertionId,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureLegacyAssertionsAsync(c, ct);
        var revisionId = await ScalarStringAsync(c, null,
            "SELECT current_revision_id FROM knowledge_assertions WHERE assertion_id = $id",
            ct, ("$id", assertionId));
        return revisionId is null ? null : (await LoadStoredRevisionAsync(c, null, revisionId, ct))?.Public;
    }

    public async Task<IReadOnlyList<KnowledgeAssertionRevision>> QueryAsync(
        KnowledgeTimeQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureLegacyAssertionsAsync(c, ct);

        var conditions = new List<string>();
        var cmd = c.CreateCommand();
        switch (query.Mode)
        {
            case KnowledgeTimeQueryMode.Current:
                conditions.Add("r.revision_id = a.current_revision_id");
                conditions.Add(query.IncludeDisputed
                    ? "r.status IN ('Current', 'Archived', 'Disputed')"
                    : "r.status IN ('Current', 'Archived')");
                break;
            case KnowledgeTimeQueryMode.AsOf:
                if (query.AsOfUtc is null)
                    throw new ArgumentException("As-of queries require AsOfUtc.", nameof(query));
                conditions.Add("r.effective_from IS NOT NULL");
                conditions.Add("r.effective_from <= $asOf");
                conditions.Add("(r.effective_to IS NULL OR r.effective_to > $asOf)");
                if (!query.IncludeDisputed)
                    conditions.Add("r.status <> 'Disputed'");
                cmd.Parameters.AddWithValue("$asOf", query.AsOfUtc.Value.ToUniversalTime().ToString("O"));
                break;
            case KnowledgeTimeQueryMode.History:
                if (!query.IncludeDisputed)
                    conditions.Add("r.status <> 'Disputed'");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(query.Mode));
        }

        if (query.Scope is not null)
        {
            conditions.Add("r.scope = $scope");
            cmd.Parameters.AddWithValue("$scope", query.Scope.Value.ToString());
        }
        if (query.ScopeId is not null)
        {
            conditions.Add("r.scope_id = $scopeId");
            cmd.Parameters.AddWithValue("$scopeId", query.ScopeId);
        }

        var where = conditions.Count == 0 ? "1 = 1" : string.Join(" AND ", conditions);
        cmd.CommandText = $"SELECT r.revision_id FROM knowledge_revisions r JOIN knowledge_assertions a ON a.assertion_id = r.assertion_id WHERE {where} ORDER BY r.recorded_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(query.Limit, 1, 500));
        var ids = new List<string>();
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
        {
            while (await rd.ReadAsync(ct))
                ids.Add(rd.GetString(0));
        }

        var result = new List<KnowledgeAssertionRevision>(ids.Count);
        foreach (var id in ids)
        {
            var revision = await LoadStoredRevisionAsync(c, null, id, ct);
            if (revision is not null)
                result.Add(revision.Public);
        }
        return result;
    }

    public async Task<IReadOnlyList<KnowledgeAssertionRevision>> GetHistoryAsync(
        string assertionId,
        CancellationToken ct = default) =>
        await QueryAsync(new KnowledgeTimeQuery(KnowledgeTimeQueryMode.History, IncludeDisputed: true, Limit: 500), assertionId, ct);

    public async Task<KnowledgeContradictionProposal> CreateContradictionProposalAsync(
        KnowledgeContradictionProposalDraft draft,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(draft.LeftAssertionId)
            || string.IsNullOrWhiteSpace(draft.LeftRevisionId)
            || string.IsNullOrWhiteSpace(draft.RightAssertionId)
            || string.IsNullOrWhiteSpace(draft.RightRevisionId))
            throw new ArgumentException("Both exact assertion and revision identities are required.", nameof(draft));
        if (string.Equals(draft.LeftRevisionId, draft.RightRevisionId, StringComparison.Ordinal))
            throw new ArgumentException("A contradiction proposal requires two different revisions.", nameof(draft));
        if (string.IsNullOrWhiteSpace(draft.Explanation))
            throw new ArgumentException("A contradiction proposal requires an explanation.", nameof(draft));

        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureLegacyAssertionsAsync(c, ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        var left = await LoadStoredRevisionAsync(c, tx, draft.LeftRevisionId, ct)
            ?? throw new KeyNotFoundException($"Revision '{draft.LeftRevisionId}' was not found.");
        var right = await LoadStoredRevisionAsync(c, tx, draft.RightRevisionId, ct)
            ?? throw new KeyNotFoundException($"Revision '{draft.RightRevisionId}' was not found.");
        if (!string.Equals(left.Public.AssertionId, draft.LeftAssertionId, StringComparison.Ordinal)
            || !string.Equals(right.Public.AssertionId, draft.RightAssertionId, StringComparison.Ordinal))
            throw new ArgumentException("Each revision must belong to its named assertion.", nameof(draft));

        var proposalId = NewRevisionId();
        var created = _timeProvider.GetUtcNow().UtcDateTime;
        var origin = draft.Origin == KnowledgeTemporalOrigin.DeterministicRule
            ? KnowledgeTemporalOrigin.DeterministicRule
            : KnowledgeTemporalOrigin.ModelInference;
        await ExecuteAsync(c, tx, @"
            INSERT INTO knowledge_contradiction_proposals (
                proposal_id, left_assertion_id, left_revision_id, right_assertion_id,
                right_revision_id, explanation, origin, source_comparison,
                effective_time_comparison, proposed_disposition, missing_evidence,
                status, created_at)
            VALUES ($proposal, $leftAssertion, $leftRevision, $rightAssertion,
                $rightRevision, $explanation, $origin, $sourceComparison,
                $effectiveTimeComparison, $disposition, $missingEvidence,
                'Pending', $created)", ct,
            ("$proposal", proposalId),
            ("$leftAssertion", draft.LeftAssertionId),
            ("$leftRevision", draft.LeftRevisionId),
            ("$rightAssertion", draft.RightAssertionId),
            ("$rightRevision", draft.RightRevisionId),
            ("$explanation", Bound(draft.Explanation, 4096)),
            ("$origin", origin.ToString()),
            ("$sourceComparison", Bound(draft.SourceComparison, 2048)),
            ("$effectiveTimeComparison", Bound(draft.EffectiveTimeComparison, 2048)),
            ("$disposition", draft.ProposedDisposition.ToString()),
            ("$missingEvidence", Bound(draft.MissingEvidence, 2048)),
            ("$created", created.ToString("O")));
        await tx.CommitAsync(ct);

        return new KnowledgeContradictionProposal(
            proposalId,
            draft.LeftAssertionId,
            draft.LeftRevisionId,
            draft.RightAssertionId,
            draft.RightRevisionId,
            Bound(draft.Explanation, 4096),
            origin,
            Bound(draft.SourceComparison, 2048),
            Bound(draft.EffectiveTimeComparison, 2048),
            draft.ProposedDisposition,
            Bound(draft.MissingEvidence, 2048),
            KnowledgeContradictionProposalStatus.Pending,
            created,
            null);
    }

    public async Task<IReadOnlyList<KnowledgeContradictionProposal>> GetContradictionProposalsAsync(
        string? assertionId = null,
        bool includeReviewed = false,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureLegacyAssertionsAsync(c, ct);
        var command = c.CreateCommand();
        var conditions = new List<string>();
        if (!includeReviewed)
            conditions.Add("status = 'Pending'");
        if (!string.IsNullOrWhiteSpace(assertionId))
        {
            conditions.Add("(left_assertion_id = $assertion OR right_assertion_id = $assertion)");
            command.Parameters.AddWithValue("$assertion", assertionId);
        }
        var where = conditions.Count == 0 ? "1 = 1" : string.Join(" AND ", conditions);
        command.CommandText = $@"
            SELECT proposal_id, left_assertion_id, left_revision_id, right_assertion_id,
                   right_revision_id, explanation, origin, source_comparison,
                   effective_time_comparison, proposed_disposition, missing_evidence,
                   status, created_at
            FROM knowledge_contradiction_proposals
            WHERE {where}
            ORDER BY created_at DESC LIMIT 100";
        var proposals = new List<KnowledgeContradictionProposal>();
        var stored = new List<(string ProposalId, string LeftAssertionId, string LeftRevisionId,
            string RightAssertionId, string RightRevisionId, string Explanation, string Origin,
            string SourceComparison, string EffectiveTimeComparison, string Disposition,
            string MissingEvidence, string Status, string CreatedAt)>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            stored.Add((
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11),
                reader.GetString(12)));
        }
        foreach (var row in stored)
        {
            var origin = Enum.TryParse<KnowledgeTemporalOrigin>(row.Origin, out var parsedOrigin)
                ? parsedOrigin : KnowledgeTemporalOrigin.ModelInference;
            var disposition = Enum.TryParse<KnowledgeContradictionDisposition>(row.Disposition, out var parsedDisposition)
                ? parsedDisposition : KnowledgeContradictionDisposition.NoRelationship;
            var status = Enum.TryParse<KnowledgeContradictionProposalStatus>(row.Status, out var parsedStatus)
                ? parsedStatus : KnowledgeContradictionProposalStatus.Pending;
            var decision = await LoadContradictionDecisionAsync(c, row.ProposalId, ct);
            proposals.Add(new KnowledgeContradictionProposal(
                row.ProposalId,
                row.LeftAssertionId,
                row.LeftRevisionId,
                row.RightAssertionId,
                row.RightRevisionId,
                row.Explanation,
                origin,
                row.SourceComparison,
                row.EffectiveTimeComparison,
                disposition,
                row.MissingEvidence,
                status,
                SqliteDateTime.Parse(row.CreatedAt),
                decision));
        }
        return proposals;
    }

    public async Task RejectContradictionProposalAsync(
        string proposalId,
        KnowledgeRevisionDecision decision,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(proposalId))
            throw new ArgumentException("Proposal id is required.", nameof(proposalId));
        var normalized = NormalizeDecision(decision, "reject-contradiction", _timeProvider.GetUtcNow().UtcDateTime);
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureLegacyAssertionsAsync(c, ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        var status = await ScalarStringAsync(c, tx,
            "SELECT status FROM knowledge_contradiction_proposals WHERE proposal_id = $id",
            ct, ("$id", proposalId));
        if (status is null)
            throw new KeyNotFoundException($"Contradiction proposal '{proposalId}' was not found.");
        if (!string.Equals(status, KnowledgeContradictionProposalStatus.Pending.ToString(), StringComparison.Ordinal))
            throw new InvalidOperationException("Only a pending contradiction proposal can be rejected.");

        await ExecuteAsync(c, tx,
            "UPDATE knowledge_contradiction_proposals SET status = 'Rejected' WHERE proposal_id = $id",
            ct, ("$id", proposalId));
        await ExecuteAsync(c, tx, @"
            INSERT INTO knowledge_contradiction_decisions (
                proposal_id, decision_id, kind, actor, reason, recorded_at)
            VALUES ($proposal, $decision, $kind, $actor, $reason, $recorded)", ct,
            ("$proposal", proposalId), ("$decision", normalized.DecisionId ?? NewRevisionId()),
            ("$kind", normalized.Kind), ("$actor", normalized.Actor),
            ("$reason", normalized.Reason), ("$recorded", normalized.RecordedAtUtc.ToString("O")));
        await tx.CommitAsync(ct);
    }

    private static async Task<KnowledgeRevisionDecision?> LoadContradictionDecisionAsync(
        SqliteConnection c,
        string proposalId,
        CancellationToken ct)
    {
        await using var command = c.CreateCommand();
        command.CommandText = @"
            SELECT decision_id, kind, actor, reason, recorded_at
            FROM knowledge_contradiction_decisions
            WHERE proposal_id = $proposal";
        command.Parameters.AddWithValue("$proposal", proposalId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new KnowledgeRevisionDecision(reader.GetString(1), reader.GetString(2), reader.GetString(3),
                SqliteDateTime.Parse(reader.GetString(4)), reader.GetString(0))
            : null;
    }

    private async Task<IReadOnlyList<KnowledgeAssertionRevision>> QueryAsync(
        KnowledgeTimeQuery query,
        string assertionId,
        CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureLegacyAssertionsAsync(c, ct);
        var cmd = c.CreateCommand();
        var status = query.IncludeDisputed ? "" : " AND r.status <> 'Disputed'";
        cmd.CommandText = $"SELECT r.revision_id FROM knowledge_revisions r WHERE r.assertion_id = $id{status} ORDER BY r.recorded_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$id", assertionId);
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(query.Limit, 1, 500));
        var ids = new List<string>();
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
        {
            while (await rd.ReadAsync(ct)) ids.Add(rd.GetString(0));
        }
        var result = new List<KnowledgeAssertionRevision>(ids.Count);
        foreach (var id in ids)
        {
            var revision = await LoadStoredRevisionAsync(c, null, id, ct);
            if (revision is not null) result.Add(revision.Public);
        }
        return result;
    }

    private async Task<KnowledgeAssertionRevision> CreateSuccessorAsync(
        string assertionId,
        string expectedCurrentRevisionId,
        KnowledgeRevisionDraft draft,
        string decisionKind,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(draft.Memory);
        if (!string.Equals(assertionId, draft.Memory.Id, StringComparison.Ordinal))
            throw new ArgumentException("The draft memory id must match the assertion id.", nameof(draft));

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var memory = PrepareMemoryForWrite(draft.Memory, now);
        var (embedding, embeddingDim) = await TryEmbedAsync(memory.Content, ct);

        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureLegacyAssertionsAsync(c, ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        var currentId = await RequireExpectedCurrentAsync(c, tx, assertionId, expectedCurrentRevisionId, ct);
        var previous = await LoadStoredRevisionAsync(c, tx, currentId, ct)
            ?? throw new InvalidOperationException($"Current revision '{currentId}' was not found.");
        var sources = NormalizeSources(ResolveSources(draft.SourceReferences, memory, previous.Public.SourceReferences));
        memory.Source = sources.FirstOrDefault();
        var revisionId = NewRevisionId();
        var decision = NormalizeDecision(draft.Decision, decisionKind, now);

        await UpdateRevisionStatusAsync(c, tx, currentId, KnowledgeRevisionStatus.Superseded, ct);
        await InsertRevisionAsync(c, tx, assertionId, revisionId, currentId, memory, now,
            draft.EffectiveFromUtc, draft.EffectiveToUtc, draft.TemporalOrigin, sources, decision,
            embedding, embeddingDim, KnowledgeRevisionStatus.Current, ct);
        await UpdateCurrentRevisionAsync(c, tx, assertionId, revisionId, ct);
        await UpsertMemoryProjectionAsync(c, tx, memory, embedding, embeddingDim, replaceEmbedding: true, ct);
        await tx.CommitAsync(ct);

        return ToPublicRevision(assertionId, revisionId, currentId, memory, now,
            draft.EffectiveFromUtc, draft.EffectiveToUtc, draft.TemporalOrigin, sources,
            KnowledgeRevisionStatus.Current, decision);
    }

    private async Task<string> RequireExpectedCurrentAsync(
        SqliteConnection c,
        SqliteTransaction tx,
        string assertionId,
        string expectedCurrentRevisionId,
        CancellationToken ct)
    {
        var actual = await ScalarStringAsync(c, tx,
            "SELECT current_revision_id FROM knowledge_assertions WHERE assertion_id = $id",
            ct, ("$id", assertionId));
        if (!string.Equals(actual, expectedCurrentRevisionId, StringComparison.Ordinal))
            throw new KnowledgeRevisionConflictException(assertionId, expectedCurrentRevisionId, actual);
        return actual!;
    }

    private static Memory PrepareMemoryForWrite(Memory memory, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(memory.Id))
            throw new ArgumentException("Memory id is required.", nameof(memory));
        if (memory.CreatedAt == default)
            memory.CreatedAt = now;
        memory.UpdatedAt = now;
        memory.Tags = NormalizeTags(memory.Tags);
        var relationships = KnowledgeRelationshipSemantics.Normalize(memory.Relationships, memory.RelatedMemoryIds);
        memory.Relationships = relationships;
        memory.RelatedMemoryIds = relationships
            .Where(r => r.Kind == KnowledgeRelationshipKind.RelatedTo && r.Target.Kind == KnowledgeEntityKind.Memory)
            .Select(r => r.Target.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return memory;
    }

    private static Memory ApplyPresentation(Memory existing, KnowledgePresentationMutation mutation, DateTime now)
    {
        existing.Title = mutation.Title;
        existing.Scope = mutation.Scope;
        existing.ScopeId = mutation.ScopeId;
        existing.Category = mutation.Category;
        existing.Tags = NormalizeTags(mutation.Tags);
        existing.ImportanceScore = mutation.ImportanceScore;
        existing.IsPinned = mutation.IsPinned;
        existing.IsArchived = mutation.IsArchived;
        existing.FrequencyCount = mutation.FrequencyCount;
        existing.LastMergeTime = mutation.LastMergeTime;
        existing.ExpirationDate = mutation.ExpirationDate;
        existing.Relationships = KnowledgeRelationshipSemantics.Normalize(mutation.Relationships, mutation.RelatedMemoryIds);
        existing.RelatedMemoryIds = existing.Relationships
            .Where(r => r.Kind == KnowledgeRelationshipKind.RelatedTo && r.Target.Kind == KnowledgeEntityKind.Memory)
            .Select(r => r.Target.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        existing.IsEncrypted = mutation.IsEncrypted;
        existing.SourceConversationId = mutation.SourceConversationId;
        existing.UpdatedAt = now;
        return existing;
    }

    private async Task<(byte[]? Blob, int? Dimension)> TryEmbedAsync(string content, CancellationToken ct)
    {
        if (_embeddings is null || string.IsNullOrWhiteSpace(content))
            return (null, null);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_queryEmbedTimeout);
            var vector = await _embeddings.EmbedAsync(content, timeoutCts.Token);
            return (ToBlob(vector), vector.Length);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return (null, null);
        }
    }

    private static string CurrentProjectionSql(string tail) => $@"
        SELECT m.*, r.revision_id AS current_revision_id
        FROM memories m
        JOIN knowledge_assertions a ON a.assertion_id = m.id
        JOIN knowledge_revisions r ON r.revision_id = a.current_revision_id
        WHERE r.status IN ('Current', 'Archived')
          {tail}";

    private static string NewRevisionId() => Guid.NewGuid().ToString("N");

    private static IReadOnlyList<SourceReference> ResolveSources(
        IReadOnlyList<SourceReference>? sources,
        Memory memory,
        IReadOnlyList<SourceReference>? fallback = null)
    {
        if (sources is not null)
            return sources;
        if (memory.Source is not null)
            return [memory.Source];
        return fallback ?? [];
    }

    private static IReadOnlyList<SourceReference> NormalizeSources(IEnumerable<SourceReference> sources) =>
        sources.Select(source => new SourceReference(
            source.Kind,
            Bound(source.Title, 512),
            BoundNullable(source.Locator, 2048),
            BoundNullable(source.Snippet, 4096),
            source.Score is { } score && double.IsFinite(score) ? score : null,
            source.Timestamp?.ToUniversalTime(),
            source.EvidenceOrigin)).ToList();

    private static KnowledgeRevisionDecision NormalizeDecision(
        KnowledgeRevisionDecision? decision,
        string defaultKind,
        DateTime now)
    {
        if (decision is null)
            return new KnowledgeRevisionDecision(defaultKind, "system", "Accepted by the owning writer.", now, NewRevisionId());
        return decision with
        {
            Kind = Bound(decision.Kind, 128),
            Actor = Bound(decision.Actor, 128),
            Reason = Bound(decision.Reason, 2048),
            RecordedAtUtc = decision.RecordedAtUtc == default ? now : decision.RecordedAtUtc.ToUniversalTime(),
            DecisionId = string.IsNullOrWhiteSpace(decision.DecisionId) ? NewRevisionId() : Bound(decision.DecisionId, 128)
        };
    }

    private static KnowledgeAssertionRevision ToPublicRevision(
        string assertionId,
        string revisionId,
        string? previousRevisionId,
        Memory memory,
        DateTime recordedAt,
        DateTime? effectiveFrom,
        DateTime? effectiveTo,
        KnowledgeTemporalOrigin temporalOrigin,
        IReadOnlyList<SourceReference> sources,
        KnowledgeRevisionStatus status,
        KnowledgeRevisionDecision? decision) =>
        new(assertionId, revisionId, previousRevisionId, memory.Content, memory.Scope, memory.ScopeId,
            memory.Category, recordedAt, effectiveFrom, effectiveTo, temporalOrigin, sources, status, decision);

    private static async Task InsertAssertionAsync(
        SqliteConnection c,
        SqliteTransaction tx,
        string assertionId,
        string revisionId,
        DateTime createdAt,
        CancellationToken ct)
    {
        await ExecuteAsync(c, tx, @"
            INSERT INTO knowledge_assertions (assertion_id, current_revision_id, created_at)
            VALUES ($id, $revision, $created)", ct,
            ("$id", assertionId), ("$revision", revisionId), ("$created", createdAt.ToString("O")));
    }

    private static async Task UpdateCurrentRevisionAsync(
        SqliteConnection c,
        SqliteTransaction tx,
        string assertionId,
        string revisionId,
        CancellationToken ct) =>
        await ExecuteAsync(c, tx,
            "UPDATE knowledge_assertions SET current_revision_id = $revision WHERE assertion_id = $id",
            ct, ("$id", assertionId), ("$revision", revisionId));

    private static async Task UpdateRevisionStatusAsync(
        SqliteConnection c,
        SqliteTransaction tx,
        string revisionId,
        KnowledgeRevisionStatus status,
        CancellationToken ct) =>
        await ExecuteAsync(c, tx,
            "UPDATE knowledge_revisions SET status = $status WHERE revision_id = $revision",
            ct, ("$revision", revisionId), ("$status", status.ToString()));

    private static async Task InsertRevisionAsync(
        SqliteConnection c,
        SqliteTransaction tx,
        string assertionId,
        string revisionId,
        string? previousRevisionId,
        Memory memory,
        DateTime recordedAt,
        DateTime? effectiveFrom,
        DateTime? effectiveTo,
        KnowledgeTemporalOrigin temporalOrigin,
        IReadOnlyList<SourceReference> sources,
        KnowledgeRevisionDecision decision,
        byte[]? embedding,
        int? embeddingDim,
        KnowledgeRevisionStatus status,
        CancellationToken ct)
    {
        if (effectiveFrom is not null && effectiveTo is not null && effectiveTo <= effectiveFrom)
            throw new ArgumentException("EffectiveToUtc must be after EffectiveFromUtc.");

        await ExecuteAsync(c, tx, @"
            INSERT INTO knowledge_revisions (
                assertion_id, revision_id, previous_revision_id, content, scope,
                scope_id, category, recorded_at, effective_from, effective_to,
                temporal_origin, status, metadata_json, embedding, embedding_dim)
            VALUES ($assertion, $revision, $previous, $content, $scope, $scopeId,
                $category, $recorded, $effectiveFrom, $effectiveTo, $origin,
                $status, $metadata, $embedding, $embeddingDim)", ct,
            ("$assertion", assertionId), ("$revision", revisionId), ("$previous", previousRevisionId),
            ("$content", memory.Content), ("$scope", memory.Scope.ToString()), ("$scopeId", memory.ScopeId),
            ("$category", memory.Category), ("$recorded", recordedAt.ToString("O")),
            ("$effectiveFrom", effectiveFrom?.ToUniversalTime().ToString("O")),
            ("$effectiveTo", effectiveTo?.ToUniversalTime().ToString("O")),
            ("$origin", temporalOrigin.ToString()), ("$status", status.ToString()),
            ("$metadata", JsonSerializer.Serialize(MemoryRevisionMetadata.FromMemory(memory))),
            ("$embedding", embedding), ("$embeddingDim", embeddingDim));

        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            await ExecuteAsync(c, tx, @"
                INSERT INTO knowledge_revision_sources (
                    revision_id, ordinal, kind, title, locator, snippet, score,
                    timestamp, evidence_origin)
                VALUES ($revision, $ordinal, $kind, $title, $locator, $snippet,
                    $score, $timestamp, $origin)", ct,
                ("$revision", revisionId), ("$ordinal", i), ("$kind", source.Kind.ToString()),
                ("$title", Bound(source.Title, 512)), ("$locator", BoundNullable(source.Locator, 2048)),
                ("$snippet", BoundNullable(source.Snippet, 4096)), ("$score", source.Score),
                ("$timestamp", source.Timestamp?.ToUniversalTime().ToString("O")),
                ("$origin", source.EvidenceOrigin.ToString()));
        }

        await InsertDecisionAsync(c, tx, assertionId, revisionId, decision, ct);
    }

    private static async Task InsertDecisionAsync(
        SqliteConnection c,
        SqliteTransaction tx,
        string assertionId,
        string revisionId,
        KnowledgeRevisionDecision? decision,
        CancellationToken ct)
    {
        if (decision is null) return;
        await ExecuteAsync(c, tx, @"
            INSERT INTO knowledge_revision_decisions (
                decision_id, assertion_id, revision_id, kind, actor, reason, recorded_at)
            VALUES ($decision, $assertion, $revision, $kind, $actor, $reason, $recorded)", ct,
            ("$decision", decision.DecisionId ?? NewRevisionId()), ("$assertion", assertionId),
            ("$revision", revisionId), ("$kind", Bound(decision.Kind, 128)),
            ("$actor", Bound(decision.Actor, 128)), ("$reason", Bound(decision.Reason, 2048)),
            ("$recorded", decision.RecordedAtUtc.ToUniversalTime().ToString("O")));
    }

    private async Task EnsureLegacyAssertionsAsync(SqliteConnection c, CancellationToken ct)
    {
        var legacy = new List<(Memory Memory, byte[]? Embedding, int? EmbeddingDimension)>();
        await using (var select = c.CreateCommand())
        {
            select.CommandText = @"
                SELECT m.*, a.current_revision_id AS current_revision_id
                FROM memories m
                LEFT JOIN knowledge_assertions a ON a.assertion_id = m.id
                WHERE a.assertion_id IS NULL";
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var embeddingOrdinal = reader.GetOrdinal("embedding");
                var embedding = reader.IsDBNull(embeddingOrdinal) ? null : (byte[])reader[embeddingOrdinal];
                var dimensionOrdinal = reader.GetOrdinal("embedding_dim");
                var dimension = reader.IsDBNull(dimensionOrdinal) ? (int?)null : reader.GetInt32(dimensionOrdinal);
                legacy.Add((Map(reader), embedding, dimension));
            }
        }

        if (legacy.Count == 0) return;
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        foreach (var (memory, embedding, embeddingDimension) in legacy)
        {
            var revisionId = LegacyRevisionId(memory.Id);
            var source = memory.Source is null ? Array.Empty<SourceReference>() : new[] { memory.Source };
            var decision = new KnowledgeRevisionDecision(
                "legacy-import", "migration", "Existing memory row represented without synthetic history.",
                memory.UpdatedAt, $"legacy:{memory.Id}:decision");
            await ExecuteAsync(c, tx, @"
                INSERT OR IGNORE INTO knowledge_assertions (assertion_id, current_revision_id, created_at)
                VALUES ($id, $revision, $created)", ct,
                ("$id", memory.Id), ("$revision", revisionId), ("$created", memory.CreatedAt.ToString("O")));
            await ExecuteAsync(c, tx, @"
                INSERT OR IGNORE INTO knowledge_revisions (
                    assertion_id, revision_id, previous_revision_id, content, scope,
                    scope_id, category, recorded_at, effective_from, effective_to,
                    temporal_origin, status, metadata_json, embedding, embedding_dim)
                VALUES ($assertion, $revision, NULL, $content, $scope, $scopeId,
                    $category, $recorded, NULL, NULL, 'Unknown', $status, $metadata,
                    $embedding, $embeddingDim)", ct,
                ("$assertion", memory.Id), ("$revision", revisionId), ("$content", memory.Content),
                ("$scope", memory.Scope.ToString()), ("$scopeId", memory.ScopeId),
                ("$category", memory.Category), ("$recorded", memory.UpdatedAt.ToString("O")),
                ("$status", memory.IsArchived ? KnowledgeRevisionStatus.Archived.ToString() : KnowledgeRevisionStatus.Current.ToString()),
                ("$metadata", JsonSerializer.Serialize(MemoryRevisionMetadata.FromMemory(memory))),
                ("$embedding", embedding), ("$embeddingDim", embeddingDimension));
            for (var i = 0; i < source.Length; i++)
                await InsertSourceAsync(c, tx, revisionId, i, source[i], ct);
            await InsertDecisionAsync(c, tx, memory.Id, revisionId, decision, ct);
        }
        await tx.CommitAsync(ct);
    }

    private static string LegacyRevisionId(string assertionId) => $"legacy:{assertionId}";

    private static async Task UpsertMemoryProjectionAsync(
        SqliteConnection c,
        SqliteTransaction tx,
        Memory memory,
        byte[]? embedding,
        int? embeddingDim,
        bool replaceEmbedding,
        CancellationToken ct)
    {
        var tagsJson = JsonSerializer.Serialize(NormalizeTags(memory.Tags));
        var relationships = KnowledgeRelationshipSemantics.Normalize(memory.Relationships, memory.RelatedMemoryIds);
        var relationshipsJson = JsonSerializer.Serialize(relationships
            .Where(r => r.Kind == KnowledgeRelationshipKind.RelatedTo && r.Target.Kind == KnowledgeEntityKind.Memory)
            .Select(r => r.Target.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList());
        var typedRelationshipsJson = JsonSerializer.Serialize(relationships);
        var sourceJson = memory.Source is null ? null : JsonSerializer.Serialize(memory.Source);
        await ExecuteAsync(c, tx, @"
            INSERT INTO memories (
                id, category, content, created_at, updated_at, source_conversation_id,
                importance_score, tags_json, is_pinned, is_archived, frequency_count,
                last_merge_time, expiration_date, relationships_json,
                typed_relationships_json, is_encrypted, scope, scope_id, title,
                source_json, embedding, embedding_dim)
            VALUES ($id, $category, $content, $created, $updated, $sourceConversation,
                $importance, $tags, $pinned, $archived, $frequency, $merge, $expiration,
                $relationships, $typedRelationships, $encrypted, $scope, $scopeId,
                $title, $sourceJson, $embedding, $embeddingDim)
            ON CONFLICT(id) DO UPDATE SET
                category = excluded.category,
                content = excluded.content,
                created_at = excluded.created_at,
                updated_at = excluded.updated_at,
                source_conversation_id = excluded.source_conversation_id,
                importance_score = excluded.importance_score,
                tags_json = excluded.tags_json,
                is_pinned = excluded.is_pinned,
                is_archived = excluded.is_archived,
                frequency_count = excluded.frequency_count,
                last_merge_time = excluded.last_merge_time,
                expiration_date = excluded.expiration_date,
                relationships_json = excluded.relationships_json,
                typed_relationships_json = excluded.typed_relationships_json,
                is_encrypted = excluded.is_encrypted,
                scope = excluded.scope,
                scope_id = excluded.scope_id,
                title = excluded.title,
                source_json = excluded.source_json,
                embedding = CASE WHEN $replaceEmbedding = 1 THEN excluded.embedding ELSE memories.embedding END,
                embedding_dim = CASE WHEN $replaceEmbedding = 1 THEN excluded.embedding_dim ELSE memories.embedding_dim END", ct,
            ("$id", memory.Id), ("$category", memory.Category), ("$content", memory.Content),
            ("$created", memory.CreatedAt.ToString("O")), ("$updated", memory.UpdatedAt.ToString("O")),
            ("$sourceConversation", memory.SourceConversationId), ("$importance", memory.ImportanceScore),
            ("$tags", tagsJson), ("$pinned", memory.IsPinned ? 1 : 0), ("$archived", memory.IsArchived ? 1 : 0),
            ("$frequency", memory.FrequencyCount), ("$merge", memory.LastMergeTime?.ToString("O")),
            ("$expiration", memory.ExpirationDate?.ToString("O")), ("$relationships", relationshipsJson),
            ("$typedRelationships", typedRelationshipsJson), ("$encrypted", memory.IsEncrypted ? 1 : 0),
            ("$scope", memory.Scope.ToString()), ("$scopeId", memory.ScopeId), ("$title", memory.Title),
            ("$sourceJson", sourceJson), ("$embedding", embedding), ("$embeddingDim", embeddingDim),
            ("$replaceEmbedding", replaceEmbedding ? 1 : 0));
        await UpsertFtsAsync(c, memory, tagsJson, ct, tx);
    }

    private static async Task InsertSourceAsync(
        SqliteConnection c,
        SqliteTransaction tx,
        string revisionId,
        int ordinal,
        SourceReference source,
        CancellationToken ct) =>
        await ExecuteAsync(c, tx, @"
            INSERT OR IGNORE INTO knowledge_revision_sources (
                revision_id, ordinal, kind, title, locator, snippet, score,
                timestamp, evidence_origin)
            VALUES ($revision, $ordinal, $kind, $title, $locator, $snippet,
                $score, $timestamp, $origin)", ct,
            ("$revision", revisionId), ("$ordinal", ordinal), ("$kind", source.Kind.ToString()),
            ("$title", Bound(source.Title, 512)), ("$locator", BoundNullable(source.Locator, 2048)),
            ("$snippet", BoundNullable(source.Snippet, 4096)), ("$score", source.Score),
            ("$timestamp", source.Timestamp?.ToUniversalTime().ToString("O")),
            ("$origin", source.EvidenceOrigin.ToString()));

    private static async Task<Memory?> LoadMemoryAsync(
        SqliteConnection c,
        SqliteTransaction? tx,
        string id,
        CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT m.*, a.current_revision_id AS current_revision_id
            FROM memories m
            JOIN knowledge_assertions a ON a.assertion_id = m.id
            WHERE m.id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    private static async Task<string?> ScalarStringAsync(
        SqliteConnection c,
        SqliteTransaction? tx,
        string sql,
        CancellationToken ct,
        params (string Name, string? Value)[] parameters)
    {
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);
        var valueResult = await cmd.ExecuteScalarAsync(ct);
        return valueResult is null or DBNull ? null : Convert.ToString(valueResult, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(
        SqliteConnection c,
        SqliteTransaction? tx,
        string sql,
        CancellationToken ct,
        params (string Name, object? Value)[] parameters)
    {
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Task ExecuteAsync(
        SqliteConnection c,
        SqliteTransaction? tx,
        string sql,
        CancellationToken ct,
        params (string Name, string? Value)[] parameters) =>
        ExecuteAsync(c, tx, sql, ct,
            parameters.Select(parameter => (parameter.Name, (object?)parameter.Value)).ToArray());

    private static async Task<StoredRevision?> LoadStoredRevisionAsync(
        SqliteConnection c,
        SqliteTransaction? tx,
        string revisionId,
        CancellationToken ct)
    {
        StoredRevision? stored = null;
        await using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT * FROM knowledge_revisions WHERE revision_id = $revision";
            cmd.Parameters.AddWithValue("$revision", revisionId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var scope = Enum.TryParse<MemoryScope>(GetString(reader, "scope", "Global"), out var parsedScope)
                    ? parsedScope : MemoryScope.Global;
                var status = Enum.TryParse<KnowledgeRevisionStatus>(GetString(reader, "status"), out var parsedStatus)
                    ? parsedStatus : KnowledgeRevisionStatus.Superseded;
                var origin = Enum.TryParse<KnowledgeTemporalOrigin>(GetString(reader, "temporal_origin"), out var parsedOrigin)
                    ? parsedOrigin : KnowledgeTemporalOrigin.Unknown;
                var metadataJson = GetString(reader, "metadata_json", "{}");
                var metadata = JsonSerializer.Deserialize<MemoryRevisionMetadata>(metadataJson)
                    ?? MemoryRevisionMetadata.Empty;
                stored = new StoredRevision(
                    new KnowledgeAssertionRevision(
                        GetString(reader, "assertion_id"),
                        GetString(reader, "revision_id"),
                        GetStringNullable(reader, "previous_revision_id"),
                        GetString(reader, "content"),
                        scope,
                        GetString(reader, "scope_id"),
                        GetString(reader, "category", "facts"),
                        SqliteDateTime.Parse(GetString(reader, "recorded_at")),
                        GetDateTimeNullable(reader, "effective_from"),
                        GetDateTimeNullable(reader, "effective_to"),
                        origin,
                        [],
                        status,
                        null),
                    metadata);
            }
        }

        if (stored is null) return null;
        var sources = new List<SourceReference>();
        await using (var sourceCommand = c.CreateCommand())
        {
            sourceCommand.Transaction = tx;
            sourceCommand.CommandText = "SELECT * FROM knowledge_revision_sources WHERE revision_id = $revision ORDER BY ordinal";
            sourceCommand.Parameters.AddWithValue("$revision", revisionId);
            await using var reader = await sourceCommand.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var kind = Enum.TryParse<ProvenanceKind>(GetString(reader, "kind"), out var parsedKind)
                    ? parsedKind : ProvenanceKind.Memory;
                var evidence = Enum.TryParse<EvidenceOrigin>(GetString(reader, "evidence_origin"), out var parsedEvidence)
                    ? parsedEvidence : EvidenceOrigin.Extracted;
                sources.Add(new SourceReference(
                    kind,
                    GetString(reader, "title"),
                    GetStringNullable(reader, "locator"),
                    GetStringNullable(reader, "snippet"),
                    GetDoubleNullable(reader, "score"),
                    GetDateTimeNullable(reader, "timestamp"),
                    evidence));
            }
        }

        KnowledgeRevisionDecision? decision = null;
        await using (var decisionCommand = c.CreateCommand())
        {
            decisionCommand.Transaction = tx;
            decisionCommand.CommandText = @"
                SELECT * FROM knowledge_revision_decisions
                WHERE revision_id = $revision
                ORDER BY recorded_at DESC LIMIT 1";
            decisionCommand.Parameters.AddWithValue("$revision", revisionId);
            await using var reader = await decisionCommand.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                decision = new KnowledgeRevisionDecision(
                    GetString(reader, "kind"), GetString(reader, "actor"), GetString(reader, "reason"),
                    SqliteDateTime.Parse(GetString(reader, "recorded_at")), GetString(reader, "decision_id"));
            }
        }

        return stored with
        {
            Public = stored.Public with { SourceReferences = sources, Decision = decision }
        };
    }

    /// <summary>
    /// Legacy fixture adapter. Production composition exposes the read
    /// projection and <see cref="IKnowledgeRevisionStore"/> separately; this
    /// upsert remains internal so old database fixtures can be exercised while
    /// they are lazily assigned one initial revision.
    /// </summary>
    internal async Task SaveAsync(Memory memory, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        memory.UpdatedAt = DateTime.UtcNow;
        var tagsJson = JsonSerializer.Serialize(NormalizeTags(memory.Tags));
        var relationships = KnowledgeRelationshipSemantics.Normalize(memory.Relationships, memory.RelatedMemoryIds);
        memory.Relationships = relationships;
        memory.RelatedMemoryIds = relationships
            .Where(r => r.Kind == KnowledgeRelationshipKind.RelatedTo && r.Target.Kind == KnowledgeEntityKind.Memory)
            .Select(r => r.Target.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var relationshipsJson = JsonSerializer.Serialize(memory.RelatedMemoryIds);
        var typedRelationshipsJson = JsonSerializer.Serialize(relationships);
        var sourceJson = memory.Source is null ? null : JsonSerializer.Serialize(memory.Source);

        // Embedding is a recall-quality enhancement, not a correctness
        // requirement: if no embedding model is configured or the call
        // fails, save proceeds with a null blob and COALESCE below keeps
        // whatever embedding (if any) the row already had rather than
        // clobbering it. Bounded by the same query-embed timeout the query path
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
                timeoutCts.CancelAfter(_queryEmbedTimeout);
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
            INSERT INTO memories (id,category,content,created_at,updated_at,source_conversation_id,importance_score,tags_json,is_pinned,is_archived,frequency_count,last_merge_time,expiration_date,relationships_json,typed_relationships_json,is_encrypted,scope,scope_id,title,source_json,embedding,embedding_dim)
            VALUES ($id,$cat,$content,$ca,$ua,$src,$imp,$tags,$pin,$arch,$freq,$merge,$exp,$rel,$typedRel,$enc,$scope,$scopeId,$title,$sourceJson,$embedding,$embeddingDim)
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
                typed_relationships_json=excluded.typed_relationships_json,
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
        cmd.Parameters.AddWithValue("$typedRel", typedRelationshipsJson);
        cmd.Parameters.AddWithValue("$enc", memory.IsEncrypted ? 1 : 0);
        cmd.Parameters.AddWithValue("$scope", memory.Scope.ToString());
        cmd.Parameters.AddWithValue("$scopeId", memory.ScopeId);
        cmd.Parameters.AddWithValue("$title", memory.Title);
        cmd.Parameters.AddWithValue("$sourceJson", sourceJson ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$embedding", (object?)embeddingBlob ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$embeddingDim", (object?)embeddingDim ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
        await UpsertFtsAsync(c, memory, tagsJson, ct);
        await EnsureLegacyAssertionsAsync(c, ct);
        await ExecuteAsync(c, null, @"
            UPDATE knowledge_revisions
            SET status = CASE WHEN $archived = 1 THEN 'Archived' ELSE 'Current' END
            WHERE revision_id = (
                SELECT current_revision_id FROM knowledge_assertions WHERE assertion_id = $id)
              AND status <> 'Disputed'", ct,
            ("$id", memory.Id), ("$archived", memory.IsArchived ? 1 : 0));

        // Backfill runs off the send path (r9 01-send-path-latency.md 1.2): a
        // write is the other trigger point besides the startup pass, so a
        // freshly-created row without its own embedding (e.g. no embedding
        // service configured at save time) still becomes vector-recallable
        // once one is available, without taxing the next chat send.
        if (embeddingBlob is null && _embeddings is not null)
            _ = Task.Run(RunEmbeddingBackfillObservedAsync);
    }

    /// <summary>Legacy fixture adapter for permanent deletion.</summary>
    internal async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var current = await GetCurrentRevisionAsync(id, ct);
        if (current is not null)
            await HardDeleteAsync(id, current.RevisionId, ct);
    }

    public async Task<List<Memory>> SearchAsync(string q, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await EnsureLegacyAssertionsAsync(c, ct);

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
                SELECT m.*, kr.revision_id AS current_revision_id
                FROM memories m
                JOIN memories_fts f ON f.id = m.id
                JOIN knowledge_assertions a ON a.assertion_id = m.id
                JOIN knowledge_revisions kr ON kr.revision_id = a.current_revision_id
                WHERE memories_fts MATCH $q AND m.is_archived = 0
                  AND kr.status = 'Current'
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
            return await ExpandOneHopRelationshipsAsync(c, ApplyRelevanceFloor(ftsResults), ct);
        }

        var hybridResults = await HybridRerankAsync(c, q, ftsResults, ct);
        return await ExpandOneHopRelationshipsAsync(c, ApplyRelevanceFloor(hybridResults), ct);
    }

    private static List<Memory> ApplyRelevanceFloor(IEnumerable<Memory> candidates) =>
        candidates
            .Where(memory => memory.IsPinned || memory.RelevanceScore is >= MinimumRecallRelevance)
            .ToList();

    /// <summary>
    /// Adds direct relationship targets after the normal lexical/vector search
    /// has ranked its candidates. It does not discover paths, re-query the
    /// index, or let relationship information replace the primary retrieval
    /// score. A superseded assertion is hidden from ordinary recall while its
    /// direct replacement can be considered with a discounted source score.
    /// </summary>
    private static async Task<List<Memory>> ExpandOneHopRelationshipsAsync(
        SqliteConnection c,
        List<Memory> primaryResults,
        CancellationToken ct)
    {
        if (primaryResults.Count == 0)
            return primaryResults;

        var primaryById = primaryResults.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var expansions = new Dictionary<string, (Memory Source, KnowledgeRelationship Relationship)>(StringComparer.Ordinal);
        foreach (var memory in primaryResults)
        {
            foreach (var relationship in memory.Relationships.Where(KnowledgeRelationshipSemantics.IsOneHopExpandable))
            {
                if (string.Equals(memory.Id, relationship.Target.Id, StringComparison.Ordinal)
                    || primaryById.ContainsKey(relationship.Target.Id)
                    || expansions.ContainsKey(relationship.Target.Id))
                    continue;

                expansions.Add(relationship.Target.Id, (memory, relationship));
            }
        }

        var now = DateTime.UtcNow;
        var expanded = new List<Memory>();
        if (expansions.Count > 0)
        {
            var ids = expansions.Keys.ToList();
            var parameterNames = ids.Select((_, i) => $"$related{i}").ToList();
            var cmd = c.CreateCommand();
            cmd.CommandText = $@"
                SELECT m.*, r.revision_id AS current_revision_id
                FROM memories m
                JOIN knowledge_assertions a ON a.assertion_id = m.id
                JOIN knowledge_revisions r ON r.revision_id = a.current_revision_id
                WHERE m.id IN ({string.Join(",", parameterNames)})
                  AND m.is_archived = 0
                  AND r.status = 'Current'
                  AND (m.is_pinned = 1 OR m.expiration_date IS NULL OR m.expiration_date > $now)";
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            for (var i = 0; i < ids.Count; i++)
                cmd.Parameters.AddWithValue(parameterNames[i], ids[i]);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var related = Map(reader);
                var (source, relationship) = expansions[related.Id];
                related.RelevanceScore = Math.Max(0.0, (source.RelevanceScore ?? 0.0) * 0.5);
                related.RetrievedViaRelationship = new RelationshipRetrieval(
                    source.Id,
                    string.IsNullOrWhiteSpace(source.Title) ? source.Content : source.Title,
                    relationship.Kind,
                    relationship.Evidence);
                expanded.Add(related);
            }
        }

        var currentPrimary = primaryResults.Where(m => !KnowledgeRelationshipSemantics.IsSuperseded(m));
        return ApplyRelevanceFloor(currentPrimary
            .Concat(expanded)
            .OrderByDescending(m => m.IsPinned)
            .ThenByDescending(m => m.RelevanceScore)
            .Take(100)
            .ToList());
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
            timeoutCts.CancelAfter(_queryEmbedTimeout);
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
        cmd.CommandText = @"
            SELECT m.id, m.embedding
            FROM memories m
            JOIN knowledge_assertions a ON a.assertion_id = m.id
            JOIN knowledge_revisions r ON r.revision_id = a.current_revision_id
            WHERE m.is_archived = 0 AND r.status = 'Current'
              AND m.embedding IS NOT NULL
              AND (m.is_pinned = 1 OR m.expiration_date IS NULL OR m.expiration_date > $now)";
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
            hydrateCmd.CommandText = $@"
                SELECT m.*, r.revision_id AS current_revision_id
                FROM memories m
                JOIN knowledge_assertions a ON a.assertion_id = m.id
                JOIN knowledge_revisions r ON r.revision_id = a.current_revision_id
                WHERE r.status = 'Current' AND m.id IN ({string.Join(",", paramNames)})";
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

        IResourceAdmissionLease? lease = null;
        try
        {
            lease = await AcquireBackfillLeaseAsync("rag.memory-backfill", nameof(MemoryStore), ct);
            await EnsureInitializedAsync(ct);
            await using var c = new SqliteConnection(Cs);
            await c.OpenAsync(ct);
            await EnsureLegacyAssertionsAsync(c, ct);

            var pending = new List<(string Id, string Content)>();
            var select = c.CreateCommand();
            select.CommandText = @"
                SELECT m.id, m.content
                FROM memories m
                JOIN knowledge_assertions a ON a.assertion_id = m.id
                JOIN knowledge_revisions r ON r.revision_id = a.current_revision_id
                WHERE m.embedding IS NULL AND m.is_archived = 0 AND r.status = 'Current'
                  AND length(m.content) > 0 LIMIT 200";
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
            if (lease is not null && !lease.IsReleased)
                await lease.ReleaseAsync("memory embedding backfill completed");
            _backfillGate.Release();
        }
    }

    private async Task RunEmbeddingBackfillObservedAsync()
    {
        try
        {
            await RunEmbeddingBackfillAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _runtimeLogs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Warning,
                RuntimeLogCategory.Rag,
                $"Embedding backfill deferred: {ex.GetType().Name}: {ex.Message}"));
        }
    }

    private async Task<IResourceAdmissionLease?> AcquireBackfillLeaseAsync(
        string consumerId,
        string lifecycleService,
        CancellationToken ct)
    {
        if (_resourceCoordinator is null)
            return null;
        _resourceCoordinator.RegisterConsumer(
            ResourceAllocationFactory.EmbeddingBackfillConsumer(consumerId, lifecycleService));
        return await _resourceCoordinator.AcquireAsync(
            new ResourceAdmissionRequest(
                consumerId,
                ResourceAllocationFactory.EmbeddingBackfillProposal(consumerId),
                callerId: $"{consumerId}.start",
                allowUnknown: true), ct);
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
        await EnsureLegacyAssertionsAsync(c, ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1)
            FROM memories m
            JOIN knowledge_assertions a ON a.assertion_id = m.id
            JOIN knowledge_revisions r ON r.revision_id = a.current_revision_id
            WHERE r.status = 'Current' AND m.embedding IS NOT NULL
              AND length(m.embedding) != $bytes";
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
        await EnsureLegacyAssertionsAsync(c, ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            UPDATE memories
            SET embedding = NULL, embedding_dim = NULL
            WHERE id IN (
                SELECT m.id
                FROM memories m
                JOIN knowledge_assertions a ON a.assertion_id = m.id
                JOIN knowledge_revisions r ON r.revision_id = a.current_revision_id
                WHERE r.status = 'Current' AND m.embedding IS NOT NULL
                  AND length(m.embedding) != $bytes)";
        cmd.Parameters.AddWithValue("$bytes", currentDim.Value * sizeof(float));
        var cleared = await cmd.ExecuteNonQueryAsync(ct);

        if (cleared > 0)
            _ = Task.Run(RunEmbeddingBackfillObservedAsync);

        return cleared;
    }

    private async Task<int?> ProbeCurrentDimensionAsync(CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_queryEmbedTimeout);
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
        await EnsureLegacyAssertionsAsync(c, ct);
        var now = DateTime.UtcNow.ToString("O");
        foreach (var id in idList)
        {
            var cmd = c.CreateCommand();
            cmd.CommandText = @"
                UPDATE memories
                SET recall_count = recall_count + 1, last_recalled_at = $now
                WHERE id IN (
                    SELECT m.id
                    FROM memories m
                    JOIN knowledge_assertions a ON a.assertion_id = m.id
                    JOIN knowledge_revisions r ON r.revision_id = a.current_revision_id
                    WHERE r.status = 'Current' AND m.id = $id)";
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
        await EnsureLegacyAssertionsAsync(c, ct);

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
            cmd.CommandText = @"
                UPDATE memories SET is_archived = 1, updated_at = $ua WHERE id = $id;
                UPDATE knowledge_revisions
                SET status = 'Archived'
                WHERE revision_id = (SELECT current_revision_id FROM knowledge_assertions WHERE assertion_id = $id)";
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
        await EnsureLegacyAssertionsAsync(c, ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = CurrentProjectionSql("AND m.importance_score >= $score AND m.is_archived = 0 ORDER BY m.importance_score DESC, m.updated_at DESC");
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
        await EnsureLegacyAssertionsAsync(c, ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = CurrentProjectionSql("AND m.is_archived = 0 ORDER BY m.updated_at DESC LIMIT $limit");
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
        await EnsureLegacyAssertionsAsync(c, ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = CurrentProjectionSql("AND m.source_conversation_id = $src AND m.is_archived = 0 ORDER BY m.updated_at DESC LIMIT $limit");
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
        await EnsureLegacyAssertionsAsync(c, ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = includeArchived
            ? "SELECT COUNT(1) FROM memories m JOIN knowledge_assertions a ON a.assertion_id = m.id JOIN knowledge_revisions r ON r.revision_id = a.current_revision_id WHERE r.status IN ('Current', 'Archived') AND m.source_conversation_id = $src"
            : "SELECT COUNT(1) FROM memories m JOIN knowledge_assertions a ON a.assertion_id = m.id JOIN knowledge_revisions r ON r.revision_id = a.current_revision_id WHERE r.status = 'Current' AND m.source_conversation_id = $src AND m.is_archived = 0";
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
        await EnsureLegacyAssertionsAsync(c, ct);
        var cmd = c.CreateCommand();

        // build parameter list: $p0, $p1, ...
        var paramNames = ids.Select((id, idx) => "$p" + idx).ToList();
        var inClause = string.Join(',', paramNames);
        cmd.CommandText = includeArchived
            ? $"SELECT m.source_conversation_id, COUNT(1) as cnt FROM memories m JOIN knowledge_assertions a ON a.assertion_id = m.id JOIN knowledge_revisions r ON r.revision_id = a.current_revision_id WHERE r.status IN ('Current', 'Archived') AND m.source_conversation_id IN ({inClause}) GROUP BY m.source_conversation_id"
            : $"SELECT m.source_conversation_id, COUNT(1) as cnt FROM memories m JOIN knowledge_assertions a ON a.assertion_id = m.id JOIN knowledge_revisions r ON r.revision_id = a.current_revision_id WHERE r.status = 'Current' AND m.source_conversation_id IN ({inClause}) AND m.is_archived = 0 GROUP BY m.source_conversation_id";

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
        CancellationToken ct,
        SqliteTransaction? tx = null)
    {
        await using var delete = c.CreateCommand();
        delete.Transaction = tx;
        delete.CommandText = "DELETE FROM memories_fts WHERE id = $id";
        delete.Parameters.AddWithValue("$id", memory.Id);
        await delete.ExecuteNonQueryAsync(ct);

        await using var insert = c.CreateCommand();
        insert.Transaction = tx;
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
        cmd.CommandText = CurrentProjectionSql("AND (m.content LIKE $q OR m.category LIKE $q OR m.tags_json LIKE $q) AND m.is_archived = 0 ORDER BY m.is_pinned DESC, m.importance_score DESC, m.updated_at DESC LIMIT 100");
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
        var legacyRelatedIds = DeserializeRelationships(GetString(r, "relationships_json", "[]"));
        var relationships = KnowledgeRelationshipSemantics.Normalize(
            DeserializeTypedRelationships(GetString(r, "typed_relationships_json", "[]")),
            legacyRelatedIds);
        return new Memory
        {
            Id = GetString(r, "id"),
            RevisionId = GetStringNullable(r, "current_revision_id"),
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
            RelatedMemoryIds = legacyRelatedIds,
            Relationships = relationships,
            IsEncrypted = GetInt(r, "is_encrypted") != 0,
            Scope = Enum.TryParse<MemoryScope>(GetString(r, "scope", "Global"), out var scope) ? scope : MemoryScope.Global,
            ScopeId = GetString(r, "scope_id"),
            Title = GetString(r, "title"),
            RecallCount = GetInt(r, "recall_count"),
            LastRecalledAt = GetDateTimeNullable(r, "last_recalled_at")
        };
    }

    private static List<string> DeserializeRelationships(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<KnowledgeRelationship> DeserializeTypedRelationships(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<KnowledgeRelationship>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
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

    private static double? GetDoubleNullable(SqliteDataReader r, string name)
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? null : r.GetDouble(ordinal);
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

    private static string Bound(string value, int maximum) =>
        (value ?? string.Empty).Trim()[..Math.Min((value ?? string.Empty).Trim().Length, maximum)];

    private static string? BoundNullable(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? null : Bound(value, maximum);

    private sealed record StoredRevision(
        KnowledgeAssertionRevision Public,
        MemoryRevisionMetadata Metadata);

    private sealed record MemoryRevisionMetadata(
        string Title,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        string? SourceConversationId,
        double ImportanceScore,
        List<string> Tags,
        bool IsPinned,
        bool IsArchived,
        int FrequencyCount,
        DateTime? LastMergeTime,
        DateTime? ExpirationDate,
        List<string> RelatedMemoryIds,
        List<KnowledgeRelationship> Relationships,
        bool IsEncrypted,
        int RecallCount,
        DateTime? LastRecalledAt)
    {
        public static MemoryRevisionMetadata Empty => new(
            string.Empty, DateTime.UtcNow, DateTime.UtcNow, null, 0.5, [], false, false, 1,
            null, null, [], [], false, 0, null);

        public static MemoryRevisionMetadata FromMemory(Memory memory) => new(
            memory.Title,
            memory.CreatedAt,
            memory.UpdatedAt,
            memory.SourceConversationId,
            memory.ImportanceScore,
            NormalizeTags(memory.Tags),
            memory.IsPinned,
            memory.IsArchived,
            memory.FrequencyCount,
            memory.LastMergeTime,
            memory.ExpirationDate,
            memory.RelatedMemoryIds.ToList(),
            memory.Relationships.ToList(),
            memory.IsEncrypted,
            memory.RecallCount,
            memory.LastRecalledAt);

        public Memory ToMemory(
            string id,
            string content,
            MemoryScope scope,
            string scopeId,
            string category,
            DateTime createdAt,
            DateTime updatedAt)
        {
            var relationships = KnowledgeRelationshipSemantics.Normalize(Relationships, RelatedMemoryIds);
            return new Memory
            {
                Id = id,
                Scope = scope,
                ScopeId = scopeId,
                Title = Title,
                Category = category,
                Content = content,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
                SourceConversationId = SourceConversationId,
                ImportanceScore = ImportanceScore,
                Tags = NormalizeTags(Tags),
                IsPinned = IsPinned,
                IsArchived = IsArchived,
                FrequencyCount = FrequencyCount,
                LastMergeTime = LastMergeTime,
                ExpirationDate = ExpirationDate,
                RelatedMemoryIds = relationships
                    .Where(r => r.Kind == KnowledgeRelationshipKind.RelatedTo && r.Target.Kind == KnowledgeEntityKind.Memory)
                    .Select(r => r.Target.Id)
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                Relationships = relationships,
                IsEncrypted = IsEncrypted,
                RecallCount = RecallCount,
                LastRecalledAt = LastRecalledAt
            };
        }
    }
}
