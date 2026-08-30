using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Microsoft.Data.Sqlite;

namespace Hermaeus.Services;

public sealed class SqliteEmpiricalExperienceStore : IEmpiricalExperienceStore
{
    private const int SchemaVersion = 1;
    private const int MaxProvenance = 16;
    private readonly ISettingsService _settings;
    private readonly RedactionService _redaction;
    private readonly IActivityRecorder? _activity;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private string _initializedPath = string.Empty;

    public SqliteEmpiricalExperienceStore(
        ISettingsService settings,
        RedactionService redaction,
        IActivityRecorder? activity = null)
    {
        _settings = settings;
        _redaction = redaction;
        _activity = activity;
    }

    private string DbPath => Path.Combine(SettingsService.ResolveDataRoot(_settings.Settings), "experience.db");
    private string ConnectionString => $"Data Source={DbPath}";

    public Task InitializeAsync(CancellationToken ct = default) => EnsureInitializedAsync(ct);

    public async Task<EmpiricalExperience> AddAsync(EmpiricalExperienceDraft draft, CancellationToken ct = default)
    {
        var prepared = Prepare(draft, correctsId: null);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await InsertAsync(connection, (SqliteTransaction)transaction, prepared, ct);
        await transaction.CommitAsync(ct);
        return prepared;
    }

    public async Task<IReadOnlyList<EmpiricalExperience>> AddBatchAsync(
        IReadOnlyList<EmpiricalExperienceDraft> drafts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        if (drafts.Count == 0)
            return [];

        var prepared = drafts.Select(draft => Prepare(draft, correctsId: null)).ToArray();
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        foreach (var row in prepared)
            await InsertAsync(connection, (SqliteTransaction)transaction, row, ct);
        await transaction.CommitAsync(ct);
        return prepared;
    }

    public async Task<EmpiricalExperience?> GetAsync(string id, CancellationToken ct = default)
    {
        ValidateId(id, nameof(id));
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        return await ReadOneAsync(connection, null, id, ct);
    }

    public async Task<IReadOnlyList<EmpiricalExperience>> QueryAsync(EmpiricalExperienceQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        var command = connection.CreateCommand();
        var clauses = new List<string>();
        AddFilter(command, clauses, "e.domain", "$domain", query.Domain);
        AddFilter(command, clauses, "e.project_id", "$project", query.ProjectId);
        AddFilter(command, clauses, "e.workspace_fingerprint", "$workspace", query.WorkspaceFingerprint);
        AddFilter(command, clauses, "e.runtime_fingerprint", "$runtime", query.RuntimeFingerprint);
        AddFilter(command, clauses, "e.model_fingerprint", "$model", query.ModelFingerprint);
        AddFilter(command, clauses, "e.context_hash", "$context", query.ContextHash);
        AddFilter(command, clauses, "e.action_hash", "$action", query.ActionHash);
        AddFilter(command, clauses, "e.outcome", "$outcome", query.Outcome?.ToString());
        AddFilter(command, clauses, "e.status", "$status", query.Status?.ToString());
        if (query.CreatedFromUtc is { } from)
        {
            clauses.Add("e.created_at >= $from");
            command.Parameters.AddWithValue("$from", from.ToUniversalTime().ToString("O"));
        }
        if (query.CreatedToUtc is { } to)
        {
            clauses.Add("e.created_at <= $to");
            command.Parameters.AddWithValue("$to", to.ToUniversalTime().ToString("O"));
        }
        if (query.Origin is { } origin)
        {
            clauses.Add("EXISTS (SELECT 1 FROM experience_provenance p WHERE p.experience_id=e.id AND p.origin=$origin)");
            command.Parameters.AddWithValue("$origin", origin.ToString());
        }
        command.Parameters.AddWithValue("$limit", Math.Clamp(query.Limit, 1, 500));
        command.CommandText = $"SELECT {Columns} FROM experiences e {(clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses))} ORDER BY e.created_at DESC LIMIT $limit";

        var rows = new List<EmpiricalExperience>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(ReadExperience(reader));
        await reader.DisposeAsync();
        for (var i = 0; i < rows.Count; i++)
            rows[i] = rows[i] with { Provenance = await ReadProvenanceAsync(connection, null, rows[i].Id, ct) };
        return rows;
    }

    public async Task<EmpiricalExperience> CorrectAsync(
        string priorId,
        EmpiricalExperienceDraft replacement,
        CancellationToken ct = default)
    {
        ValidateId(priorId, nameof(priorId));
        var prepared = Prepare(replacement, priorId);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var prior = await ReadOneAsync(connection, (SqliteTransaction)transaction, priorId, ct)
            ?? throw new KeyNotFoundException($"Experience '{priorId}' does not exist.");
        if (prior.Status != EmpiricalExperienceStatus.Current)
            throw new InvalidOperationException("Only a current experience can be corrected.");
        if (!string.Equals(prior.Domain, prepared.Domain, StringComparison.Ordinal))
            throw new InvalidOperationException("A correction must stay in the original experience domain.");

        await InsertAsync(connection, (SqliteTransaction)transaction, prepared, ct);
        var supersede = connection.CreateCommand();
        supersede.Transaction = (SqliteTransaction)transaction;
        supersede.CommandText = "UPDATE experiences SET status=$status WHERE id=$id AND status=$current";
        supersede.Parameters.AddWithValue("$status", EmpiricalExperienceStatus.Superseded.ToString());
        supersede.Parameters.AddWithValue("$current", EmpiricalExperienceStatus.Current.ToString());
        supersede.Parameters.AddWithValue("$id", priorId);
        if (await supersede.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("The experience changed while correction was being saved.");
        await transaction.CommitAsync(ct);
        return prepared;
    }

    public async Task RemoveAsync(string id, CancellationToken ct = default)
    {
        ValidateId(id, nameof(id));
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var existing = await ReadOneAsync(connection, (SqliteTransaction)transaction, id, ct)
            ?? throw new KeyNotFoundException($"Experience '{id}' does not exist.");
        var dependency = connection.CreateCommand();
        dependency.Transaction = (SqliteTransaction)transaction;
        dependency.CommandText = "SELECT COUNT(*) FROM experiences WHERE corrects_experience_id=$id";
        dependency.Parameters.AddWithValue("$id", id);
        if (Convert.ToInt32(await dependency.ExecuteScalarAsync(ct)) > 0)
            throw new InvalidOperationException("Remove or supersede the dependent correction before removing this experience.");
        var delete = connection.CreateCommand();
        delete.Transaction = (SqliteTransaction)transaction;
        delete.CommandText = "DELETE FROM experiences WHERE id=$id";
        delete.Parameters.AddWithValue("$id", id);
        await delete.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        _activity.RecordSafe("experience.remove", id, ActivityOutcome.Succeeded,
            "Empirical experience removed", existing.Domain);
    }

    public async Task<string> ExportAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count is < 1 or > 500) throw new InvalidOperationException("Select between 1 and 500 experiences to export.");
        var rows = new List<EmpiricalExperience>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            var row = await GetAsync(id, ct) ?? throw new KeyNotFoundException($"Experience '{id}' does not exist.");
            rows.Add(row);
        }
        return JsonSerializer.Serialize(new EmpiricalExperienceExport(1, DateTime.UtcNow, rows), new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        });
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        var path = DbPath;
        if (_initializedPath == path && File.Exists(path)) return;
        await _initGate.WaitAsync(ct);
        try
        {
            if (_initializedPath == path && File.Exists(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync(ct);
            await EnableForeignKeysAsync(connection, ct);
            await SqliteMigrationRunner.ApplyAsync(connection, "empirical_experience", SchemaVersion,
            [
                new SqliteMigration(1, CreateSchemaAsync)
            ], ct);
            _initializedPath = path;
        }
        finally { _initGate.Release(); }
    }

    private static async Task<bool> CreateSchemaAsync(SqliteConnection connection, CancellationToken ct)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS experiences (
                id TEXT PRIMARY KEY,
                schema_version INTEGER NOT NULL,
                domain TEXT NOT NULL,
                project_id TEXT,
                workspace_fingerprint TEXT,
                context_json TEXT NOT NULL,
                context_hash TEXT NOT NULL,
                action_json TEXT NOT NULL,
                action_hash TEXT NOT NULL,
                outcome TEXT NOT NULL,
                outcome_evidence_code TEXT NOT NULL,
                outcome_detail TEXT NOT NULL,
                outcome_derived_at TEXT NOT NULL,
                outcome_derivation_version INTEGER NOT NULL,
                runtime_fingerprint TEXT,
                model_fingerprint TEXT,
                created_at TEXT NOT NULL,
                corrects_experience_id TEXT REFERENCES experiences(id) ON DELETE RESTRICT,
                status TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS experience_provenance (
                experience_id TEXT NOT NULL REFERENCES experiences(id) ON DELETE CASCADE,
                ordinal INTEGER NOT NULL,
                evidence_id TEXT NOT NULL,
                kind TEXT NOT NULL,
                title TEXT NOT NULL,
                locator TEXT,
                snippet TEXT,
                score REAL,
                timestamp TEXT,
                origin TEXT NOT NULL,
                PRIMARY KEY (experience_id, ordinal)
            );
            CREATE INDEX IF NOT EXISTS idx_experience_domain ON experiences(domain);
            CREATE INDEX IF NOT EXISTS idx_experience_project ON experiences(project_id);
            CREATE INDEX IF NOT EXISTS idx_experience_workspace ON experiences(workspace_fingerprint);
            CREATE INDEX IF NOT EXISTS idx_experience_context_action ON experiences(context_hash, action_hash);
            CREATE INDEX IF NOT EXISTS idx_experience_runtime ON experiences(runtime_fingerprint);
            CREATE INDEX IF NOT EXISTS idx_experience_model ON experiences(model_fingerprint);
            CREATE INDEX IF NOT EXISTS idx_experience_created ON experiences(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_experience_status ON experiences(status);
            CREATE INDEX IF NOT EXISTS idx_experience_provenance_origin ON experience_provenance(origin);
            """;
        await command.ExecuteNonQueryAsync(ct);
        return true;
    }

    private EmpiricalExperience Prepare(EmpiricalExperienceDraft draft, string? correctsId)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!EmpiricalExperienceDomains.Initial.Contains(draft.Domain))
            throw new InvalidOperationException($"Unsupported experience domain '{draft.Domain}'.");
        if (draft.SchemaVersion != 1) throw new InvalidOperationException("Only experience schema version 1 is supported.");
        if (draft.Provenance.Count is < 1 or > MaxProvenance)
            throw new InvalidOperationException($"Experience provenance must contain between 1 and {MaxProvenance} references.");
        var context = ExperienceJson.CanonicalizeJson(_redaction.Redact(draft.ContextJson));
        var action = ExperienceJson.CanonicalizeJson(_redaction.Redact(draft.ActionJson));
        var provenance = draft.Provenance.Select(PrepareProvenance).ToArray();
        var id = string.IsNullOrWhiteSpace(draft.Id) ? Guid.NewGuid().ToString("N") : draft.Id!.Trim();
        ValidateId(id, nameof(draft.Id));
        return new EmpiricalExperience
        {
            Id = id,
            SchemaVersion = 1,
            Domain = draft.Domain,
            ProjectId = Bound(draft.ProjectId, 128),
            WorkspaceFingerprint = Fingerprint(draft.WorkspaceFingerprint, "workspace"),
            ContextJson = context,
            ContextHash = ExperienceJson.Hash(context),
            ActionJson = action,
            ActionHash = ExperienceJson.Hash(action),
            Outcome = draft.Outcome with { Detail = Bound(_redaction.Redact(draft.Outcome.Detail), NormalizedToolOutcome.MaxDetailLength) ?? string.Empty },
            Provenance = provenance,
            RuntimeFingerprint = Fingerprint(draft.RuntimeFingerprint, "runtime"),
            ModelFingerprint = Fingerprint(draft.ModelFingerprint, "model"),
            CreatedAtUtc = DateTime.UtcNow,
            CorrectsExperienceId = correctsId,
            Status = EmpiricalExperienceStatus.Current
        };
    }

    private EmpiricalExperienceProvenance PrepareProvenance(EmpiricalExperienceProvenance value)
    {
        ValidateId(value.EvidenceId, nameof(value.EvidenceId));
        var source = value.Source with
        {
            Title = Bound(_redaction.Redact(value.Source.Title), 512) ?? string.Empty,
            Locator = Bound(_redaction.Redact(value.Source.Locator ?? string.Empty), 1024),
            Snippet = Bound(_redaction.Redact(value.Source.Snippet ?? string.Empty), 2048)
        };
        return new EmpiricalExperienceProvenance(value.EvidenceId, source);
    }

    private static string? Fingerprint(string? value, string field)
    {
        var bounded = Bound(value, 256);
        if (!string.IsNullOrEmpty(bounded) && (Path.IsPathRooted(bounded) || bounded.Contains('\\') || bounded.Contains('/')))
            throw new InvalidOperationException($"The {field} fingerprint must be opaque, not a path.");
        return bounded;
    }

    private static string? Bound(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > max) throw new InvalidOperationException($"Experience value exceeds {max} characters.");
        return trimmed;
    }

    private static void ValidateId(string id, string field)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(c => !char.IsLetterOrDigit(c) && c is not '-' and not '_' and not ':'))
            throw new ArgumentException("Experience identifiers must be 1-128 safe opaque characters.", field);
    }

    private static async Task InsertAsync(SqliteConnection connection, SqliteTransaction transaction, EmpiricalExperience row, CancellationToken ct)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO experiences (id,schema_version,domain,project_id,workspace_fingerprint,context_json,context_hash,
                action_json,action_hash,outcome,outcome_evidence_code,outcome_detail,outcome_derived_at,outcome_derivation_version,
                runtime_fingerprint,model_fingerprint,created_at,corrects_experience_id,status)
            VALUES ($id,$schema,$domain,$project,$workspace,$context,$context_hash,$action,$action_hash,$outcome,$code,$detail,
                $derived,$derivation_version,$runtime,$model,$created,$corrects,$status)
            """;
        Add(command, "$id", row.Id); Add(command, "$schema", row.SchemaVersion); Add(command, "$domain", row.Domain);
        Add(command, "$project", row.ProjectId); Add(command, "$workspace", row.WorkspaceFingerprint);
        Add(command, "$context", row.ContextJson); Add(command, "$context_hash", row.ContextHash);
        Add(command, "$action", row.ActionJson); Add(command, "$action_hash", row.ActionHash);
        Add(command, "$outcome", row.Outcome.Outcome.ToString()); Add(command, "$code", row.Outcome.EvidenceCode);
        Add(command, "$detail", row.Outcome.Detail); Add(command, "$derived", row.Outcome.DerivedAtUtc.ToString("O"));
        Add(command, "$derivation_version", row.Outcome.DerivationVersion); Add(command, "$runtime", row.RuntimeFingerprint);
        Add(command, "$model", row.ModelFingerprint); Add(command, "$created", row.CreatedAtUtc.ToString("O"));
        Add(command, "$corrects", row.CorrectsExperienceId); Add(command, "$status", row.Status.ToString());
        await command.ExecuteNonQueryAsync(ct);
        for (var i = 0; i < row.Provenance.Count; i++)
        {
            var item = row.Provenance[i];
            var provenance = connection.CreateCommand();
            provenance.Transaction = transaction;
            provenance.CommandText = """
                INSERT INTO experience_provenance (experience_id,ordinal,evidence_id,kind,title,locator,snippet,score,timestamp,origin)
                VALUES ($id,$ordinal,$evidence,$kind,$title,$locator,$snippet,$score,$timestamp,$origin)
                """;
            Add(provenance, "$id", row.Id); Add(provenance, "$ordinal", i); Add(provenance, "$evidence", item.EvidenceId);
            Add(provenance, "$kind", item.Source.Kind.ToString()); Add(provenance, "$title", item.Source.Title);
            Add(provenance, "$locator", item.Source.Locator); Add(provenance, "$snippet", item.Source.Snippet);
            Add(provenance, "$score", item.Source.Score); Add(provenance, "$timestamp", item.Source.Timestamp?.ToString("O"));
            Add(provenance, "$origin", item.Source.EvidenceOrigin.ToString());
            await provenance.ExecuteNonQueryAsync(ct);
        }
    }

    private const string Columns = "e.id,e.schema_version,e.domain,e.project_id,e.workspace_fingerprint,e.context_json,e.context_hash,e.action_json,e.action_hash,e.outcome,e.outcome_evidence_code,e.outcome_detail,e.outcome_derived_at,e.outcome_derivation_version,e.runtime_fingerprint,e.model_fingerprint,e.created_at,e.corrects_experience_id,e.status";

    private static EmpiricalExperience ReadExperience(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0), SchemaVersion = reader.GetInt32(1), Domain = reader.GetString(2),
        ProjectId = NullableString(reader, 3), WorkspaceFingerprint = NullableString(reader, 4),
        ContextJson = reader.GetString(5), ContextHash = reader.GetString(6), ActionJson = reader.GetString(7), ActionHash = reader.GetString(8),
        Outcome = new NormalizedToolOutcome
        {
            Outcome = Enum.Parse<NormalizedOutcome>(reader.GetString(9)), EvidenceCode = reader.GetString(10), Detail = reader.GetString(11),
            DerivedAtUtc = DateTime.Parse(reader.GetString(12), null, System.Globalization.DateTimeStyles.RoundtripKind), DerivationVersion = reader.GetInt32(13)
        },
        RuntimeFingerprint = NullableString(reader, 14), ModelFingerprint = NullableString(reader, 15),
        CreatedAtUtc = DateTime.Parse(reader.GetString(16), null, System.Globalization.DateTimeStyles.RoundtripKind),
        CorrectsExperienceId = NullableString(reader, 17), Status = Enum.Parse<EmpiricalExperienceStatus>(reader.GetString(18))
    };

    private static async Task<EmpiricalExperience?> ReadOneAsync(SqliteConnection connection, SqliteTransaction? transaction, string id, CancellationToken ct)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT {Columns} FROM experiences e WHERE e.id=$id"; command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var row = ReadExperience(reader); await reader.DisposeAsync();
        return row with { Provenance = await ReadProvenanceAsync(connection, transaction, id, ct) };
    }

    private static async Task<IReadOnlyList<EmpiricalExperienceProvenance>> ReadProvenanceAsync(SqliteConnection connection, SqliteTransaction? transaction, string id, CancellationToken ct)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT evidence_id,kind,title,locator,snippet,score,timestamp,origin FROM experience_provenance WHERE experience_id=$id ORDER BY ordinal";
        command.Parameters.AddWithValue("$id", id);
        var items = new List<EmpiricalExperienceProvenance>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(new EmpiricalExperienceProvenance(reader.GetString(0), new SourceReference(
                Enum.Parse<ProvenanceKind>(reader.GetString(1)), reader.GetString(2), NullableString(reader, 3), NullableString(reader, 4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5), reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind),
                Enum.Parse<EvidenceOrigin>(reader.GetString(7)))));
        }
        return items;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(ConnectionString); await connection.OpenAsync(ct); await EnableForeignKeysAsync(connection, ct); return connection;
    }

    private static async Task EnableForeignKeysAsync(SqliteConnection connection, CancellationToken ct)
    {
        var command = connection.CreateCommand(); command.CommandText = "PRAGMA foreign_keys=ON"; await command.ExecuteNonQueryAsync(ct);
    }

    private static void AddFilter(SqliteCommand command, ICollection<string> clauses, string column, string parameter, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return; clauses.Add($"{column}={parameter}"); command.Parameters.AddWithValue(parameter, value);
    }

    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    private static string? NullableString(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
