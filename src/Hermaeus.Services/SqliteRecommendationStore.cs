using System.Text;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Microsoft.Data.Sqlite;

namespace Hermaeus.Services;

/// <summary>
/// Services-owned persistence for reviewable recommendations. Evidence,
/// decisions, and rollback records have separate rows so recommendation prose
/// is never the authority for an apply or undo operation.
/// </summary>
public sealed class SqliteRecommendationStore : IRecommendationStore
{
    private const int SchemaVersion = 1;
    private const int MaximumPayloadBytes = 32 * 1024;
    private const int MaximumRows = 500;
    private readonly RedactionService _redaction;
    private readonly ISettingsService _settings;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private string _initializedPath = string.Empty;

    public SqliteRecommendationStore(ISettingsService settings, RedactionService redaction)
    {
        _settings = settings;
        _redaction = redaction;
    }

    private string DbPath => Path.Combine(SettingsService.ResolveDataRoot(_settings.Settings), "experience.db");
    private string ConnectionString => $"Data Source={DbPath}";

    public Task InitializeAsync(CancellationToken ct = default) => EnsureInitializedAsync(ct);

    public async Task<ConfigurationRecommendation> AddOrGetAsync(
        ConfigurationRecommendation recommendation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        var prepared = Prepare(recommendation);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO configuration_recommendations
                (id,schema_version,kind,target_identity,current_configuration_identity,
                 proposed_patch_domain,proposed_patch_json,proposed_patch_hash,eligibility,
                 rule_id,derivation_version,reason_code,evaluated_at,created_at,expires_at,status)
            VALUES
                ($id,$schema,$kind,$target,$current,$patch_domain,$patch_json,$patch_hash,$eligibility,
                 $rule,$derivation,$reason,$evaluated,$created,$expires,$status)
            ON CONFLICT(kind,target_identity,current_configuration_identity,proposed_patch_hash,derivation_version)
            DO NOTHING
            """;
        Add(insert, "$id", prepared.Id);
        Add(insert, "$schema", prepared.SchemaVersion);
        Add(insert, "$kind", prepared.Kind.ToString());
        Add(insert, "$target", prepared.TargetIdentity);
        Add(insert, "$current", prepared.CurrentConfigurationIdentity);
        Add(insert, "$patch_domain", prepared.ProposedPatch.TargetDomain);
        Add(insert, "$patch_json", prepared.ProposedPatch.CanonicalJson);
        Add(insert, "$patch_hash", prepared.ProposedPatch.Sha256);
        Add(insert, "$eligibility", prepared.Eligibility.ToString());
        Add(insert, "$rule", prepared.RuleId);
        Add(insert, "$derivation", prepared.DerivationVersion);
        Add(insert, "$reason", prepared.ReasonCode);
        Add(insert, "$evaluated", prepared.EvaluatedAtUtc.ToString("O"));
        Add(insert, "$created", prepared.CreatedAtUtc.ToString("O"));
        Add(insert, "$expires", prepared.ExpiresAtUtc?.ToString("O"));
        Add(insert, "$status", prepared.Status.ToString());
        var inserted = await insert.ExecuteNonQueryAsync(ct) == 1;

        var id = prepared.Id;
        if (inserted)
        {
            await InsertChildrenAsync(connection, transaction, prepared, ct);
        }
        else
        {
            var existing = connection.CreateCommand();
            existing.Transaction = transaction;
            existing.CommandText = """
                SELECT id FROM configuration_recommendations
                WHERE kind=$kind AND target_identity=$target
                  AND current_configuration_identity=$current
                  AND proposed_patch_hash=$patch_hash AND derivation_version=$derivation
                """;
            Add(existing, "$kind", prepared.Kind.ToString());
            Add(existing, "$target", prepared.TargetIdentity);
            Add(existing, "$current", prepared.CurrentConfigurationIdentity);
            Add(existing, "$patch_hash", prepared.ProposedPatch.Sha256);
            Add(existing, "$derivation", prepared.DerivationVersion);
            id = Convert.ToString(await existing.ExecuteScalarAsync(ct))
                ?? throw new InvalidOperationException("The existing recommendation identity could not be read.");
        }

        await transaction.CommitAsync(ct);
        return inserted ? prepared : await GetAsync(id, ct)
            ?? throw new InvalidOperationException("The existing recommendation could not be loaded.");
    }

    public async Task<ConfigurationRecommendation?> GetAsync(string id, CancellationToken ct = default)
    {
        ValidateOpaque(id, nameof(id));
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        return await Read(connection, null, id, ct);
    }

    public async Task<IReadOnlyList<ConfigurationRecommendation>> QueryAsync(
        RecommendationQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        var command = connection.CreateCommand();
        var clauses = new List<string>();
        if (query.Kind is { } kind)
        {
            clauses.Add("kind=$kind");
            Add(command, "$kind", kind.ToString());
        }
        AddOptional(command, clauses, "target_identity", "$target", query.TargetIdentity);
        AddOptional(command, clauses, "current_configuration_identity", "$current", query.CurrentConfigurationIdentity);
        if (query.Eligibility is { } eligibility)
        {
            clauses.Add("eligibility=$eligibility");
            Add(command, "$eligibility", eligibility.ToString());
        }
        if (query.Status is { } status)
        {
            clauses.Add("status=$status");
            Add(command, "$status", status.ToString());
        }
        Add(command, "$limit", Math.Clamp(query.Limit, 1, MaximumRows));
        command.CommandText = $"SELECT id FROM configuration_recommendations {(clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses))} ORDER BY created_at DESC LIMIT $limit";

        var ids = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            ids.Add(reader.GetString(0));
        await reader.DisposeAsync();

        var rows = new List<ConfigurationRecommendation>(ids.Count);
        foreach (var id in ids)
        {
            var row = await Read(connection, null, id, ct);
            if (row is not null)
                rows.Add(row);
        }
        return rows;
    }

    public async Task SetStatusAsync(string recommendationId, RecommendationStatus status, CancellationToken ct = default)
    {
        ValidateOpaque(recommendationId, nameof(recommendationId));
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE configuration_recommendations SET status=$status WHERE id=$id";
        Add(command, "$status", status.ToString());
        Add(command, "$id", recommendationId);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new KeyNotFoundException($"Recommendation '{recommendationId}' does not exist.");
    }

    public async Task<RecommendationDecisionRecord> AddDecisionAsync(
        RecommendationDecisionRecord decision,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var prepared = Prepare(decision);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO recommendation_decisions
                (id,recommendation_id,decision,actor,expected_current_configuration_identity,result_code,created_at)
            VALUES ($id,$recommendation,$decision,$actor,$expected,$result,$created)
            """;
        Add(command, "$id", prepared.Id);
        Add(command, "$recommendation", prepared.RecommendationId);
        Add(command, "$decision", prepared.Decision.ToString());
        Add(command, "$actor", prepared.Actor);
        Add(command, "$expected", prepared.ExpectedCurrentConfigurationIdentity);
        Add(command, "$result", prepared.ResultCode);
        Add(command, "$created", prepared.CreatedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
        return prepared;
    }

    public async Task<RecommendationRollbackRecord> AddRollbackAsync(
        RecommendationRollbackRecord rollback,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rollback);
        var prepared = Prepare(rollback);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO recommendation_rollbacks
                (id,recommendation_id,pre_image_json,pre_image_hash,post_apply_configuration_identity,created_at,consumed)
            VALUES ($id,$recommendation,$pre_image,$hash,$post_apply,$created,$consumed)
            """;
        Add(command, "$id", prepared.Id);
        Add(command, "$recommendation", prepared.RecommendationId);
        Add(command, "$pre_image", prepared.PreImageJson);
        Add(command, "$hash", prepared.PreImageHash);
        Add(command, "$post_apply", prepared.PostApplyConfigurationIdentity);
        Add(command, "$created", prepared.CreatedAtUtc.ToString("O"));
        Add(command, "$consumed", prepared.Consumed ? 1 : 0);
        await command.ExecuteNonQueryAsync(ct);
        return prepared;
    }

    public async Task ConsumeRollbackAsync(string rollbackId, CancellationToken ct = default)
    {
        ValidateOpaque(rollbackId, nameof(rollbackId));
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE recommendation_rollbacks SET consumed=1 WHERE id=$id";
        Add(command, "$id", rollbackId);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new KeyNotFoundException($"Recommendation rollback '{rollbackId}' does not exist.");
    }

    public async Task<IReadOnlyList<RecommendationDecisionRecord>> QueryDecisionsAsync(
        string? recommendationId = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        var command = connection.CreateCommand();
        command.CommandText = recommendationId is null
            ? "SELECT id,recommendation_id,decision,actor,expected_current_configuration_identity,result_code,created_at FROM recommendation_decisions ORDER BY created_at DESC LIMIT $limit"
            : "SELECT id,recommendation_id,decision,actor,expected_current_configuration_identity,result_code,created_at FROM recommendation_decisions WHERE recommendation_id=$recommendation ORDER BY created_at DESC LIMIT $limit";
        Add(command, "$limit", MaximumRows);
        if (recommendationId is not null)
            Add(command, "$recommendation", ValidateOpaque(recommendationId, nameof(recommendationId)));
        var rows = new List<RecommendationDecisionRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                Enum.Parse<RecommendationDecisionKind>(reader.GetString(2)),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                ParseUtc(reader.GetString(6))));
        }
        return rows;
    }

    public async Task<IReadOnlyList<RecommendationRollbackRecord>> QueryRollbacksAsync(
        string? recommendationId = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        var command = connection.CreateCommand();
        command.CommandText = recommendationId is null
            ? "SELECT id,recommendation_id,pre_image_json,pre_image_hash,post_apply_configuration_identity,created_at,consumed FROM recommendation_rollbacks ORDER BY created_at DESC LIMIT $limit"
            : "SELECT id,recommendation_id,pre_image_json,pre_image_hash,post_apply_configuration_identity,created_at,consumed FROM recommendation_rollbacks WHERE recommendation_id=$recommendation ORDER BY created_at DESC LIMIT $limit";
        Add(command, "$limit", MaximumRows);
        if (recommendationId is not null)
            Add(command, "$recommendation", ValidateOpaque(recommendationId, nameof(recommendationId)));
        var rows = new List<RecommendationRollbackRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                ParseUtc(reader.GetString(5)),
                reader.GetInt32(6) != 0));
        }
        return rows;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        var path = DbPath;
        if (_initializedPath == path && File.Exists(path))
            return;
        await _initGate.WaitAsync(ct);
        try
        {
            if (_initializedPath == path && File.Exists(path))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync(ct);
            await EnableForeignKeysAsync(connection, ct);
            await SqliteMigrationRunner.ApplyAsync(connection, "recommendations", SchemaVersion,
                [new SqliteMigration(1, CreateSchemaAsync)], ct);
            _initializedPath = path;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private static async Task<bool> CreateSchemaAsync(SqliteConnection connection, CancellationToken ct)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS configuration_recommendations (
                id TEXT PRIMARY KEY,
                schema_version INTEGER NOT NULL,
                kind TEXT NOT NULL,
                target_identity TEXT NOT NULL,
                current_configuration_identity TEXT NOT NULL,
                proposed_patch_domain TEXT NOT NULL,
                proposed_patch_json TEXT NOT NULL,
                proposed_patch_hash TEXT NOT NULL,
                eligibility TEXT NOT NULL,
                rule_id TEXT NOT NULL,
                derivation_version INTEGER NOT NULL,
                reason_code TEXT NOT NULL,
                evaluated_at TEXT NOT NULL,
                created_at TEXT NOT NULL,
                expires_at TEXT,
                status TEXT NOT NULL,
                UNIQUE(kind,target_identity,current_configuration_identity,proposed_patch_hash,derivation_version)
            );
            CREATE TABLE IF NOT EXISTS recommendation_evidence (
                recommendation_id TEXT NOT NULL REFERENCES configuration_recommendations(id) ON DELETE CASCADE,
                ordinal INTEGER NOT NULL,
                evidence_id TEXT NOT NULL,
                evidence_kind TEXT NOT NULL,
                required INTEGER NOT NULL,
                state TEXT NOT NULL,
                observed_at TEXT NOT NULL,
                maximum_age_seconds REAL,
                PRIMARY KEY(recommendation_id,ordinal)
            );
            CREATE TABLE IF NOT EXISTS recommendation_conditions (
                recommendation_id TEXT NOT NULL REFERENCES configuration_recommendations(id) ON DELETE CASCADE,
                ordinal INTEGER NOT NULL,
                code TEXT NOT NULL,
                value TEXT NOT NULL,
                PRIMARY KEY(recommendation_id,ordinal)
            );
            CREATE TABLE IF NOT EXISTS recommendation_tradeoffs (
                recommendation_id TEXT NOT NULL REFERENCES configuration_recommendations(id) ON DELETE CASCADE,
                ordinal INTEGER NOT NULL,
                code TEXT NOT NULL,
                value TEXT NOT NULL,
                PRIMARY KEY(recommendation_id,ordinal)
            );
            CREATE TABLE IF NOT EXISTS recommendation_decisions (
                id TEXT PRIMARY KEY,
                recommendation_id TEXT NOT NULL REFERENCES configuration_recommendations(id) ON DELETE CASCADE,
                decision TEXT NOT NULL,
                actor TEXT NOT NULL,
                expected_current_configuration_identity TEXT,
                result_code TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS recommendation_rollbacks (
                id TEXT PRIMARY KEY,
                recommendation_id TEXT NOT NULL REFERENCES configuration_recommendations(id) ON DELETE CASCADE,
                pre_image_json TEXT NOT NULL,
                pre_image_hash TEXT NOT NULL,
                post_apply_configuration_identity TEXT NOT NULL,
                created_at TEXT NOT NULL,
                consumed INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_recommendations_target
                ON configuration_recommendations(target_identity,created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_recommendation_decisions_recommendation
                ON recommendation_decisions(recommendation_id,created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_recommendation_rollbacks_recommendation
                ON recommendation_rollbacks(recommendation_id,created_at DESC);
            """;
        await command.ExecuteNonQueryAsync(ct);
        return true;
    }

    private async Task InsertChildrenAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ConfigurationRecommendation recommendation,
        CancellationToken ct)
    {
        for (var index = 0; index < recommendation.Evidence.Count; index++)
        {
            var item = recommendation.Evidence[index];
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO recommendation_evidence
                    (recommendation_id,ordinal,evidence_id,evidence_kind,required,state,observed_at,maximum_age_seconds)
                VALUES ($recommendation,$ordinal,$id,$kind,$required,$state,$observed,$maximum_age)
                """;
            Add(command, "$recommendation", recommendation.Id);
            Add(command, "$ordinal", index);
            Add(command, "$id", item.EvidenceId);
            Add(command, "$kind", item.EvidenceKind);
            Add(command, "$required", item.Required ? 1 : 0);
            Add(command, "$state", item.State.ToString());
            Add(command, "$observed", item.ObservedAtUtc.ToString("O"));
            Add(command, "$maximum_age", item.MaximumAge?.TotalSeconds);
            await command.ExecuteNonQueryAsync(ct);
        }
        await InsertKeyValuesAsync(connection, transaction, "recommendation_conditions", recommendation.Id,
            recommendation.Conditions.Select(value => (value.Code, value.Value)), ct);
        await InsertKeyValuesAsync(connection, transaction, "recommendation_tradeoffs", recommendation.Id,
            recommendation.Tradeoffs.Select(value => (value.Code, value.Value)), ct);
    }

    private static async Task InsertKeyValuesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string recommendationId,
        IEnumerable<(string Code, string Value)> values,
        CancellationToken ct)
    {
        var index = 0;
        foreach (var value in values)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {table} (recommendation_id,ordinal,code,value) VALUES ($recommendation,$ordinal,$code,$value)";
            Add(command, "$recommendation", recommendationId);
            Add(command, "$ordinal", index++);
            Add(command, "$code", value.Code);
            Add(command, "$value", value.Value);
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task<ConfigurationRecommendation?> Read(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string id,
        CancellationToken ct)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id,schema_version,kind,target_identity,current_configuration_identity,
                   proposed_patch_domain,proposed_patch_json,proposed_patch_hash,eligibility,
                   rule_id,derivation_version,reason_code,evaluated_at,created_at,expires_at,status
            FROM configuration_recommendations WHERE id=$id
            """;
        Add(command, "$id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        var row = new ConfigurationRecommendation
        {
            Id = reader.GetString(0),
            SchemaVersion = reader.GetInt32(1),
            Kind = Enum.Parse<RecommendationKind>(reader.GetString(2)),
            TargetIdentity = reader.GetString(3),
            CurrentConfigurationIdentity = reader.GetString(4),
            ProposedPatch = new RecommendationPatch
            {
                TargetDomain = reader.GetString(5),
                CanonicalJson = reader.GetString(6),
                Sha256 = reader.GetString(7)
            },
            Eligibility = Enum.Parse<RecommendationEligibility>(reader.GetString(8)),
            RuleId = reader.GetString(9),
            DerivationVersion = reader.GetInt32(10),
            ReasonCode = reader.GetString(11),
            EvaluatedAtUtc = ParseUtc(reader.GetString(12)),
            CreatedAtUtc = ParseUtc(reader.GetString(13)),
            ExpiresAtUtc = reader.IsDBNull(14) ? null : ParseUtc(reader.GetString(14)),
            Status = Enum.Parse<RecommendationStatus>(reader.GetString(15))
        };
        await reader.DisposeAsync();

        row = row with
        {
            Evidence = await ReadEvidenceAsync(connection, id, ct),
            Conditions = await ReadKeyValuesAsync<RecommendationCondition>(connection, "recommendation_conditions", id, ct),
            Tradeoffs = await ReadKeyValuesAsync<RecommendationTradeoff>(connection, "recommendation_tradeoffs", id, ct)
        };
        return row;
    }

    private static async Task<IReadOnlyList<RecommendationEvidenceReference>> ReadEvidenceAsync(
        SqliteConnection connection,
        string recommendationId,
        CancellationToken ct)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT evidence_id,evidence_kind,required,state,observed_at,maximum_age_seconds FROM recommendation_evidence WHERE recommendation_id=$recommendation ORDER BY ordinal";
        Add(command, "$recommendation", recommendationId);
        var rows = new List<RecommendationEvidenceReference>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) != 0,
                Enum.Parse<CapabilityState>(reader.GetString(3)), ParseUtc(reader.GetString(4)),
                reader.IsDBNull(5) ? null : TimeSpan.FromSeconds(reader.GetDouble(5))));
        return rows;
    }

    private static async Task<IReadOnlyList<T>> ReadKeyValuesAsync<T>(
        SqliteConnection connection,
        string table,
        string recommendationId,
        CancellationToken ct)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"SELECT code,value FROM {table} WHERE recommendation_id=$recommendation ORDER BY ordinal";
        Add(command, "$recommendation", recommendationId);
        var values = new List<(string Code, string Value)>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            values.Add((reader.GetString(0), reader.GetString(1)));

        if (typeof(T) == typeof(RecommendationCondition))
            return values.Select(value => (T)(object)new RecommendationCondition(value.Code, value.Value)).ToArray();
        return values.Select(value => (T)(object)new RecommendationTradeoff(value.Code, value.Value)).ToArray();
    }

    private ConfigurationRecommendation Prepare(ConfigurationRecommendation recommendation)
    {
        ValidateOpaque(recommendation.Id, nameof(recommendation.Id));
        ValidateOpaque(recommendation.TargetIdentity, nameof(recommendation.TargetIdentity));
        ValidateOpaque(recommendation.CurrentConfigurationIdentity, nameof(recommendation.CurrentConfigurationIdentity));
        ValidateOpaque(recommendation.RuleId, nameof(recommendation.RuleId));
        ValidateOpaque(recommendation.ReasonCode, nameof(recommendation.ReasonCode));
        var patch = RecommendationPatch.Create(
            recommendation.ProposedPatch.TargetDomain,
            recommendation.ProposedPatch.CanonicalJson);
        if (!string.Equals(patch.Sha256, recommendation.ProposedPatch.Sha256, StringComparison.Ordinal))
            throw new InvalidOperationException("Recommendation patch identity does not match its canonical content.");
        if (Encoding.UTF8.GetByteCount(patch.CanonicalJson) > MaximumPayloadBytes)
            throw new InvalidOperationException("Recommendation patch exceeds the persistence bound.");
        var evidence = recommendation.Evidence.Select(Prepare).ToArray();
        var conditions = recommendation.Conditions
            .Select(value => Prepare((value.Code, value.Value), nameof(recommendation.Conditions)))
            .Select(value => new RecommendationCondition(value.Code, value.Value))
            .ToArray();
        var tradeoffs = recommendation.Tradeoffs
            .Select(value => Prepare((value.Code, value.Value), nameof(recommendation.Tradeoffs)))
            .Select(value => new RecommendationTradeoff(value.Code, value.Value))
            .ToArray();
        return recommendation with
        {
            ProposedPatch = patch,
            Evidence = evidence,
            Conditions = conditions,
            Tradeoffs = tradeoffs
        };
    }

    private RecommendationEvidenceReference Prepare(RecommendationEvidenceReference value)
    {
        ValidateOpaque(value.EvidenceId, nameof(value.EvidenceId));
        ValidateToken(value.EvidenceKind, nameof(value.EvidenceKind), 96);
        if (value.MaximumAge is { } maximumAge
            && (maximumAge <= TimeSpan.Zero || maximumAge > TimeSpan.FromDays(365)))
            throw new ArgumentOutOfRangeException(nameof(value.MaximumAge));
        return value with { ObservedAtUtc = value.ObservedAtUtc.ToUniversalTime() };
    }

    private (string Code, string Value) Prepare((string Code, string Value) value, string field)
    {
        ValidateToken(value.Code, field, 96);
        var redacted = _redaction.Redact(value.Value ?? string.Empty) ?? string.Empty;
        if (redacted.Length > 1024)
            throw new InvalidOperationException($"Recommendation {field} value exceeds the persistence bound.");
        return (value.Code.Trim(), redacted);
    }

    private static RecommendationDecisionRecord Prepare(RecommendationDecisionRecord value)
    {
        ValidateOpaque(value.Id, nameof(value.Id));
        ValidateOpaque(value.RecommendationId, nameof(value.RecommendationId));
        ValidateToken(value.Actor, nameof(value.Actor), 96);
        if (value.ExpectedCurrentConfigurationIdentity is not null)
            ValidateOpaque(value.ExpectedCurrentConfigurationIdentity, nameof(value.ExpectedCurrentConfigurationIdentity));
        ValidateToken(value.ResultCode, nameof(value.ResultCode), 128);
        return value with { CreatedAtUtc = value.CreatedAtUtc.ToUniversalTime() };
    }

    private static RecommendationRollbackRecord Prepare(RecommendationRollbackRecord value)
    {
        ValidateOpaque(value.Id, nameof(value.Id));
        ValidateOpaque(value.RecommendationId, nameof(value.RecommendationId));
        var canonical = ExperienceJson.CanonicalizeJson(value.PreImageJson);
        if (Encoding.UTF8.GetByteCount(canonical) > MaximumPayloadBytes)
            throw new InvalidOperationException("Recommendation rollback exceeds the persistence bound.");
        return value with
        {
            PreImageJson = canonical,
            PreImageHash = ExperienceJson.Hash(canonical),
            PostApplyConfigurationIdentity = ValidateOpaque(value.PostApplyConfigurationIdentity, nameof(value.PostApplyConfigurationIdentity)),
            CreatedAtUtc = value.CreatedAtUtc.ToUniversalTime()
        };
    }

    private static void AddOptional(SqliteCommand command, ICollection<string> clauses, string column, string parameter, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        clauses.Add($"{column}={parameter}");
        Add(command, parameter, ValidateOpaque(value, column));
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string ValidateOpaque(string? value, string field)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 256 || trimmed.Any(char.IsWhiteSpace)
            || trimmed.Contains('/') || trimmed.Contains('\\'))
            throw new ArgumentException($"Recommendation {field} must be a path-free opaque value.", field);
        return trimmed;
    }

    private static string ValidateToken(string? value, string field, int maximum)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > maximum || trimmed.Any(character =>
            !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new ArgumentException($"Recommendation {field} must be a bounded token.", field);
        return trimmed;
    }

    private static DateTime ParseUtc(string value) =>
        DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await EnableForeignKeysAsync(connection, ct);
        return connection;
    }

    private static async Task EnableForeignKeysAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON";
        await command.ExecuteNonQueryAsync(ct);
    }
}
