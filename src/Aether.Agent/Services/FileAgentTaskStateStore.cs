using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aether.Agent.Models;
using Aether.Core.Services;
using Microsoft.Data.Sqlite;

namespace Aether.Agent.Services;

public sealed class FileAgentTaskStateStore : IAgentTaskStateStore
{
    private const int IndexSchemaVersion = 2;
    private const int MaxTaskIdLength = 80;
    private static readonly Regex SafeTaskIdRegex = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9"
    };
    private readonly ISettingsService _settings;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private string _initializedIndexPath = string.Empty;

    public FileAgentTaskStateStore(ISettingsService settings)
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

    public async Task<IReadOnlyList<AgentTaskListItem>> ListRecentAsync(int limit = 25, CancellationToken ct = default)
    {
        await EnsureIndexInitializedAsync(ct);
        await using var c = new SqliteConnection(IndexConnectionString);
        await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = @"
            SELECT task_id, goal, status, updated_at, parent_task_id
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
                r.IsDBNull(4) ? null : r.GetString(4)));
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
            WHERE t.status IN ('WaitingForUser', 'Blocked') OR t.approval_count > 0
            ORDER BY t.updated_at DESC
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));
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

        // The index table only carries summary columns; a task actually
        // waiting on an approval needs its PendingToolAction, which only
        // exists in the full per-task state file.
        for (var i = 0; i < queue.Count; i++)
        {
            if (queue[i].Status is not (AgentTaskStatus.WaitingForUser or AgentTaskStatus.Blocked))
                continue;

            var full = await LoadAsync(queue[i].TaskId, ct);
            if (full is null) continue;
            queue[i] = queue[i] with
            {
                PendingToolAction = full.PendingToolAction,
                WorkspaceRoot = full.WorkspaceRoot is { Length: > 0 } ? full.WorkspaceRoot : null
            };
        }

        return queue;
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
                        parent_task_id TEXT
                    );
                    CREATE INDEX IF NOT EXISTS idx_agent_task_index_updated ON agent_task_index(updated_at DESC);
                    CREATE INDEX IF NOT EXISTS idx_agent_task_index_review ON agent_task_index(status, approval_count, updated_at DESC);";
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await SqliteMigrationRunner.ApplyAsync(c, "agent_task_index", IndexSchemaVersion,
            [
                new SqliteMigration(1, (_, _) => Task.FromResult(false)),
                new SqliteMigration(2, AddParentTaskIdColumnAsync)
            ], ct);
            await ReconcileIndexAsync(c, ct);

            _initializedIndexPath = path;
        }
        finally
        {
            _initGate.Release();
        }
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
                approval_count, last_approval_action, last_approval_approved, last_approval_at, parent_task_id)
            VALUES (
                $task_id, $goal, $status, $updated_at, $active_step, $summary,
                $approval_count, $last_action, $last_approved, $last_at, $parent_task_id)
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
                parent_task_id = excluded.parent_task_id";
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
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static AgentTaskStatus ParseStatus(string value) =>
        Enum.TryParse<AgentTaskStatus>(value, ignoreCase: true, out var status) ? status : AgentTaskStatus.New;

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? date
            : DateTime.MinValue;
}
