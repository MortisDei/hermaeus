using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Microsoft.Data.Sqlite;

namespace Hermaeus.Services;

/// <summary>
/// SQLite-backed unified trace store (traces.db in the data root).
/// Retention is enforced per kind on every append.
/// </summary>
public sealed class SqliteTraceStore : ITraceStore
{
    private const int SchemaVersion = 2;
    internal const int MaxTracesPerKind = 500;

    private readonly ISettingsService _settings;
    private string _initializedPath = string.Empty;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    private string DbPath
    {
        get
        {
            var dir = SettingsService.ResolveDataRoot(_settings.Settings);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "traces.db");
        }
    }

    private string Cs => $"Data Source={DbPath}";

    public SqliteTraceStore(ISettingsService settings)
    {
        _settings = settings;
    }

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
            CREATE TABLE IF NOT EXISTS traces (
                id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                created_at TEXT NOT NULL,
                source_id TEXT NOT NULL DEFAULT '',
                model_id TEXT NOT NULL DEFAULT '',
                operation TEXT NOT NULL DEFAULT '',
                first_token_ms INTEGER NOT NULL DEFAULT 0,
                total_latency_ms INTEGER NOT NULL DEFAULT 0,
                prompt_tokens INTEGER NOT NULL DEFAULT 0,
                completion_tokens INTEGER NOT NULL DEFAULT 0,
                total_tokens INTEGER NOT NULL DEFAULT 0,
                error TEXT NOT NULL DEFAULT '',
                detail_json TEXT NOT NULL DEFAULT '{}'
            );
            CREATE INDEX IF NOT EXISTS idx_traces_kind_created ON traces(kind, created_at DESC);";
            await cmd.ExecuteNonQueryAsync(ct);
            await SqliteMigrationRunner.ApplyAsync(c, "traces", SchemaVersion,
            [
                new SqliteMigration(1, (_, _) => Task.FromResult(false)),
                new SqliteMigration(2, CreateModelUsageTableAsync)
            ], ct);
            _initializedPath = dbPath;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task AppendAsync(TraceRecord trace, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);

        var cmd = c.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = @"
            INSERT INTO traces
                (id,kind,created_at,source_id,model_id,operation,first_token_ms,total_latency_ms,prompt_tokens,completion_tokens,total_tokens,error,detail_json)
            VALUES
                ($id,$kind,$created,$source,$model,$op,$first,$total,$pt,$ct,$tt,$err,$detail)";
        cmd.Parameters.AddWithValue("$id", trace.Id);
        cmd.Parameters.AddWithValue("$kind", trace.Kind.ToString());
        cmd.Parameters.AddWithValue("$created", trace.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$source", trace.SourceId);
        cmd.Parameters.AddWithValue("$model", trace.ModelId);
        cmd.Parameters.AddWithValue("$op", trace.Operation);
        cmd.Parameters.AddWithValue("$first", trace.FirstTokenMs);
        cmd.Parameters.AddWithValue("$total", trace.TotalLatencyMs);
        cmd.Parameters.AddWithValue("$pt", trace.PromptTokens);
        cmd.Parameters.AddWithValue("$ct", trace.CompletionTokens);
        cmd.Parameters.AddWithValue("$tt", trace.TotalTokens);
        cmd.Parameters.AddWithValue("$err", trace.Error);
        cmd.Parameters.AddWithValue("$detail", trace.DetailJson);
        await cmd.ExecuteNonQueryAsync(ct);

        var prune = c.CreateCommand();
        prune.Transaction = (SqliteTransaction)tx;
        prune.CommandText = @"
            DELETE FROM traces
            WHERE kind = $kind AND id NOT IN (
                SELECT id FROM traces WHERE kind = $kind
                ORDER BY created_at DESC LIMIT $keep)";
        prune.Parameters.AddWithValue("$kind", trace.Kind.ToString());
        prune.Parameters.AddWithValue("$keep", MaxTracesPerKind);
        await prune.ExecuteNonQueryAsync(ct);

        // model_usage is a durable rollup, never pruned by the traces
        // retention above, so long-run usage patterns survive trace
        // pruning (r6 02-usage-history-recommendations.md 2.1).
        if (!string.IsNullOrWhiteSpace(trace.ModelId))
        {
            var usage = c.CreateCommand();
            usage.Transaction = (SqliteTransaction)tx;
            usage.CommandText = @"
                INSERT INTO model_usage (kind, model_id, day, call_count, total_tokens)
                VALUES ($kind, $model, $day, 1, $tokens)
                ON CONFLICT(kind, model_id, day) DO UPDATE SET
                    call_count = call_count + 1,
                    total_tokens = total_tokens + excluded.total_tokens";
            usage.Parameters.AddWithValue("$kind", trace.Kind.ToString());
            usage.Parameters.AddWithValue("$model", trace.ModelId);
            usage.Parameters.AddWithValue("$day", trace.CreatedAt.ToString("yyyy-MM-dd"));
            usage.Parameters.AddWithValue("$tokens", trace.TotalTokens);
            await usage.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Per-model daily call/token totals over the trailing window, read
    /// directly from the durable model_usage rollup (never pruned, unlike
    /// the capped traces table). Used by <see cref="IModelUsageService"/>.
    /// </summary>
    public async Task<List<ModelUsageRow>> GetModelUsageAsync(TraceKind? kind, int days, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);

        var since = DateTime.UtcNow.Date.AddDays(-Math.Max(0, days - 1)).ToString("yyyy-MM-dd");
        var cmd = c.CreateCommand();
        cmd.CommandText = kind is null
            ? "SELECT kind, model_id, SUM(call_count), SUM(total_tokens) FROM model_usage WHERE day >= $since GROUP BY kind, model_id"
            : "SELECT kind, model_id, SUM(call_count), SUM(total_tokens) FROM model_usage WHERE day >= $since AND kind = $kind GROUP BY kind, model_id";
        cmd.Parameters.AddWithValue("$since", since);
        if (kind is not null)
            cmd.Parameters.AddWithValue("$kind", kind.Value.ToString());

        var result = new List<ModelUsageRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new ModelUsageRow(
                Enum.TryParse<TraceKind>(r.GetString(0), out var k) ? k : TraceKind.Chat,
                r.GetString(1),
                r.GetInt64(2),
                r.GetInt64(3)));
        }

        return result;
    }

    private static async Task<bool> CreateModelUsageTableAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS model_usage (
                kind TEXT NOT NULL,
                model_id TEXT NOT NULL,
                day TEXT NOT NULL,
                call_count INTEGER NOT NULL DEFAULT 0,
                total_tokens INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (kind, model_id, day)
            );
            CREATE INDEX IF NOT EXISTS idx_model_usage_kind_day ON model_usage(kind, day);";
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    public async Task<List<TraceRecord>> GetRecentAsync(TraceKind? kind = null, int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var c = new SqliteConnection(Cs);
        await c.OpenAsync(ct);

        var cmd = c.CreateCommand();
        cmd.CommandText = kind is null
            ? "SELECT id,kind,created_at,source_id,model_id,operation,first_token_ms,total_latency_ms,prompt_tokens,completion_tokens,total_tokens,error,detail_json FROM traces ORDER BY created_at DESC LIMIT $limit"
            : "SELECT id,kind,created_at,source_id,model_id,operation,first_token_ms,total_latency_ms,prompt_tokens,completion_tokens,total_tokens,error,detail_json FROM traces WHERE kind = $kind ORDER BY created_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));
        if (kind is not null)
            cmd.Parameters.AddWithValue("$kind", kind.Value.ToString());

        var result = new List<TraceRecord>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new TraceRecord
            {
                Id = r.GetString(0),
                Kind = Enum.TryParse<TraceKind>(r.GetString(1), out var k) ? k : TraceKind.Chat,
                CreatedAt = DateTime.Parse(r.GetString(2)).ToUniversalTime(),
                SourceId = r.GetString(3),
                ModelId = r.GetString(4),
                Operation = r.GetString(5),
                FirstTokenMs = r.GetInt64(6),
                TotalLatencyMs = r.GetInt64(7),
                PromptTokens = r.GetInt32(8),
                CompletionTokens = r.GetInt32(9),
                TotalTokens = r.GetInt32(10),
                Error = r.GetString(11),
                DetailJson = r.GetString(12)
            });
        }

        return result;
    }
}
