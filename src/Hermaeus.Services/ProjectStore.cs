using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Hermaeus.Services;

/// <summary>
/// Backed by {DataRoot}/projects.db, directly under the data root like every
/// other store, so data-root migration and backup pick it up automatically
/// through <see cref="DataRootManifest"/> with no per-file registration step.
/// </summary>
public sealed class ProjectStore : IProjectStore, IProjectStateStore
{
    private const int SchemaVersion = 2;
    private const int MaxStateItems = 64;
    private const int MaxPendingProposals = 32;
    private const int MaxProposalBytes = 64 * 1024;
    private const int MaxSourceBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISettingsService _settings;
    private string _initializedPath = string.Empty;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private string DbPath
    {
        get
        {
            var dir = SettingsService.ResolveDataRoot(_settings.Settings);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "projects.db");
        }
    }
    private string Cs => $"Data Source={DbPath}";

    public ProjectStore(ISettingsService settings)
    {
        _settings = settings;
    }

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
            CREATE TABLE IF NOT EXISTS projects (
                id                    TEXT PRIMARY KEY,
                name                  TEXT NOT NULL,
                description           TEXT NOT NULL,
                folder_root           TEXT NOT NULL,
                dataset_id            TEXT NOT NULL,
                default_model_id      TEXT NOT NULL,
                default_system_prompt TEXT NOT NULL,
                color                 TEXT NOT NULL,
                created_at            TEXT NOT NULL,
                updated_at            TEXT NOT NULL,
                last_opened_at        TEXT NOT NULL,
                is_archived           INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_projects_last_opened ON projects(last_opened_at DESC);";
            await cmd.ExecuteNonQueryAsync(ct);

            await SqliteMigrationRunner.ApplyAsync(c, "projects", SchemaVersion,
                [new SqliteMigration(2, AddProjectStateTablesAsync)], ct);
            _initializedPath = dbPath;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private static async Task<bool> EnsureColumnAsync(SqliteConnection c, string column, string definition, CancellationToken ct)
    {
        var exists = false;
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(projects)";
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                if (string.Equals(rd.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists) return false;
        await using var alter = c.CreateCommand();
        alter.CommandText = $"ALTER TABLE projects ADD COLUMN {column} {definition}";
        await alter.ExecuteNonQueryAsync(ct);
        return true;
    }

    public async Task<List<Project>> GetAllAsync(bool includeArchived = true, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = includeArchived
            ? "SELECT * FROM projects ORDER BY is_archived ASC, last_opened_at DESC"
            : "SELECT * FROM projects WHERE is_archived = 0 ORDER BY last_opened_at DESC";
        var r = new List<Project>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) r.Add(Map(rd));
        return r;
    }

    public async Task<Project?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM projects WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct) ? Map(rd) : null;
    }

    public async Task SaveAsync(Project project, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        project.UpdatedAt = DateTime.UtcNow;
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO projects (id,name,description,folder_root,dataset_id,default_model_id,default_system_prompt,color,created_at,updated_at,last_opened_at,is_archived)
            VALUES ($id,$name,$desc,$folder,$dataset,$model,$prompt,$color,$ca,$ua,$loa,$archived)
            ON CONFLICT(id) DO UPDATE SET
                name=excluded.name, description=excluded.description,
                folder_root=excluded.folder_root, dataset_id=excluded.dataset_id,
                default_model_id=excluded.default_model_id, default_system_prompt=excluded.default_system_prompt,
                color=excluded.color, updated_at=excluded.updated_at,
                last_opened_at=excluded.last_opened_at, is_archived=excluded.is_archived";
        // Every text column here is NOT NULL, and a null parameter does not bind
        // as NULL: Microsoft.Data.Sqlite throws "Value must be set." while
        // preparing the statement. That is not hypothetical. Avalonia binds a
        // cleared TextBox back as null regardless of the property's nullable
        // annotation, so emptying Description (or any other box) in the project
        // editor and pressing Save took the whole app down with an unhandled
        // exception. Normalise here, at the boundary that actually knows the
        // columns are NOT NULL, rather than trusting every caller.
        cmd.Parameters.AddWithValue("$id", Text(project.Id));
        cmd.Parameters.AddWithValue("$name", Text(project.Name).Trim());
        cmd.Parameters.AddWithValue("$desc", Text(project.Description));
        cmd.Parameters.AddWithValue("$folder", Text(project.FolderRoot));
        cmd.Parameters.AddWithValue("$dataset", Text(project.DatasetId));
        cmd.Parameters.AddWithValue("$model", Text(project.DefaultModelId));
        cmd.Parameters.AddWithValue("$prompt", Text(project.DefaultSystemPrompt));
        cmd.Parameters.AddWithValue("$color", ProjectColors.IsValid(Text(project.Color)) ? project.Color : ProjectColors.Default);
        cmd.Parameters.AddWithValue("$ca", project.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$ua", project.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$loa", project.LastOpenedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$archived", project.IsArchived ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// A value safe to bind to a NOT NULL text column. See SaveAsync for why
    /// this exists rather than relying on the model's non-nullable strings.
    /// </summary>
    private static string Text(string? value) => value ?? string.Empty;

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        foreach (var table in new[] { "project_state_proposals", "project_state_items", "project_state" })
        {
            var owned = c.CreateCommand();
            owned.Transaction = transaction;
            owned.CommandText = $"DELETE FROM {table} WHERE project_id = $id";
            owned.Parameters.AddWithValue("$id", id);
            await owned.ExecuteNonQueryAsync(ct);
        }
        var cmd = c.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "DELETE FROM projects WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<ProjectState> GetStateAsync(string projectId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        return await LoadStateAsync(c, projectId, null, ct);
    }

    public async Task<ProjectState> SaveStateAsync(ProjectState state, long expectedRevision, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        ValidateState(state);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        var saved = await SaveStateInTransactionAsync(c, transaction, state, expectedRevision, ct);
        await transaction.CommitAsync(ct);
        return saved;
    }

    public async Task<IReadOnlyList<ProjectStateProposal>> GetProposalsAsync(
        string projectId, bool includeReviewed = false, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        var command = c.CreateCommand();
        command.CommandText = """
            SELECT id, project_id, base_revision, proposed_state_json, origin, source_json,
                   status, rejection_reason, created_at, updated_at
            FROM project_state_proposals
            WHERE project_id = $project AND ($reviewed = 1 OR status = 'Pending')
            ORDER BY created_at ASC
            """;
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$reviewed", includeReviewed ? 1 : 0);
        var proposals = new List<ProjectStateProposal>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) proposals.Add(MapProposal(reader));
        return proposals;
    }

    public async Task<ProjectStateProposal> CreateProposalAsync(ProjectStateProposal proposal, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        ValidateState(proposal.ProposedState);
        if (!string.Equals(proposal.ProjectId, proposal.ProposedState.ProjectId, StringComparison.Ordinal))
            throw new ArgumentException("Proposal and proposed state must name the same project.", nameof(proposal));
        var json = JsonSerializer.Serialize(proposal.ProposedState, JsonOptions);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxProposalBytes)
            throw new ArgumentException($"Project State proposal exceeds {MaxProposalBytes} bytes.", nameof(proposal));

        proposal.Id = string.IsNullOrWhiteSpace(proposal.Id) ? Guid.NewGuid().ToString("N") : proposal.Id;
        proposal.Status = ProjectStateProposalStatus.Pending;
        proposal.RejectionReason = string.Empty;
        proposal.CreatedAtUtc = DateTime.UtcNow;
        proposal.UpdatedAtUtc = proposal.CreatedAtUtc;
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        if (!await ProjectExistsAsync(c, proposal.ProjectId, null, ct))
            throw new KeyNotFoundException($"Project '{proposal.ProjectId}' was not found.");
        var pendingCount = c.CreateCommand();
        pendingCount.CommandText = "SELECT COUNT(*) FROM project_state_proposals WHERE project_id = $project AND status = 'Pending'";
        pendingCount.Parameters.AddWithValue("$project", proposal.ProjectId);
        if (Convert.ToInt32(await pendingCount.ExecuteScalarAsync(ct)) >= MaxPendingProposals)
            throw new InvalidOperationException($"Project State is limited to {MaxPendingProposals} pending proposals.");
        var command = c.CreateCommand();
        command.CommandText = """
            INSERT INTO project_state_proposals
                (id, project_id, base_revision, proposed_state_json, origin, source_json,
                 status, rejection_reason, created_at, updated_at)
            VALUES ($id, $project, $revision, $state, $origin, $source,
                    $status, '', $created, $updated)
            """;
        AddProposalParameters(command, proposal, json);
        await command.ExecuteNonQueryAsync(ct);
        return proposal;
    }

    public async Task<ProjectState> AcceptProposalAsync(
        string proposalId, ProjectState? editedState = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        var proposal = await LoadProposalAsync(c, proposalId, transaction, ct)
            ?? throw new KeyNotFoundException($"Project State proposal '{proposalId}' was not found.");
        if (proposal.Status != ProjectStateProposalStatus.Pending)
            throw new InvalidOperationException("Only a pending Project State proposal can be accepted.");

        var accepted = (editedState ?? proposal.ProposedState).Clone();
        accepted.ProjectId = proposal.ProjectId;
        accepted.UpdatedByOrigin = editedState is null ? proposal.Origin : EvidenceOrigin.UserProvided;
        if (editedState is not null)
        {
            foreach (var item in accepted.Items) item.Origin = EvidenceOrigin.UserProvided;
        }
        ValidateState(accepted);
        var saved = await SaveStateInTransactionAsync(c, transaction, accepted, proposal.BaseRevision, ct);
        await SetProposalStatusAsync(c, transaction, proposalId, ProjectStateProposalStatus.Accepted, string.Empty, ct);
        await transaction.CommitAsync(ct);
        return saved;
    }

    public async Task RejectProposalAsync(string proposalId, string reason, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs); await c.OpenAsync(ct);
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        var proposal = await LoadProposalAsync(c, proposalId, transaction, ct)
            ?? throw new KeyNotFoundException($"Project State proposal '{proposalId}' was not found.");
        if (proposal.Status != ProjectStateProposalStatus.Pending)
            throw new InvalidOperationException("Only a pending Project State proposal can be rejected.");
        await SetProposalStatusAsync(c, transaction, proposalId, ProjectStateProposalStatus.Rejected, Bound(reason, 1000), ct);
        await transaction.CommitAsync(ct);
    }

    private static async Task<bool> AddProjectStateTablesAsync(SqliteConnection c, CancellationToken ct)
    {
        var command = c.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS project_state (
                project_id TEXT PRIMARY KEY,
                revision INTEGER NOT NULL,
                current_objective TEXT NOT NULL,
                milestone TEXT NOT NULL,
                status TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                updated_by_origin TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS project_state_items (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                kind TEXT NOT NULL,
                text TEXT NOT NULL,
                artifact_locator TEXT NULL,
                display_order INTEGER NOT NULL,
                origin TEXT NOT NULL,
                source_json TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_project_state_items_order
                ON project_state_items(project_id, display_order, id);
            CREATE TABLE IF NOT EXISTS project_state_proposals (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                base_revision INTEGER NOT NULL,
                proposed_state_json TEXT NOT NULL,
                origin TEXT NOT NULL,
                source_json TEXT NULL,
                status TEXT NOT NULL,
                rejection_reason TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_project_state_proposals_queue
                ON project_state_proposals(project_id, status, created_at);
            """;
        await command.ExecuteNonQueryAsync(ct);
        return true;
    }

    private static async Task<ProjectState> LoadStateAsync(
        SqliteConnection c, string projectId, SqliteTransaction? transaction, CancellationToken ct)
    {
        var state = new ProjectState { ProjectId = projectId, Revision = 0 };
        var command = c.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT revision, current_objective, milestone, status, updated_at, updated_by_origin FROM project_state WHERE project_id = $project";
        command.Parameters.AddWithValue("$project", projectId);
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                state.Revision = reader.GetInt64(0);
                state.CurrentObjective = reader.GetString(1);
                state.Milestone = reader.GetString(2);
                state.Status = reader.GetString(3);
                state.UpdatedAtUtc = SqliteDateTime.Parse(reader.GetString(4));
                state.UpdatedByOrigin = Enum.Parse<EvidenceOrigin>(reader.GetString(5));
            }
        }
        if (state.Revision == 0) return state;

        var items = c.CreateCommand();
        items.Transaction = transaction;
        items.CommandText = "SELECT id, kind, text, artifact_locator, display_order, origin, source_json, created_at, updated_at FROM project_state_items WHERE project_id = $project ORDER BY display_order, id";
        items.Parameters.AddWithValue("$project", projectId);
        await using var itemReader = await items.ExecuteReaderAsync(ct);
        while (await itemReader.ReadAsync(ct))
        {
            state.Items.Add(new ProjectStateItem
            {
                Id = itemReader.GetString(0),
                Kind = Enum.Parse<ProjectStateItemKind>(itemReader.GetString(1)),
                Text = itemReader.GetString(2),
                ArtifactLocator = itemReader.IsDBNull(3) ? null : itemReader.GetString(3),
                Order = itemReader.GetInt32(4),
                Origin = Enum.Parse<EvidenceOrigin>(itemReader.GetString(5)),
                Source = DeserializeSource(itemReader, 6),
                CreatedAtUtc = SqliteDateTime.Parse(itemReader.GetString(7)),
                UpdatedAtUtc = SqliteDateTime.Parse(itemReader.GetString(8))
            });
        }
        return state;
    }

    private static async Task<ProjectState> SaveStateInTransactionAsync(
        SqliteConnection c, SqliteTransaction transaction, ProjectState state, long expectedRevision, CancellationToken ct)
    {
        if (!await ProjectExistsAsync(c, state.ProjectId, transaction, ct))
            throw new KeyNotFoundException($"Project '{state.ProjectId}' was not found.");
        var current = await LoadStateAsync(c, state.ProjectId, transaction, ct);
        if (current.Revision != expectedRevision)
            throw new ProjectStateRevisionConflictException(expectedRevision, current.Revision);
        var now = DateTime.UtcNow;
        var nextRevision = expectedRevision + 1;
        var stateCommand = c.CreateCommand();
        stateCommand.Transaction = transaction;
        stateCommand.CommandText = """
            INSERT INTO project_state (project_id, revision, current_objective, milestone, status, updated_at, updated_by_origin)
            VALUES ($project, $revision, $objective, $milestone, $status, $updated, $origin)
            ON CONFLICT(project_id) DO UPDATE SET revision = excluded.revision,
                current_objective = excluded.current_objective, milestone = excluded.milestone,
                status = excluded.status, updated_at = excluded.updated_at,
                updated_by_origin = excluded.updated_by_origin
            """;
        stateCommand.Parameters.AddWithValue("$project", state.ProjectId);
        stateCommand.Parameters.AddWithValue("$revision", nextRevision);
        stateCommand.Parameters.AddWithValue("$objective", Bound(state.CurrentObjective, 4000));
        stateCommand.Parameters.AddWithValue("$milestone", Bound(state.Milestone, 1000));
        stateCommand.Parameters.AddWithValue("$status", Bound(state.Status, 1000));
        stateCommand.Parameters.AddWithValue("$updated", now.ToString("O"));
        stateCommand.Parameters.AddWithValue("$origin", state.UpdatedByOrigin.ToString());
        await stateCommand.ExecuteNonQueryAsync(ct);

        var deleteItems = c.CreateCommand();
        deleteItems.Transaction = transaction;
        deleteItems.CommandText = "DELETE FROM project_state_items WHERE project_id = $project";
        deleteItems.Parameters.AddWithValue("$project", state.ProjectId);
        await deleteItems.ExecuteNonQueryAsync(ct);
        for (var index = 0; index < state.Items.Count; index++)
        {
            var item = state.Items[index];
            var insert = c.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO project_state_items
                    (id, project_id, kind, text, artifact_locator, display_order, origin, source_json, created_at, updated_at)
                VALUES ($id, $project, $kind, $text, $locator, $order, $origin, $source, $created, $updated)
                """;
            insert.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id);
            insert.Parameters.AddWithValue("$project", state.ProjectId);
            insert.Parameters.AddWithValue("$kind", item.Kind.ToString());
            insert.Parameters.AddWithValue("$text", Bound(item.Text, 4000));
            insert.Parameters.AddWithValue("$locator", (object?)BoundNullable(item.ArtifactLocator, 2000) ?? DBNull.Value);
            insert.Parameters.AddWithValue("$order", index);
            insert.Parameters.AddWithValue("$origin", item.Origin.ToString());
            insert.Parameters.AddWithValue("$source", (object?)SerializeSource(item.Source) ?? DBNull.Value);
            insert.Parameters.AddWithValue("$created", item.CreatedAtUtc.ToString("O"));
            insert.Parameters.AddWithValue("$updated", now.ToString("O"));
            await insert.ExecuteNonQueryAsync(ct);
        }
        return await LoadStateAsync(c, state.ProjectId, transaction, ct);
    }

    private static async Task<ProjectStateProposal?> LoadProposalAsync(
        SqliteConnection c, string id, SqliteTransaction transaction, CancellationToken ct)
    {
        var command = c.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, project_id, base_revision, proposed_state_json, origin, source_json, status, rejection_reason, created_at, updated_at FROM project_state_proposals WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapProposal(reader) : null;
    }

    private static ProjectStateProposal MapProposal(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0), ProjectId = reader.GetString(1), BaseRevision = reader.GetInt64(2),
        ProposedState = JsonSerializer.Deserialize<ProjectState>(reader.GetString(3), JsonOptions) ?? new ProjectState(),
        Origin = Enum.Parse<EvidenceOrigin>(reader.GetString(4)), Source = DeserializeSource(reader, 5),
        Status = Enum.Parse<ProjectStateProposalStatus>(reader.GetString(6)), RejectionReason = reader.GetString(7),
        CreatedAtUtc = SqliteDateTime.Parse(reader.GetString(8)), UpdatedAtUtc = SqliteDateTime.Parse(reader.GetString(9))
    };

    private static void AddProposalParameters(SqliteCommand command, ProjectStateProposal proposal, string json)
    {
        command.Parameters.AddWithValue("$id", proposal.Id); command.Parameters.AddWithValue("$project", proposal.ProjectId);
        command.Parameters.AddWithValue("$revision", proposal.BaseRevision); command.Parameters.AddWithValue("$state", json);
        command.Parameters.AddWithValue("$origin", proposal.Origin.ToString());
        command.Parameters.AddWithValue("$source", (object?)SerializeSource(proposal.Source) ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", proposal.Status.ToString());
        command.Parameters.AddWithValue("$created", proposal.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updated", proposal.UpdatedAtUtc.ToString("O"));
    }

    private static async Task SetProposalStatusAsync(SqliteConnection c, SqliteTransaction transaction, string id,
        ProjectStateProposalStatus status, string reason, CancellationToken ct)
    {
        var command = c.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE project_state_proposals SET status = $status, rejection_reason = $reason, updated_at = $updated WHERE id = $id";
        command.Parameters.AddWithValue("$status", status.ToString()); command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O")); command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void ValidateState(ProjectState state)
    {
        if (string.IsNullOrWhiteSpace(state.ProjectId)) throw new ArgumentException("Project id is required.", nameof(state));
        if (state.Items.Count > MaxStateItems) throw new ArgumentException($"Project State is limited to {MaxStateItems} items.", nameof(state));
        if (state.Items.Any(item => string.IsNullOrWhiteSpace(item.Text))) throw new ArgumentException("Project State items require text.", nameof(state));
        if (state.Items.Select(item => SerializeSource(item.Source)).Where(json => json is not null)
            .Any(json => System.Text.Encoding.UTF8.GetByteCount(json!) > MaxSourceBytes))
            throw new ArgumentException($"A Project State source exceeds {MaxSourceBytes} bytes.", nameof(state));
    }

    private static async Task<bool> ProjectExistsAsync(
        SqliteConnection connection, string projectId, SqliteTransaction? transaction, CancellationToken ct)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM projects WHERE id = $id";
        command.Parameters.AddWithValue("$id", projectId);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private static string Bound(string? value, int max)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length > max ? text[..max] : text;
    }
    private static string? BoundNullable(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : Bound(value, max);
    private static string? SerializeSource(SourceReference? source) => source is null ? null : JsonSerializer.Serialize(source, JsonOptions);
    private static SourceReference? DeserializeSource(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal)
        ? null : JsonSerializer.Deserialize<SourceReference>(reader.GetString(ordinal), JsonOptions);

    private static Project Map(SqliteDataReader r) => new()
    {
        Id = GetString(r, "id"),
        Name = GetString(r, "name"),
        Description = GetString(r, "description"),
        FolderRoot = GetString(r, "folder_root"),
        DatasetId = GetString(r, "dataset_id"),
        DefaultModelId = GetString(r, "default_model_id"),
        DefaultSystemPrompt = GetString(r, "default_system_prompt"),
        Color = GetString(r, "color", ProjectColors.Default),
        CreatedAt = SqliteDateTime.Parse(GetString(r, "created_at")),
        UpdatedAt = SqliteDateTime.Parse(GetString(r, "updated_at")),
        LastOpenedAt = SqliteDateTime.Parse(GetString(r, "last_opened_at")),
        IsArchived = GetInt(r, "is_archived") != 0
    };

    private static string GetString(SqliteDataReader r, string name, string fallback = "")
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? fallback : r.GetString(ordinal);
    }

    private static int GetInt(SqliteDataReader r, string name)
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? 0 : r.GetInt32(ordinal);
    }
}
