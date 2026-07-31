using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Microsoft.Data.Sqlite;

namespace Hermaeus.Services;

/// <summary>
/// SQLite-backed shared store for the Evaluation System (eval_runs.db in the
/// data root). Additive alongside benchmarks.db; see
/// docs/review/10-evaluation-system.md step 1.
/// </summary>
public sealed class SqliteEvalStore : IEvalStore
{
    private const int SchemaVersion = 1;
    internal const int MaxSavedRuns = 500;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly ISettingsService _settings;
    private string _initializedPath = string.Empty;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    private string DbPath
    {
        get
        {
            var dir = SettingsService.ResolveDataRoot(_settings.Settings);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "eval_runs.db");
        }
    }

    private string Cs => $"Data Source={DbPath}";

    public SqliteEvalStore(ISettingsService settings)
    {
        _settings = settings;
    }

    public async Task InitializeAsync(CancellationToken ct = default) => await EnsureInitializedAsync(ct);

    private async Task EnsureInitializedAsync(CancellationToken ct)
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
            CREATE TABLE IF NOT EXISTS eval_runs (
                id TEXT PRIMARY KEY,
                mode TEXT NOT NULL,
                model_id TEXT NOT NULL DEFAULT '',
                dataset_id TEXT NOT NULL DEFAULT '',
                suite_id TEXT NOT NULL DEFAULT '',
                started_at TEXT NOT NULL,
                finished_at TEXT NOT NULL DEFAULT '',
                run_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_eval_runs_mode_started ON eval_runs(mode, started_at DESC);";
            await cmd.ExecuteNonQueryAsync(ct);
            await SqliteMigrationRunner.ApplyAsync(c, "eval_runs", SchemaVersion,
            [
                new SqliteMigration(1, (_, _) => Task.FromResult(false))
            ], ct);
            _initializedPath = dbPath;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task SaveRunAsync(EvalRun run, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        // One transaction for the insert and its prune, matching
        // SqliteTraceStore.AppendAsync. Two implicit transactions meant two
        // durable commits per saved run, and a crash between them left the
        // table over its cap. r28 doc 04 4.1's per-test timings named the
        // retention test as one of the two slowest on the Windows leg, where a
        // commit is far more expensive than on Linux, which is what made this
        // visible.
        await using var tx = await c.BeginTransactionAsync(ct);

        var cmd = c.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = @"
            INSERT INTO eval_runs (id,mode,model_id,dataset_id,suite_id,started_at,finished_at,run_json)
            VALUES ($id,$mode,$model,$dataset,$suite,$started,$finished,$json)
            ON CONFLICT(id) DO UPDATE SET finished_at=excluded.finished_at, run_json=excluded.run_json";
        cmd.Parameters.AddWithValue("$id", run.Id);
        cmd.Parameters.AddWithValue("$mode", run.Mode.ToString());
        cmd.Parameters.AddWithValue("$model", run.Target.ModelId);
        cmd.Parameters.AddWithValue("$dataset", run.Target.DatasetId ?? string.Empty);
        cmd.Parameters.AddWithValue("$suite", run.SuiteId ?? string.Empty);
        cmd.Parameters.AddWithValue("$started", run.StartedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$finished", run.FinishedAt?.ToString("O") ?? string.Empty);
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(run, JsonOpts));
        await cmd.ExecuteNonQueryAsync(ct);

        var prune = c.CreateCommand();
        prune.Transaction = (SqliteTransaction)tx;
        prune.CommandText = @"
            DELETE FROM eval_runs WHERE id NOT IN (
                SELECT id FROM eval_runs ORDER BY started_at DESC LIMIT $keep)";
        prune.Parameters.AddWithValue("$keep", MaxSavedRuns);
        await prune.ExecuteNonQueryAsync(ct);

        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<EvalRun>> GetRunsAsync(EvalMode? mode = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);

        var cmd = c.CreateCommand();
        cmd.CommandText = mode is null
            ? "SELECT run_json FROM eval_runs ORDER BY started_at DESC"
            : "SELECT run_json FROM eval_runs WHERE mode = $mode ORDER BY started_at DESC";
        if (mode is not null)
            cmd.Parameters.AddWithValue("$mode", mode.Value.ToString());

        var runs = new List<EvalRun>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var run = JsonSerializer.Deserialize<EvalRun>(r.GetString(0), JsonOpts);
            if (run is not null)
                runs.Add(run);
        }

        return runs;
    }

    public async Task<EvalRun?> GetRunAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);

        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT run_json FROM eval_runs WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is string json ? JsonSerializer.Deserialize<EvalRun>(json, JsonOpts) : null;
    }
}
