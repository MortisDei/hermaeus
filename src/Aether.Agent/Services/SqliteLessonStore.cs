using System.Globalization;
using System.Text.Json;
using Aether.Agent.Models;
using Aether.Core.Services;
using Microsoft.Data.Sqlite;

namespace Aether.Agent.Services;

/// <summary>
/// SQLite-backed <see cref="ILessonStore"/>. Rebuildable index-style store
/// (agent/lessons.db under the data root), one row per (scope, scope_id,
/// signature) dedupe key.
/// </summary>
public sealed class SqliteLessonStore : ILessonStore
{
    private const int SchemaVersion = 2;
    private const double ConfidenceFloor = 0.2;
    private const double MaxConfidence = 0.95;
    private const double MaxStatedConfidence = 0.5;
    private const int MaxSourceTaskIds = 10;

    private readonly ISettingsService _settings;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private string _initializedPath = string.Empty;

    public SqliteLessonStore(ISettingsService settings)
    {
        _settings = settings;
    }

    private string AgentRoot
    {
        get
        {
            var configured = _settings.Settings.DataManagement.DataRootDirectory?.Trim();
            var root = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether")
                : Path.GetFullPath(configured);
            return Path.Combine(root, "agent");
        }
    }

    private string DbPath => Path.Combine(AgentRoot, "lessons.db");
    private string Cs => $"Data Source={DbPath}";

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var path = DbPath;
        if (_initializedPath == path && File.Exists(path)) return;

        await _initGate.WaitAsync(ct);
        try
        {
            if (_initializedPath == path && File.Exists(path)) return;

            Directory.CreateDirectory(AgentRoot);
            await using var c = new SqliteConnection(Cs);
            await c.OpenAsync(ct);
            await using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS agent_lessons (
                        id TEXT PRIMARY KEY,
                        scope TEXT NOT NULL,
                        scope_id TEXT NOT NULL DEFAULT '',
                        kind TEXT NOT NULL,
                        signature TEXT NOT NULL,
                        claim TEXT NOT NULL,
                        guidance TEXT NOT NULL,
                        outcome TEXT NOT NULL,
                        confidence REAL NOT NULL DEFAULT 0.3,
                        evidence_count INTEGER NOT NULL DEFAULT 1,
                        created_at TEXT NOT NULL,
                        updated_at TEXT NOT NULL,
                        last_confirmed_at TEXT NOT NULL,
                        status TEXT NOT NULL DEFAULT 'Active',
                        is_pinned INTEGER NOT NULL DEFAULT 0,
                        source_task_ids_json TEXT NOT NULL DEFAULT '[]'
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS idx_agent_lessons_dedupe ON agent_lessons(scope, scope_id, signature);
                    CREATE INDEX IF NOT EXISTS idx_agent_lessons_scope ON agent_lessons(scope, scope_id, status, confidence DESC);";
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await SqliteMigrationRunner.ApplyAsync(c, "agent_lessons", SchemaVersion,
            [
                new SqliteMigration(1, (_, _) => Task.FromResult(false)),
                new SqliteMigration(2, MigrateOutcomeOutOfSignatureAsync)
            ], ct);

            _initializedPath = path;
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>
    /// v1 baked the outcome into Command/Patch/Approval signatures (e.g.
    /// "command:dotnet test:fail:CS0246"), which made the store's
    /// contradiction logic unreachable: a command that failed then
    /// succeeded created two permanently-separate rows instead of one row
    /// that could reinforce or contradict itself (docs/review/02-lessons-v2.md L1).
    /// This strips the outcome suffix back out; on a dedupe-key collision
    /// (both an "ok" and a "fail" row existed for the same subject) it keeps
    /// the row with the most evidence (ties: most recently updated) and
    /// drops the rest, since this is a rebuildable index store where a
    /// lossy collapse is acceptable.
    /// </summary>
    private static async Task<bool> MigrateOutcomeOutOfSignatureAsync(SqliteConnection c, CancellationToken ct)
    {
        var rows = new List<(string Id, string Scope, string ScopeId, string Kind, string Signature, int EvidenceCount, string UpdatedAt)>();
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id, scope, scope_id, kind, signature, evidence_count, updated_at FROM agent_lessons";
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                rows.Add((rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.GetInt32(5), rd.GetString(6)));
        }

        var changed = false;
        var groups = rows
            .Select(r => (Row: r, NewSignature: StripOutcomeSuffix(r.Kind, r.Signature)))
            .Where(x => x.NewSignature is not null)
            .GroupBy(x => (x.Row.Scope, x.Row.ScopeId, x.Row.Kind, NewSignature: x.NewSignature!));

        foreach (var group in groups)
        {
            var ordered = group.OrderByDescending(x => x.Row.EvidenceCount).ThenByDescending(x => x.Row.UpdatedAt).ToList();
            var keep = ordered[0].Row;

            foreach (var drop in ordered.Skip(1))
            {
                await using var del = c.CreateCommand();
                del.CommandText = "DELETE FROM agent_lessons WHERE id = $id";
                del.Parameters.AddWithValue("$id", drop.Row.Id);
                await del.ExecuteNonQueryAsync(ct);
                changed = true;
            }

            if (!string.Equals(keep.Signature, group.Key.NewSignature, StringComparison.Ordinal))
            {
                await using var upd = c.CreateCommand();
                upd.CommandText = "UPDATE agent_lessons SET signature = $sig WHERE id = $id";
                upd.Parameters.AddWithValue("$sig", group.Key.NewSignature);
                upd.Parameters.AddWithValue("$id", keep.Id);
                await upd.ExecuteNonQueryAsync(ct);
                changed = true;
            }
        }

        return changed;
    }

    private static string? StripOutcomeSuffix(string kind, string signature) => kind switch
    {
        "Command" => System.Text.RegularExpressions.Regex.Match(signature, "^command:(.*):(ok|fail):[^:]*$") is { Success: true } m
            ? $"command:{m.Groups[1].Value}"
            : null,
        "Patch" => System.Text.RegularExpressions.Regex.Match(signature, "^patch:(apply_draft_patch|edit_file|create_file):(.*):(ok|fail)$") is { Success: true } m
            ? $"patch:{m.Groups[1].Value}:{m.Groups[2].Value}"
            : null,
        "Approval" => System.Text.RegularExpressions.Regex.Match(signature, "^approval:(.*):rejected$") is { Success: true } m
            ? $"approval:{m.Groups[1].Value}"
            : null,
        _ => null
    };

    public async Task<AgentLesson> RecordEvidenceAsync(AgentLessonEvidence evidence, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);

        var existing = await FindBySignatureAsync(c, evidence.Scope, evidence.ScopeId, evidence.Signature, ct);
        var now = DateTime.UtcNow;

        if (existing is null)
        {
            if (evidence.CounterOnly)
            {
                // Nothing yet to counter; counter-only evidence never
                // originates a lesson on its own (see AgentLessonEvidence.CounterOnly).
                return new AgentLesson
                {
                    Scope = evidence.Scope,
                    ScopeId = evidence.ScopeId,
                    Kind = evidence.Kind,
                    Signature = evidence.Signature,
                    Claim = evidence.Claim,
                    Guidance = evidence.Guidance,
                    Outcome = evidence.Outcome,
                    EvidenceCount = 0
                };
            }

            var lesson = new AgentLesson
            {
                Scope = evidence.Scope,
                ScopeId = evidence.ScopeId,
                Kind = evidence.Kind,
                Signature = evidence.Signature,
                Claim = evidence.Claim,
                Guidance = evidence.Guidance,
                Outcome = evidence.Outcome,
                Confidence = InitialConfidence(evidence.Kind),
                EvidenceCount = 1,
                CreatedAt = now,
                UpdatedAt = now,
                LastConfirmedAt = now,
                Status = AgentLessonStatus.Active,
                SourceTaskIds = evidence.SourceTaskId is null ? [] : [evidence.SourceTaskId]
            };
            await InsertAsync(c, lesson, ct);
            return lesson;
        }

        if (existing.IsPinned)
            return existing; // Manual override: evidence no longer moves a pinned lesson.

        if (existing.Outcome == evidence.Outcome)
        {
            // Reinforcement: same claim confirmed again.
            existing.EvidenceCount++;
            existing.Confidence = ConfidenceCurve(existing.EvidenceCount, evidence.Kind);
            existing.Claim = evidence.Claim;
            existing.Guidance = evidence.Guidance;
            existing.LastConfirmedAt = now;
            existing.Status = AgentLessonStatus.Active; // Reviving evidence un-retires a lesson.
        }
        else
        {
            // Contradiction: what used to hold no longer does, or vice versa.
            existing.Confidence = Math.Max(0, existing.Confidence * 0.3);
            if (existing.Confidence < ConfidenceFloor)
            {
                existing.Status = AgentLessonStatus.Retired;
                // The old claim has been thoroughly contradicted: flip to
                // the new outcome and restart its evidence count so further
                // matching evidence reinforces (and can revive) the lesson
                // under its current claim, instead of every future match
                // being treated as yet another contradiction of the stale
                // original outcome forever.
                existing.Outcome = evidence.Outcome;
                existing.EvidenceCount = 1;
                existing.Confidence = InitialConfidence(evidence.Kind);
                existing.Claim = evidence.Claim;
                existing.Guidance = evidence.Guidance;
            }
        }

        existing.UpdatedAt = now;
        if (evidence.SourceTaskId is not null)
        {
            existing.SourceTaskIds.Remove(evidence.SourceTaskId);
            existing.SourceTaskIds.Insert(0, evidence.SourceTaskId);
            if (existing.SourceTaskIds.Count > MaxSourceTaskIds)
                existing.SourceTaskIds = existing.SourceTaskIds[..MaxSourceTaskIds];
        }

        await UpdateRowAsync(c, existing, ct);
        return existing;
    }

    public async Task ConfirmAsync(IReadOnlyList<string> lessonIds, string sourceTaskId, CancellationToken ct = default)
    {
        if (lessonIds.Count == 0) return;
        await InitializeAsync(ct);

        foreach (var id in lessonIds.Distinct(StringComparer.Ordinal))
        {
            var lesson = await GetByIdAsync(id, ct);
            if (lesson is null || lesson.IsPinned || lesson.Status != AgentLessonStatus.Active)
                continue;

            lesson.EvidenceCount++;
            lesson.Confidence = ConfidenceCurve(lesson.EvidenceCount, lesson.Kind);
            var now = DateTime.UtcNow;
            lesson.UpdatedAt = now;
            lesson.LastConfirmedAt = now;
            if (!string.IsNullOrEmpty(sourceTaskId))
            {
                lesson.SourceTaskIds.Remove(sourceTaskId);
                lesson.SourceTaskIds.Insert(0, sourceTaskId);
                if (lesson.SourceTaskIds.Count > MaxSourceTaskIds)
                    lesson.SourceTaskIds = lesson.SourceTaskIds[..MaxSourceTaskIds];
            }

            await using var c = new SqliteConnection(Cs);
            await c.OpenAsync(ct);
            await UpdateRowAsync(c, lesson, ct);
        }
    }

    private static double InitialConfidence(AgentLessonKind kind) => kind == AgentLessonKind.Stated ? 0.25 : 0.3;

    private static double ConfidenceCurve(int evidenceCount, AgentLessonKind kind)
    {
        var raw = 1.0 - (1.0 / (1 + evidenceCount));
        var cap = kind == AgentLessonKind.Stated ? MaxStatedConfidence : MaxConfidence;
        return Math.Min(raw, cap);
    }

    public async Task<IReadOnlyList<AgentLesson>> ListRelevantAsync(string? workspaceScopeId, bool includeRetired, int limit, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);

        var statusFilter = includeRetired ? "" : " AND status = 'Active'";
        var cmd = c.CreateCommand();
        if (string.IsNullOrWhiteSpace(workspaceScopeId))
        {
            cmd.CommandText = $@"SELECT * FROM agent_lessons WHERE scope = 'Global'{statusFilter}
                ORDER BY is_pinned DESC, confidence DESC, updated_at DESC LIMIT $limit";
        }
        else
        {
            cmd.CommandText = $@"SELECT * FROM agent_lessons WHERE (scope = 'Global' OR (scope = 'Workspace' AND scope_id = $scopeId)){statusFilter}
                ORDER BY is_pinned DESC, confidence DESC, updated_at DESC LIMIT $limit";
            cmd.Parameters.AddWithValue("$scopeId", workspaceScopeId);
        }
        cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        var results = new List<AgentLesson>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) results.Add(Map(rd));
        return results;
    }

    public async Task<IReadOnlyList<AgentLesson>> ListAllAsync(bool includeRetired, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = includeRetired
            ? "SELECT * FROM agent_lessons ORDER BY is_pinned DESC, confidence DESC, updated_at DESC"
            : "SELECT * FROM agent_lessons WHERE status = 'Active' ORDER BY is_pinned DESC, confidence DESC, updated_at DESC";
        var results = new List<AgentLesson>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) results.Add(Map(rd));
        return results;
    }

    public async Task<AgentLesson?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM agent_lessons WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct) ? Map(rd) : null;
    }

    public async Task UpdateAsync(string id, string claim, string guidance, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE agent_lessons SET claim = $claim, guidance = $guidance, updated_at = $now WHERE id = $id";
        cmd.Parameters.AddWithValue("$claim", claim);
        cmd.Parameters.AddWithValue("$guidance", guidance);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetPinnedAsync(string id, bool pinned, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE agent_lessons SET is_pinned = $pinned, updated_at = $now WHERE id = $id";
        cmd.Parameters.AddWithValue("$pinned", pinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetStatusAsync(string id, AgentLessonStatus status, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE agent_lessons SET status = $status, updated_at = $now WHERE id = $id";
        cmd.Parameters.AddWithValue("$status", status.ToString());
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM agent_lessons WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<AgentLesson?> FindBySignatureAsync(SqliteConnection c, AgentLessonScope scope, string scopeId, string signature, CancellationToken ct)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM agent_lessons WHERE scope = $scope AND scope_id = $scopeId AND signature = $sig";
        cmd.Parameters.AddWithValue("$scope", scope.ToString());
        cmd.Parameters.AddWithValue("$scopeId", scopeId ?? string.Empty);
        cmd.Parameters.AddWithValue("$sig", signature);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct) ? Map(rd) : null;
    }

    private static async Task InsertAsync(SqliteConnection c, AgentLesson lesson, CancellationToken ct)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO agent_lessons (id, scope, scope_id, kind, signature, claim, guidance, outcome, confidence, evidence_count, created_at, updated_at, last_confirmed_at, status, is_pinned, source_task_ids_json)
            VALUES ($id, $scope, $scopeId, $kind, $sig, $claim, $guidance, $outcome, $confidence, $count, $created, $updated, $confirmed, $status, $pinned, $tasks)";
        BindParameters(cmd, lesson);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpdateRowAsync(SqliteConnection c, AgentLesson lesson, CancellationToken ct)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            UPDATE agent_lessons SET
                claim = $claim, guidance = $guidance, outcome = $outcome, confidence = $confidence,
                evidence_count = $count, updated_at = $updated, last_confirmed_at = $confirmed,
                status = $status, source_task_ids_json = $tasks
            WHERE id = $id";
        BindParameters(cmd, lesson);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void BindParameters(SqliteCommand cmd, AgentLesson lesson)
    {
        cmd.Parameters.AddWithValue("$id", lesson.Id);
        cmd.Parameters.AddWithValue("$scope", lesson.Scope.ToString());
        cmd.Parameters.AddWithValue("$scopeId", lesson.ScopeId);
        cmd.Parameters.AddWithValue("$kind", lesson.Kind.ToString());
        cmd.Parameters.AddWithValue("$sig", lesson.Signature);
        cmd.Parameters.AddWithValue("$claim", lesson.Claim);
        cmd.Parameters.AddWithValue("$guidance", lesson.Guidance);
        cmd.Parameters.AddWithValue("$outcome", lesson.Outcome.ToString());
        cmd.Parameters.AddWithValue("$confidence", lesson.Confidence);
        cmd.Parameters.AddWithValue("$count", lesson.EvidenceCount);
        cmd.Parameters.AddWithValue("$created", lesson.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", lesson.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$confirmed", lesson.LastConfirmedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$status", lesson.Status.ToString());
        cmd.Parameters.AddWithValue("$pinned", lesson.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$tasks", JsonSerializer.Serialize(lesson.SourceTaskIds));
    }

    private static AgentLesson Map(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        Scope = Enum.TryParse<AgentLessonScope>(r.GetString(r.GetOrdinal("scope")), out var scope) ? scope : AgentLessonScope.Global,
        ScopeId = r.GetString(r.GetOrdinal("scope_id")),
        Kind = Enum.TryParse<AgentLessonKind>(r.GetString(r.GetOrdinal("kind")), out var kind) ? kind : AgentLessonKind.Task,
        Signature = r.GetString(r.GetOrdinal("signature")),
        Claim = r.GetString(r.GetOrdinal("claim")),
        Guidance = r.GetString(r.GetOrdinal("guidance")),
        Outcome = Enum.TryParse<AgentLessonOutcome>(r.GetString(r.GetOrdinal("outcome")), out var outcome) ? outcome : AgentLessonOutcome.Observation,
        Confidence = r.GetDouble(r.GetOrdinal("confidence")),
        EvidenceCount = r.GetInt32(r.GetOrdinal("evidence_count")),
        CreatedAt = ParseTimestamp(r.GetString(r.GetOrdinal("created_at"))),
        UpdatedAt = ParseTimestamp(r.GetString(r.GetOrdinal("updated_at"))),
        LastConfirmedAt = ParseTimestamp(r.GetString(r.GetOrdinal("last_confirmed_at"))),
        Status = Enum.TryParse<AgentLessonStatus>(r.GetString(r.GetOrdinal("status")), out var status) ? status : AgentLessonStatus.Active,
        IsPinned = r.GetInt32(r.GetOrdinal("is_pinned")) != 0,
        SourceTaskIds = JsonSerializer.Deserialize<List<string>>(r.GetString(r.GetOrdinal("source_task_ids_json"))) ?? []
    };

    /// <summary>
    /// Round-trips the "O"-format UTC strings <see cref="BindParameters"/>
    /// writes without applying local-time conversion, which bare
    /// <see cref="DateTime.Parse(string)"/> does for a string carrying a "Z"
    /// or offset.
    /// </summary>
    private static DateTime ParseTimestamp(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTime.UtcNow;
}
