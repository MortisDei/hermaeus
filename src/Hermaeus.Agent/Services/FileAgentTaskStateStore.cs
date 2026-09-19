using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hermaeus.Agent.Models;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Microsoft.Data.Sqlite;

namespace Hermaeus.Agent.Services;

public sealed class FileAgentTaskStateStore : IAgentTaskStateStore
{
    private const int IndexSchemaVersion = 6;
    private const int MaxTaskIdLength = 80;
    private static readonly Regex SafeTaskIdRegex = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9"
    };
    private readonly ISettingsService _settings;
    private readonly IRuntimeLogService? _logs;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private string _initializedIndexPath = string.Empty;

    public FileAgentTaskStateStore(ISettingsService settings, IRuntimeLogService? logs = null)
    {
        _settings = settings;
        _logs = logs;
    }

    private string AgentRoot
    {
        get
        {
            var configured = _settings.Settings.DataManagement.DataRootDirectory?.Trim();
            var root = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hermaeus")
                : Path.GetFullPath(configured);
            return Path.Combine(root, "agent");
        }
    }

    public Task InitializeAsync(CancellationToken ct = default)
    {
        return EnsureIndexInitializedAsync(ct);
    }

    public string GetTaskDirectory(string taskId)
    {
        var safeId = NormalizeTaskId(taskId);
        return Path.Combine(AgentRoot, "tasks", safeId);
    }

    public async Task SaveAsync(AgentTaskState state, CancellationToken ct = default)
    {
        await EnsureIndexInitializedAsync(ct);
        state.UpdatedAt = DateTime.UtcNow;
        var dir = GetTaskDirectory(state.TaskId);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "task_state.json");
        await AtomicFileWriter.WriteAllTextAsync(path, JsonSerializer.Serialize(state, AgentJson.Options), ct);
        await UpsertIndexAsync(state, ct);
    }

    public async Task<AgentTaskState?> LoadAsync(string taskId, CancellationToken ct = default)
    {
        var path = Path.Combine(GetTaskDirectory(taskId), "task_state.json");
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<AgentTaskState>(json, AgentJson.Options);
    }

    public async Task DeleteAsync(string taskId, CancellationToken ct = default)
    {
        await EnsureIndexInitializedAsync(ct);
        var root = await LoadAsync(taskId, ct)
            ?? throw new InvalidOperationException("Agent task was not found.");
        if (!string.IsNullOrWhiteSpace(root.ParentTaskId))
            throw new InvalidOperationException("Delete the top-level run to remove its sub-tasks.");

        var links = new List<(string TaskId, string? ParentTaskId)>();
        await using (var connection = new SqliteConnection(IndexConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT task_id, parent_task_id FROM agent_task_index";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                links.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal) { root.TaskId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var link in links)
            {
                if (link.ParentTaskId is not null && ids.Contains(link.ParentTaskId) && ids.Add(link.TaskId))
                    changed = true;
            }
        }

        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var state = await LoadAsync(id, ct);
            if (state?.Status == AgentTaskStatus.Running)
                throw new InvalidOperationException("Stop the run before deleting it.");
        }

        var tasksRoot = Path.GetFullPath(Path.Combine(AgentRoot, "tasks"));
        var tasksRootPrefix = tasksRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var directory = Path.GetFullPath(GetTaskDirectory(id));
            if (!directory.StartsWith(tasksRootPrefix, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
                throw new InvalidOperationException("Agent task directory escaped the agent task root.");
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }

        await using var deleteConnection = new SqliteConnection(IndexConnectionString);
        await deleteConnection.OpenAsync(ct);
        await using var deleteCommand = deleteConnection.CreateCommand();
        var parameters = new List<string>(ids.Count);
        var index = 0;
        foreach (var id in ids)
        {
            var parameter = $"$task{index++}";
            parameters.Add(parameter);
            deleteCommand.Parameters.AddWithValue(parameter, id);
        }
        deleteCommand.CommandText = $"DELETE FROM agent_task_index WHERE task_id IN ({string.Join(",", parameters)})";
        await deleteCommand.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AgentTaskListItem>> ListRecentAsync(int limit = 25, CancellationToken ct = default)
    {
        await EnsureIndexInitializedAsync(ct);
        await using var c = new SqliteConnection(IndexConnectionString);
        await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = @"
            SELECT task_id, goal, status, updated_at, parent_task_id, pending_step_count, has_reservations, project_id, model_id, model_display_name
            FROM agent_task_index
            ORDER BY updated_at DESC
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));
        var tasks = new List<AgentTaskListItem>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            tasks.Add(new AgentTaskListItem(
                r.GetString(0),
                r.GetString(1),
                ParseStatus(r.GetString(2)),
                ParseDate(r.GetString(3)),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetInt32(5),
                r.GetInt32(6) != 0,
                r.IsDBNull(7) ? string.Empty : r.GetString(7),
                r.IsDBNull(8) ? string.Empty : r.GetString(8),
                r.IsDBNull(9) ? string.Empty : r.GetString(9)));
        }

        return tasks;
    }

    public async Task<IReadOnlyList<AgentReviewQueueItem>> ListReviewQueueAsync(int limit = 25, CancellationToken ct = default)
    {
        await EnsureIndexInitializedAsync(ct);
        await using var c = new SqliteConnection(IndexConnectionString);
        await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = @"
            SELECT t.task_id, t.goal, t.status, t.updated_at, t.active_step, t.summary,
                   t.approval_count, t.last_approval_action, t.last_approval_approved, t.last_approval_at,
                   p.goal, t.parent_task_id
            FROM agent_task_index t
            LEFT JOIN agent_task_index p ON p.task_id = t.parent_task_id
            -- The queue lists what needs a decision now (r26 01 1.1). It used
            -- to also list every task that had ever been approved
            -- (OR t.approval_count > 0), which made an archive out of a queue:
            -- approving incremented the count, so a row could never leave.
            -- Approval history lives in the run ledger and on each row's own
            -- approval labels.
            WHERE t.status IN ('WaitingForUser', 'Blocked')
            ORDER BY t.updated_at DESC";
        var queue = new List<AgentReviewQueueItem>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            queue.Add(new AgentReviewQueueItem(
                r.GetString(0),
                r.GetString(1),
                ParseStatus(r.GetString(2)),
                ParseDate(r.GetString(3)),
                r.GetString(4),
                r.GetString(5),
                r.GetInt32(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetInt32(8) != 0,
                r.IsDBNull(9) ? null : ParseDate(r.GetString(9)),
                PendingToolAction: null,
                ParentGoal: r.IsDBNull(10) ? null : r.GetString(10),
                ParentTaskId: r.IsDBNull(11) ? null : r.GetString(11)));
        }

        // The index table is only a rebuildable summary. The full task state
        // is authoritative for whether an interaction still exists, whether
        // a child is owned by a parent, and whether the status has changed
        // since the index row was written.
        var actionable = new List<AgentReviewQueueItem>();
        foreach (var indexed in queue)
        {
            var full = await LoadAsync(indexed.TaskId, ct);
            if (full is null)
            {
                // The index is intentionally rebuildable but remains useful
                // when a task JSON file is unavailable. Preserve the legacy
                // status summary rather than silently dropping an owner row;
                // startup reconciliation repairs JSON-backed rows whenever it
                // can load their authoritative state.
                actionable.Add(indexed);
                continue;
            }

            if (full.Status is not (AgentTaskStatus.WaitingForUser or AgentTaskStatus.Blocked))
                continue;

            if (!HasActionableOwnerInteraction(full))
                continue;

            actionable.Add(indexed with
            {
                Status = full.Status,
                UpdatedAt = full.UpdatedAt,
                ActiveStep = full.ActiveStep,
                Summary = full.Summary,
                PendingToolAction = full.PendingToolAction,
                WorkspaceRoot = full.WorkspaceRoot is { Length: > 0 } ? full.WorkspaceRoot : null
            });
        }

        return actionable
            .OrderByDescending(item => item.UpdatedAt)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    public async Task AppendLogAsync(string taskId, string line, CancellationToken ct = default)
    {
        var dir = GetTaskDirectory(taskId);
        Directory.CreateDirectory(dir);
        await File.AppendAllTextAsync(Path.Combine(dir, "agent.log"), $"{DateTime.UtcNow:O} {line}{Environment.NewLine}", ct);
    }

    public async Task AppendTraceAsync(string taskId, object trace, CancellationToken ct = default)
    {
        var dir = GetTaskDirectory(taskId);
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(trace, AgentJson.CompactOptions);
        await File.AppendAllTextAsync(Path.Combine(dir, "agent.trace.jsonl"), json + Environment.NewLine, ct);
    }

    public async Task AppendTranscriptEntryAsync(string taskId, AgentTranscriptEntry entry, CancellationToken ct = default)
    {
        var dir = GetTaskDirectory(taskId);
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(entry, AgentJson.CompactOptions);
        await File.AppendAllTextAsync(Path.Combine(dir, "transcript.jsonl"), json + Environment.NewLine, ct);
    }

    public async Task<IReadOnlyList<AgentTranscriptEntry>> LoadTranscriptAsync(string taskId, CancellationToken ct = default)
    {
        var path = Path.Combine(GetTaskDirectory(taskId), "transcript.jsonl");
        if (!File.Exists(path))
            return [];

        var entries = new List<AgentTranscriptEntry>();
        foreach (var line in await File.ReadAllLinesAsync(path, ct))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<AgentTranscriptEntry>(line, AgentJson.CompactOptions);
                if (entry is not null)
                    entries.Add(entry);
            }
            catch (JsonException)
            {
                // A malformed line should not break replay of the rest of the transcript.
            }
        }

        return entries;
    }

    private static string NormalizeTaskId(string taskId)
    {
        var trimmed = taskId.Trim();
        var safeId = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(safeId)
            || !string.Equals(trimmed, safeId, StringComparison.Ordinal)
            || safeId is "." or ".."
            || safeId.Length > MaxTaskIdLength
            || !SafeTaskIdRegex.IsMatch(safeId)
            || WindowsReservedNames.Contains(safeId))
        {
            throw new InvalidOperationException("Agent task id is invalid.");
        }

        return safeId;
    }

    private string IndexPath => Path.Combine(AgentRoot, "task_index.db");
    private string IndexConnectionString => $"Data Source={IndexPath}";

    private async Task EnsureIndexInitializedAsync(CancellationToken ct)
    {
        var path = IndexPath;
        if (_initializedIndexPath == path && File.Exists(path))
            return;

        await _initGate.WaitAsync(ct);
        try
        {
            if (_initializedIndexPath == path && File.Exists(path))
                return;

            Directory.CreateDirectory(Path.Combine(AgentRoot, "tasks"));
            await using var c = new SqliteConnection(IndexConnectionString);
            await c.OpenAsync(ct);
            await using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = @"
                    PRAGMA journal_mode=WAL;
                    CREATE TABLE IF NOT EXISTS agent_task_index (
                        task_id TEXT PRIMARY KEY,
                        goal TEXT NOT NULL,
                        status TEXT NOT NULL,
                        updated_at TEXT NOT NULL,
                        active_step TEXT NOT NULL,
                        summary TEXT NOT NULL,
                        approval_count INTEGER NOT NULL DEFAULT 0,
                        last_approval_action TEXT,
                        last_approval_approved INTEGER,
                        last_approval_at TEXT,
                        parent_task_id TEXT,
                        pending_step_count INTEGER NOT NULL DEFAULT 0,
                        has_reservations INTEGER NOT NULL DEFAULT 0,
                        project_id TEXT NOT NULL DEFAULT '',
                        model_id TEXT NOT NULL DEFAULT '',
                        model_display_name TEXT NOT NULL DEFAULT ''
                    );
                    CREATE INDEX IF NOT EXISTS idx_agent_task_index_updated ON agent_task_index(updated_at DESC);
                    CREATE INDEX IF NOT EXISTS idx_agent_task_index_review ON agent_task_index(status, approval_count, updated_at DESC);";
                await cmd.ExecuteNonQueryAsync(ct);
            }

            var journalMode = await ReadJournalModeAsync(c, ct);
            _logs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Info,
                RuntimeLogCategory.Service,
                $"Agent task index opened with mode=read-write, pooling=provider-default, journal={journalMode}, schema_target={IndexSchemaVersion}",
                OperationCorrelation.NewId()));

            await SqliteMigrationRunner.ApplyAsync(c, "agent_task_index", IndexSchemaVersion,
            [
                new SqliteMigration(1, (_, _) => Task.FromResult(false)),
                new SqliteMigration(2, AddParentTaskIdColumnAsync),
                new SqliteMigration(3, AddPendingStepCountColumnAsync),
                new SqliteMigration(4, AddHasReservationsColumnAsync),
                new SqliteMigration(5, AddProjectIdColumnAsync),
                new SqliteMigration(6, AddModelIdentityColumnsAsync)
            ], ct);
            var recovered = await ReconcileStaleRunningTasksAsync(ct);
            await ReconcileIndexAsync(c, ct);
            if (recovered > 0)
            {
                _logs?.Add(new RuntimeLogEntry(
                    DateTime.UtcNow,
                    RuntimeLogLevel.Warning,
                    RuntimeLogCategory.Agent,
                    $"Agent startup recovery interrupted {recovered} persisted run(s); no active execution owner was present.",
                    OperationCorrelation.NewId()));
            }

            _initializedIndexPath = path;
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
        return Convert.ToString(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) ?? "Unknown";
    }

    private async Task<int> ReconcileStaleRunningTasksAsync(CancellationToken ct)
    {
        var states = new Dictionary<string, (string Path, AgentTaskState State)>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AgentRoot, "tasks"), "task_state.json", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var state = JsonSerializer.Deserialize<AgentTaskState>(json, AgentJson.Options);
                if (state is not null)
                    states[state.TaskId] = (file, state);
            }
            catch (JsonException)
            {
                // ReconcileIndexAsync retains the existing rule: one corrupt
                // task file must not prevent the remaining tasks from loading.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logs?.Add(new RuntimeLogEntry(
                    DateTime.UtcNow,
                    RuntimeLogLevel.Warning,
                    RuntimeLogCategory.Agent,
                    $"Agent startup recovery skipped an unreadable task state; exception={ex.GetType().Name}.",
                    OperationCorrelation.NewId()));
            }
        }

        var reason = "The previous Agent execution was interrupted because its execution context was not present during startup recovery. Continue it explicitly to resume.";
        var changed = new List<(string Path, AgentTaskState State)>();
        foreach (var entry in states.Values)
        {
            var stateChanged = false;
            if (entry.State.Status == AgentTaskStatus.Complete
                && entry.State.SubTaskPlan.Any(spec => spec.Status is AgentSubTaskStatus.Pending or AgentSubTaskStatus.Running))
            {
                MarkIncompleteOrchestration(entry.State);
                stateChanged = true;
            }

            if (entry.State.Status == AgentTaskStatus.Running)
            {
                MarkInterrupted(entry.State, reason);
                stateChanged = true;
            }

            if (entry.State.SubTaskPlan.Count > 0)
            {
                foreach (var spec in entry.State.SubTaskPlan)
                {
                    if (spec.Status != AgentSubTaskStatus.Running || string.IsNullOrWhiteSpace(spec.TaskId))
                        continue;

                    var child = states.TryGetValue(spec.TaskId, out var childEntry) ? childEntry.State : null;
                    if (child is null || child.Status != AgentTaskStatus.Interrupted)
                        continue;

                    spec.Status = AgentSubTaskStatus.Interrupted;
                    spec.ResultSummary = child.InterruptionReason;
                    stateChanged = true;
                }
            }

            var receiptRecovery = await ReconcileIncompleteReceiptsAsync(entry.State, ct);
            stateChanged |= receiptRecovery;

            if (stateChanged)
                changed.Add(entry);
        }

        foreach (var entry in ReconcileOwnerInteractions(states))
        {
            if (!changed.Any(existing => string.Equals(existing.State.TaskId, entry.State.TaskId, StringComparison.Ordinal)))
                changed.Add(entry);
        }

        foreach (var entry in changed)
        {
            ct.ThrowIfCancellationRequested();
            entry.State.UpdatedAt = DateTime.UtcNow;
            await AtomicFileWriter.WriteAllTextAsync(
                entry.Path,
                JsonSerializer.Serialize(entry.State, AgentJson.Options),
                ct);
        }

        return changed.Count(entry => entry.State.Status == AgentTaskStatus.Interrupted);
    }

    /// <summary>
    /// Resolves the only ambiguous crash window in a prepared mutation: the
    /// process may have written the target after the Pending receipt was saved
    /// but before the terminal outcome was persisted. A matching complete
    /// post-image is Applied, a matching pre-image remains Unknown, and any
    /// other content is Conflict. No outcome is replayed and no caller-provided
    /// workspace is trusted during startup recovery.
    /// </summary>
    private async Task<bool> ReconcileIncompleteReceiptsAsync(AgentTaskState state, CancellationToken ct)
    {
        var changed = false;
        foreach (var receipt in state.MutationReceipts.Where(receipt =>
                     receipt.Outcome == AgentMutationOutcome.Pending && receipt.FinishedAt == default))
        {
            ct.ThrowIfCancellationRequested();
            var root = receipt.WorkspaceRoot.Length > 0 ? receipt.WorkspaceRoot : state.WorkspaceRoot;
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(receipt.RelativePath)
                || receipt.MutationKind == AgentMutationKind.Command)
            {
                CompleteUnknownReceipt(receipt,
                    "The execution context disappeared before a safe post-image could be observed.");
                changed = true;
                continue;
            }

            string fullPath;
            try
            {
                root = AgentWorkspaceTools.ResolveWorkspaceRoot(root);
                fullPath = AgentWorkspaceTools.ResolveSafePath(root, receipt.RelativePath);
            }
            catch (Exception ex) when (ex is InvalidOperationException or DirectoryNotFoundException or ArgumentException)
            {
                CompleteUnknownReceipt(receipt, $"The post-image could not be safely resolved during startup recovery: {ex.Message}");
                changed = true;
                continue;
            }

            string? observed = null;
            try
            {
                if (File.Exists(fullPath))
                    observed = await File.ReadAllTextAsync(fullPath, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CompleteUnknownReceipt(receipt, $"The post-image could not be read during startup recovery: {ex.Message}");
                changed = true;
                continue;
            }

            receipt.ObservedPostImageExisted = observed is not null;
            receipt.ObservedPostImageSha256 = observed is null
                ? string.Empty
                : AgentMutationPreparation.ComputeContentSha256(observed);
            var postMatches = observed is not null
                && string.Equals(receipt.ObservedPostImageSha256, receipt.ProposedContentSha256, StringComparison.Ordinal);
            var preMatches = observed is null
                ? !receipt.ExpectedPreImageExisted
                : receipt.ExpectedPreImageExisted
                    && string.Equals(receipt.ObservedPostImageSha256, receipt.ExpectedPreImageSha256, StringComparison.Ordinal);
            receipt.Changed = receipt.ObservedPostImageExisted != receipt.ExpectedPreImageExisted
                || !string.Equals(receipt.ObservedPostImageSha256, receipt.ExpectedPreImageSha256, StringComparison.Ordinal);
            receipt.Verified = postMatches;
            if (postMatches)
            {
                receipt.Outcome = receipt.Changed ? AgentMutationOutcome.Applied : AgentMutationOutcome.AlreadySatisfied;
                receipt.CompletionReason = receipt.Changed
                    ? "Startup recovery matched the complete prepared post-image after an interrupted execution."
                    : "Startup recovery confirmed that the complete prepared output was already present.";
            }
            else
            {
                receipt.Outcome = preMatches ? AgentMutationOutcome.Unknown : AgentMutationOutcome.Conflict;
                receipt.CompletionReason = preMatches
                    ? "Startup recovery found the pre-image, so execution did not establish a post-image. No replay was attempted."
                    : "Startup recovery found content that matched neither the prepared pre-image nor post-image. No replay was attempted.";
            }

            receipt.FinishedAt = DateTime.UtcNow;
            receipt.EvidenceId = $"startup-recovery:{receipt.ReceiptId}";
            changed = true;
        }

        if (changed)
        {
            var incomplete = state.MutationReceipts.Any(receipt =>
                receipt.Outcome == AgentMutationOutcome.Pending && receipt.FinishedAt == default);
            if (!incomplete && state.Status == AgentTaskStatus.Running)
                MarkInterrupted(state,
                    "The previous Agent execution was interrupted; its prepared mutation receipt was reconciled without replay.");
            else if (!incomplete && state.PendingToolAction is not null)
                state.PendingToolAction = null;
        }

        return changed;
    }

    private static void CompleteUnknownReceipt(AgentMutationReceipt receipt, string reason)
    {
        receipt.Outcome = AgentMutationOutcome.Unknown;
        receipt.Verified = false;
        receipt.Changed = false;
        receipt.CompletionReason = reason;
        receipt.EvidenceId = $"startup-recovery:{receipt.ReceiptId}";
        receipt.FinishedAt = DateTime.UtcNow;
    }

    private static void MarkInterrupted(AgentTaskState state, string reason)
    {
        state.Status = AgentTaskStatus.Interrupted;
        state.InterruptionReason = reason;
        state.ActiveStep = "Interrupted during startup recovery";
        state.PendingToolAction = null;
        state.PlanApprovalPending = false;
        state.LastUserMessage = string.Empty;
        state.Decisions.Add(new AgentDecision("Execution interrupted", reason, DateTime.UtcNow));
        state.Summary = string.IsNullOrWhiteSpace(state.Summary)
            ? reason
            : $"{state.Summary} {reason}";

        foreach (var spec in state.SubTaskPlan.Where(spec => spec.Status == AgentSubTaskStatus.Running))
        {
            spec.Status = AgentSubTaskStatus.Interrupted;
            spec.ResultSummary = reason;
        }
    }

    private static void MarkIncompleteOrchestration(AgentTaskState state)
    {
        const string reason = "The parent run was marked complete while one or more required sub-tasks were still unfinished. Review the child states before continuing.";
        state.Status = AgentTaskStatus.Blocked;
        state.ActiveStep = "Blocked: unfinished sub-task plan requires review";
        state.LastUserMessage = reason;
        state.Decisions.Add(new AgentDecision("Incomplete orchestration recovered", reason, DateTime.UtcNow));
        state.Summary = string.IsNullOrWhiteSpace(state.Summary)
            ? reason
            : $"{state.Summary} {reason}";
    }

    private static IReadOnlyList<(string Path, AgentTaskState State)> ReconcileOwnerInteractions(
        IReadOnlyDictionary<string, (string Path, AgentTaskState State)> states)
    {
        var changed = new Dictionary<string, (string Path, AgentTaskState State)>(StringComparer.Ordinal);

        foreach (var entry in states.Values)
        {
            var state = entry.State;
            if (string.IsNullOrWhiteSpace(state.ParentTaskId))
                continue;

            var parentExists = states.TryGetValue(state.ParentTaskId, out var parentEntry)
                && parentEntry.State.SubTaskPlan.Any(spec => string.Equals(spec.TaskId, state.TaskId, StringComparison.Ordinal));
            if (parentExists)
                continue;

            const string reason = "This sub-task no longer has a persisted parent owner, so its pending interaction was closed during startup recovery.";
            var stateChanged = state.PendingToolAction is not null
                || state.PendingOwnerInteractions.Count > 0
                || state.PendingOwnerInteractionTaskId.Length > 0
                || state.PendingOwnerInteractionId.Length > 0;
            state.PendingToolAction = null;
            state.PendingOwnerInteractions.Clear();
            state.PendingOwnerInteractionTaskId = string.Empty;
            state.PendingOwnerInteractionId = string.Empty;
            state.LastUserMessage = string.Empty;
            if (state.Status is not (AgentTaskStatus.Complete or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled or AgentTaskStatus.Interrupted))
            {
                state.Status = AgentTaskStatus.Interrupted;
                state.InterruptionReason = reason;
                state.ActiveStep = "Interrupted: orphaned sub-task";
                state.Decisions.Add(new AgentDecision("Orphaned sub-task recovered", reason, DateTime.UtcNow));
                state.Summary = string.IsNullOrWhiteSpace(state.Summary) ? reason : $"{state.Summary} {reason}";
                stateChanged = true;
            }

            if (stateChanged)
                changed[state.TaskId] = entry;
        }

        foreach (var entry in states.Values)
        {
            var parent = entry.State;
            var desired = new List<AgentOwnerInteraction>();
            if (parent.SubTaskPlan.Count > 0)
            {
                foreach (var (spec, index) in parent.SubTaskPlan
                    .Select((spec, index) => (spec, index))
                    .Where(item => !string.IsNullOrWhiteSpace(item.spec.TaskId)))
                {
                    if (!states.TryGetValue(spec.TaskId!, out var childEntry)
                        || !string.Equals(childEntry.State.ParentTaskId, parent.TaskId, StringComparison.Ordinal))
                    {
                        if (spec.Status is AgentSubTaskStatus.Pending or AgentSubTaskStatus.Running)
                        {
                            spec.Status = AgentSubTaskStatus.Interrupted;
                            spec.ResultSummary = "The persisted sub-task state is missing, so this sub-task was closed during startup recovery.";
                            changed[parent.TaskId] = entry;
                        }
                        continue;
                    }

                    if (childEntry.State.PendingOwnerInteractions.Count > 0
                        || childEntry.State.PendingOwnerInteractionTaskId.Length > 0
                        || childEntry.State.PendingOwnerInteractionId.Length > 0)
                    {
                        childEntry.State.PendingOwnerInteractions.Clear();
                        childEntry.State.PendingOwnerInteractionTaskId = string.Empty;
                        childEntry.State.PendingOwnerInteractionId = string.Empty;
                        changed[childEntry.State.TaskId] = childEntry;
                    }

                    if (BuildOwnerInteraction(childEntry.State, index) is { } childInteraction)
                    {
                        PreserveInteractionIdentity(parent.PendingOwnerInteractions, childInteraction);
                        desired.Add(childInteraction);
                    }
                }

                if (desired.Count == 0 && string.IsNullOrWhiteSpace(parent.PendingOwnerInteractionTaskId)
                    && BuildOwnerInteraction(parent, sequence: -1) is { } ownInteraction)
                {
                    PreserveInteractionIdentity(parent.PendingOwnerInteractions, ownInteraction);
                    desired.Add(ownInteraction);
                }
            }
            else if (BuildOwnerInteraction(parent, sequence: -1) is { } ownInteraction)
            {
                PreserveInteractionIdentity(parent.PendingOwnerInteractions, ownInteraction);
                desired.Add(ownInteraction);
            }

            if (!OwnerInteractionListsEqual(parent.PendingOwnerInteractions, desired))
            {
                parent.PendingOwnerInteractions = desired;
                changed[parent.TaskId] = entry;
            }

            var selected = desired
                .OrderBy(item => item.Sequence)
                .ThenBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.InteractionId, StringComparer.Ordinal)
                .FirstOrDefault();
            var selectedSource = selected is null || selected.SourceTaskId == parent.TaskId
                ? string.Empty
                : selected.SourceTaskId;
            var selectedId = selected?.InteractionId ?? string.Empty;
            var expectedStatus = selected is null
                ? parent.Status
                : selected.Kind == AgentOwnerInteractionKind.Instruction
                    ? AgentTaskStatus.Blocked
                    : AgentTaskStatus.WaitingForUser;
            var expectedPrompt = selected?.Prompt ?? string.Empty;
            var expectedPending = selected?.PendingToolAction;
            var expectedActiveStep = selectedSource.Length == 0
                ? parent.ActiveStep
                : BuildChildActiveStep(parent, selectedSource);
            var mirrorChanged = !string.Equals(parent.PendingOwnerInteractionTaskId, selectedSource, StringComparison.Ordinal)
                || !string.Equals(parent.PendingOwnerInteractionId, selectedId, StringComparison.Ordinal)
                || !string.Equals(parent.LastUserMessage, expectedPrompt, StringComparison.Ordinal)
                || !string.Equals(
                    parent.PendingToolAction is null ? string.Empty : AgentApprovalFingerprint.Resolve(parent.PendingToolAction),
                    expectedPending is null ? string.Empty : AgentApprovalFingerprint.Resolve(expectedPending),
                    StringComparison.Ordinal)
                || (selected is not null && parent.Status != expectedStatus);
            if (selected is null)
            {
                if (parent.PendingOwnerInteractionTaskId.Length > 0 || parent.PendingOwnerInteractionId.Length > 0
                    || parent.PendingToolAction is not null || parent.LastUserMessage.Length > 0)
                    mirrorChanged = true;
                parent.PendingOwnerInteractionTaskId = string.Empty;
                parent.PendingOwnerInteractionId = string.Empty;
                parent.PendingToolAction = null;
                parent.LastUserMessage = string.Empty;
                if (parent.SubTaskPlan.Any(spec => spec.Status is AgentSubTaskStatus.Pending or AgentSubTaskStatus.Running)
                    && (parent.Status is AgentTaskStatus.WaitingForUser or AgentTaskStatus.Blocked))
                {
                    parent.Status = AgentTaskStatus.Running;
                    mirrorChanged = true;
                }
            }
            else
            {
                parent.PendingOwnerInteractionTaskId = selectedSource;
                parent.PendingOwnerInteractionId = selectedId;
                parent.PendingToolAction = ClonePendingToolAction(expectedPending);
                parent.LastUserMessage = expectedPrompt;
                parent.Status = expectedStatus;
                if (selectedSource.Length > 0)
                    parent.ActiveStep = expectedActiveStep;
            }

            if (mirrorChanged)
                changed[parent.TaskId] = entry;
        }

        return changed.Values.ToList();
    }

    private static AgentOwnerInteraction? BuildOwnerInteraction(AgentTaskState state, int sequence)
    {
        if (state.Status == AgentTaskStatus.WaitingForUser && state.PendingToolAction is { } pending)
        {
            return new AgentOwnerInteraction
            {
                SourceTaskId = state.TaskId,
                Kind = AgentOwnerInteractionKind.Approval,
                SourceStatus = state.Status,
                Prompt = string.IsNullOrWhiteSpace(state.LastUserMessage)
                    ? string.IsNullOrWhiteSpace(pending.Reason) ? "Review the pending action." : pending.Reason
                    : state.LastUserMessage,
                PendingToolAction = ClonePendingToolAction(pending),
                ProposalId = pending.ProposalId,
                ProposalRevision = pending.ProposalRevision,
                Fingerprint = AgentApprovalFingerprint.Resolve(pending),
                SourceStepCount = state.StepCount,
                Sequence = sequence
            };
        }

        if (state.Status == AgentTaskStatus.WaitingForUser && state.LastUserMessage.Length > 0)
        {
            return new AgentOwnerInteraction
            {
                SourceTaskId = state.TaskId,
                Kind = AgentOwnerInteractionKind.Question,
                SourceStatus = state.Status,
                Prompt = state.LastUserMessage,
                SourceStepCount = state.StepCount,
                Sequence = sequence
            };
        }

        if (state.Status == AgentTaskStatus.Blocked
            && state.UserTransitions.LastOrDefault()?.Kind != AgentTaskTransitionKind.StopRun)
        {
            var prompt = state.LastUserMessage.Length > 0 ? state.LastUserMessage : state.ActiveStep;
            if (prompt.Length == 0)
                return null;
            return new AgentOwnerInteraction
            {
                SourceTaskId = state.TaskId,
                Kind = AgentOwnerInteractionKind.Instruction,
                SourceStatus = state.Status,
                Prompt = prompt,
                SourceStepCount = state.StepCount,
                Sequence = sequence
            };
        }

        return null;
    }

    private static void PreserveInteractionIdentity(
        IEnumerable<AgentOwnerInteraction> existing,
        AgentOwnerInteraction desired)
    {
        var prior = existing.FirstOrDefault(item => string.Equals(item.SourceTaskId, desired.SourceTaskId, StringComparison.Ordinal));
        if (prior is null)
            return;
        desired.InteractionId = prior.InteractionId;
        desired.CreatedAtUtc = prior.CreatedAtUtc;
    }

    private static bool OwnerInteractionListsEqual(
        IReadOnlyList<AgentOwnerInteraction> left,
        IReadOnlyList<AgentOwnerInteraction> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair =>
            string.Equals(pair.First.InteractionId, pair.Second.InteractionId, StringComparison.Ordinal)
            && string.Equals(pair.First.SourceTaskId, pair.Second.SourceTaskId, StringComparison.Ordinal)
            && pair.First.Kind == pair.Second.Kind
            && pair.First.SourceStatus == pair.Second.SourceStatus
            && string.Equals(pair.First.Prompt, pair.Second.Prompt, StringComparison.Ordinal)
            && pair.First.SourceStepCount == pair.Second.SourceStepCount
            && pair.First.Sequence == pair.Second.Sequence
            && string.Equals(
                pair.First.PendingToolAction is null ? string.Empty : AgentApprovalFingerprint.Resolve(pair.First.PendingToolAction),
                pair.Second.PendingToolAction is null ? string.Empty : AgentApprovalFingerprint.Resolve(pair.Second.PendingToolAction),
                StringComparison.Ordinal));

    private static string BuildChildActiveStep(AgentTaskState parent, string childTaskId)
    {
        var index = parent.SubTaskPlan.FindIndex(item => string.Equals(item.TaskId, childTaskId, StringComparison.Ordinal));
        var goal = index >= 0 ? parent.SubTaskPlan[index].Goal : childTaskId;
        return $"Waiting on sub-task {index + 1}/{parent.SubTaskPlan.Count}: {goal}";
    }

    private static AgentPendingToolAction? ClonePendingToolAction(AgentPendingToolAction? pending) => pending is null
        ? null
        : new AgentPendingToolAction
        {
            ToolName = pending.ToolName,
            Arguments = AgentMutationPreparation.CloneArguments(pending.Arguments),
            RiskLevel = pending.RiskLevel,
            RequestedAt = pending.RequestedAt,
            Reason = pending.Reason,
            Fingerprint = pending.Fingerprint,
            RequestedActionFingerprint = AgentApprovalFingerprint.ResolveRequestedAction(pending),
            ProposalId = pending.ProposalId,
            ProposalRevision = pending.ProposalRevision,
            SchemaVersion = pending.SchemaVersion,
            WorkspaceRoot = pending.WorkspaceRoot,
            MutationKind = pending.MutationKind,
            RelativePath = pending.RelativePath,
            ExpectedPreImageSha256 = pending.ExpectedPreImageSha256,
            ExpectedPreImageExisted = pending.ExpectedPreImageExisted,
            ProposedContent = pending.ProposedContent,
            ProposedContentSha256 = pending.ProposedContentSha256,
            PolicyFingerprint = pending.PolicyFingerprint,
            PreparedAt = pending.PreparedAt
        };

    private static bool HasActionableOwnerInteraction(AgentTaskState state)
    {
        if (state.PendingOwnerInteractionTaskId.Length > 0)
            return state.PendingOwnerInteractions.Any(item =>
                string.Equals(item.InteractionId, state.PendingOwnerInteractionId, StringComparison.Ordinal)
                && item.Kind is AgentOwnerInteractionKind.Approval or AgentOwnerInteractionKind.Question or AgentOwnerInteractionKind.Instruction);

        return state.Status switch
        {
            // WaitingForUser is itself the durable owner-facing pause marker.
            // Older task files may not have the additive interaction mirror or
            // a copied prompt, but must remain reviewable by status.
            AgentTaskStatus.WaitingForUser => true,
            AgentTaskStatus.Blocked => state.UserTransitions.LastOrDefault()?.Kind != AgentTaskTransitionKind.StopRun,
            _ => false
        };
    }

    /// <summary>Additive schema change for r15 sub-task orchestration (doc 01 1.1): a fresh install already gets the column from CREATE TABLE, so this only matters for a pre-r15 index file.</summary>
    private static async Task<bool> AddParentTaskIdColumnAsync(SqliteConnection c, CancellationToken ct)
    {
        await using (var check = c.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('agent_task_index') WHERE name = 'parent_task_id'";
            var exists = Convert.ToInt64(await check.ExecuteScalarAsync(ct)) > 0;
            if (exists) return false;
        }

        await using var alter = c.CreateCommand();
        alter.CommandText = "ALTER TABLE agent_task_index ADD COLUMN parent_task_id TEXT";
        await alter.ExecuteNonQueryAsync(ct);
        return true;
    }

    /// <summary>Additive schema change for r19 3.3 (premature-complete honesty note): a fresh
    /// install already gets the column from CREATE TABLE, so this only matters for a pre-r19 index file.</summary>
    private static async Task<bool> AddPendingStepCountColumnAsync(SqliteConnection c, CancellationToken ct)
    {
        await using (var check = c.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('agent_task_index') WHERE name = 'pending_step_count'";
            var exists = Convert.ToInt64(await check.ExecuteScalarAsync(ct)) > 0;
            if (exists) return false;
        }

        await using var alter = c.CreateCommand();
        alter.CommandText = "ALTER TABLE agent_task_index ADD COLUMN pending_step_count INTEGER NOT NULL DEFAULT 0";
        await alter.ExecuteNonQueryAsync(ct);
        return true;
    }

    /// <summary>Additive schema change for r23 2.3 ("Completed with reservations"): a fresh
    /// install already gets the column from CREATE TABLE, so this only matters for a pre-r23 index file.</summary>
    private static async Task<bool> AddHasReservationsColumnAsync(SqliteConnection c, CancellationToken ct)
    {
        await using (var check = c.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('agent_task_index') WHERE name = 'has_reservations'";
            var exists = Convert.ToInt64(await check.ExecuteScalarAsync(ct)) > 0;
            if (exists) return false;
        }

        await using var alter = c.CreateCommand();
        alter.CommandText = "ALTER TABLE agent_task_index ADD COLUMN has_reservations INTEGER NOT NULL DEFAULT 0";
        await alter.ExecuteNonQueryAsync(ct);
        return true;
    }

    /// <summary>Additive schema change for r24 doc 01 1.2: a fresh install already gets
    /// the column from CREATE TABLE, so this only matters for a pre-r24 index file.</summary>
    private static async Task<bool> AddProjectIdColumnAsync(SqliteConnection c, CancellationToken ct)
    {
        await using (var check = c.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('agent_task_index') WHERE name = 'project_id'";
            var exists = Convert.ToInt64(await check.ExecuteScalarAsync(ct)) > 0;
            if (exists) return false;
        }

        await using var alter = c.CreateCommand();
        alter.CommandText = "ALTER TABLE agent_task_index ADD COLUMN project_id TEXT NOT NULL DEFAULT ''";
        await alter.ExecuteNonQueryAsync(ct);
        return true;
    }

    private static async Task<bool> AddModelIdentityColumnsAsync(SqliteConnection c, CancellationToken ct)
    {
        var changed = false;
        foreach (var (name, definition) in new[]
        {
            ("model_id", "TEXT NOT NULL DEFAULT ''"),
            ("model_display_name", "TEXT NOT NULL DEFAULT ''")
        })
        {
            await using var check = c.CreateCommand();
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('agent_task_index') WHERE name = '{name}'";
            if (Convert.ToInt64(await check.ExecuteScalarAsync(ct)) > 0) continue;
            await using var alter = c.CreateCommand();
            alter.CommandText = $"ALTER TABLE agent_task_index ADD COLUMN {name} {definition}";
            await alter.ExecuteNonQueryAsync(ct);
            changed = true;
        }
        return changed;
    }

    private async Task ReconcileIndexAsync(SqliteConnection c, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AgentRoot, "tasks"), "task_state.json", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var state = JsonSerializer.Deserialize<AgentTaskState>(json, AgentJson.Options);
                if (state is not null)
                    await UpsertIndexAsync(c, state, ct);
            }
            catch
            {
                // Ignore corrupt task state entries so one bad task cannot hide the rest.
            }
        }
    }

    private async Task UpsertIndexAsync(AgentTaskState state, CancellationToken ct)
    {
        await using var c = new SqliteConnection(IndexConnectionString);
        await c.OpenAsync(ct);
        await UpsertIndexAsync(c, state, ct);
    }

    private static async Task UpsertIndexAsync(SqliteConnection c, AgentTaskState state, CancellationToken ct)
    {
        var approvals = state.ApprovalHistory.OrderByDescending(a => a.Timestamp).ToList();
        var last = approvals.FirstOrDefault();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO agent_task_index (
                task_id, goal, status, updated_at, active_step, summary,
                approval_count, last_approval_action, last_approval_approved, last_approval_at, parent_task_id, pending_step_count, has_reservations, project_id, model_id, model_display_name)
            VALUES (
                $task_id, $goal, $status, $updated_at, $active_step, $summary,
                $approval_count, $last_action, $last_approved, $last_at, $parent_task_id, $pending_step_count, $has_reservations, $project_id, $model_id, $model_display_name)
            ON CONFLICT(task_id) DO UPDATE SET
                goal = excluded.goal,
                status = excluded.status,
                updated_at = excluded.updated_at,
                active_step = excluded.active_step,
                summary = excluded.summary,
                approval_count = excluded.approval_count,
                last_approval_action = excluded.last_approval_action,
                last_approval_approved = excluded.last_approval_approved,
                last_approval_at = excluded.last_approval_at,
                parent_task_id = excluded.parent_task_id,
                pending_step_count = excluded.pending_step_count,
                has_reservations = excluded.has_reservations,
                project_id = excluded.project_id,
                model_id = excluded.model_id,
                model_display_name = excluded.model_display_name";
        cmd.Parameters.AddWithValue("$task_id", state.TaskId);
        cmd.Parameters.AddWithValue("$goal", state.Goal);
        cmd.Parameters.AddWithValue("$status", state.Status.ToString());
        cmd.Parameters.AddWithValue("$updated_at", state.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$active_step", state.ActiveStep);
        cmd.Parameters.AddWithValue("$summary", state.Summary);
        cmd.Parameters.AddWithValue("$approval_count", approvals.Count);
        cmd.Parameters.AddWithValue("$last_action", (object?)last?.Action ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$last_approved", last is null ? DBNull.Value : last.Approved ? 1 : 0);
        cmd.Parameters.AddWithValue("$last_at", last is null ? DBNull.Value : last.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("$parent_task_id", (object?)state.ParentTaskId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pending_step_count", state.PendingSteps.Count);
        cmd.Parameters.AddWithValue("$has_reservations", state.Reservations.Count > 0 ? 1 : 0);
        cmd.Parameters.AddWithValue("$project_id", state.ProjectId);
        cmd.Parameters.AddWithValue("$model_id", state.ModelId);
        cmd.Parameters.AddWithValue("$model_display_name", state.ModelDisplayName);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static AgentTaskStatus ParseStatus(string value) =>
        Enum.TryParse<AgentTaskStatus>(value, ignoreCase: true, out var status) ? status : AgentTaskStatus.New;

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? date
            : DateTime.MinValue;
}
