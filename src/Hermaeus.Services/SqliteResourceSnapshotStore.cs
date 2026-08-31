using System.Text;
using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Microsoft.Data.Sqlite;

namespace Hermaeus.Services;

/// <summary>
/// Bounded persistence for explicit resource snapshots. It stores a path-free
/// projection in experience.db and never records a continuous sampling stream.
/// </summary>
public sealed class SqliteResourceSnapshotStore : IResourceSnapshotStore
{
    private const int SchemaVersion = 1;
    private const int MaximumSnapshots = 32;
    private const int MaximumPayloadBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISettingsService _settings;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private string _initializedPath = string.Empty;

    public SqliteResourceSnapshotStore(ISettingsService settings) => _settings = settings;

    private string DbPath => Path.Combine(SettingsService.ResolveDataRoot(_settings.Settings), "experience.db");
    private string ConnectionString => $"Data Source={DbPath}";

    public async Task SaveAsync(ResourceSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var document = ResourceSnapshotPersistenceProjection.Project(snapshot);
        var payload = JsonSerializer.Serialize(document, JsonOptions);
        if (Encoding.UTF8.GetByteCount(payload) > MaximumPayloadBytes)
            throw new InvalidOperationException($"Resource snapshot exceeds the {MaximumPayloadBytes}-byte persistence bound.");

        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "INSERT INTO resource_snapshots (id,captured_at,payload) VALUES ($id,$captured,$payload)";
        command.Parameters.AddWithValue("$id", document.SnapshotId);
        command.Parameters.AddWithValue("$captured", document.CapturedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$payload", payload);
        await command.ExecuteNonQueryAsync(ct);

        var prune = connection.CreateCommand();
        prune.Transaction = (SqliteTransaction)transaction;
        prune.CommandText = "DELETE FROM resource_snapshots WHERE id IN (SELECT id FROM resource_snapshots ORDER BY captured_at DESC LIMIT -1 OFFSET $keep)";
        prune.Parameters.AddWithValue("$keep", MaximumSnapshots);
        await prune.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<PersistedResourceSnapshot>> LoadRecentAsync(int maximum = MaximumSnapshots, CancellationToken ct = default)
    {
        maximum = Math.Clamp(maximum, 1, MaximumSnapshots);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM resource_snapshots ORDER BY captured_at DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", maximum);
        var rows = new List<PersistedResourceSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var payload = reader.GetString(0);
            var document = JsonSerializer.Deserialize<PersistedResourceSnapshot>(payload, JsonOptions);
            if (document is not null)
                rows.Add(document);
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
            await SqliteMigrationRunner.ApplyAsync(connection, "resource_intelligence", SchemaVersion,
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
            CREATE TABLE IF NOT EXISTS resource_snapshots (
                id TEXT PRIMARY KEY,
                captured_at TEXT NOT NULL,
                payload TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_resource_snapshots_captured_at
                ON resource_snapshots(captured_at DESC);
            """;
        await command.ExecuteNonQueryAsync(ct);
        return true;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
