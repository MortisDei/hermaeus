using Aether.Core.Models;
using Aether.Core.Services;
using Microsoft.Data.Sqlite;

namespace Aether.Services;

/// <summary>
/// SQLite-backed unified trace store (traces.db in the data root).
/// Retention is enforced per kind on every append.
/// </summary>
public sealed class SqliteTraceStore : ITraceStore
{
    private const int SchemaVersion = 1;
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
                new SqliteMigration(1, (_, _) => Task.FromResult(false))
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

        var cmd = c.CreateCommand();
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
        prune.CommandText = @"
            DELETE FROM traces
            WHERE kind = $kind AND id NOT IN (
                SELECT id FROM traces WHERE kind = $kind
                ORDER BY created_at DESC LIMIT $keep)";
        prune.Parameters.AddWithValue("$kind", trace.Kind.ToString());
        prune.Parameters.AddWithValue("$keep", MaxTracesPerKind);
        await prune.ExecuteNonQueryAsync(ct);
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
