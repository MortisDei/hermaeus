using Microsoft.Data.Sqlite;

namespace Hermaeus.Rag.Storage;

internal sealed record SqliteMigration(int Version, Func<SqliteConnection, CancellationToken, Task<bool>> ApplyAsync);

internal static class SqliteMigrationRunner
{
    public static async Task<bool> ApplyAsync(
        SqliteConnection connection,
        string scope,
        int targetVersion,
        IReadOnlyList<SqliteMigration> migrations,
        CancellationToken ct)
    {
        await EnsureVersionTableAsync(connection, ct);
        var current = await GetVersionAsync(connection, scope, ct);
        var changed = false;

        foreach (var migration in migrations.OrderBy(m => m.Version))
        {
            if (migration.Version <= current || migration.Version > targetVersion)
                continue;

            changed |= await migration.ApplyAsync(connection, ct);
            await SetVersionAsync(connection, scope, migration.Version, ct);
            current = migration.Version;
        }

        if (current < targetVersion)
            await SetVersionAsync(connection, scope, targetVersion, ct);

        return changed;
    }

    private static async Task EnsureVersionTableAsync(SqliteConnection connection, CancellationToken ct)
    {
        // r20 rename: the bookkeeping table was "aether_schema_versions" before the
        // product rename. Rename it in place on first touch so existing databases
        // keep their recorded version instead of re-running every migration.
        await using (var legacyCheck = connection.CreateCommand())
        {
            legacyCheck.CommandText = @"
                SELECT
                    (SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'aether_schema_versions'),
                    (SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'hermaeus_schema_versions')";
            await using var reader = await legacyCheck.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct) && reader.GetInt32(0) > 0 && reader.GetInt32(1) == 0)
            {
                await reader.DisposeAsync();
                await using var rename = connection.CreateCommand();
                rename.CommandText = "ALTER TABLE aether_schema_versions RENAME TO hermaeus_schema_versions";
                await rename.ExecuteNonQueryAsync(ct);
            }
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS hermaeus_schema_versions (
                scope TEXT PRIMARY KEY,
                version INTEGER NOT NULL,
                updated_at TEXT NOT NULL
            );";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> GetVersionAsync(SqliteConnection connection, string scope, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT version FROM hermaeus_schema_versions WHERE scope = $scope";
        cmd.Parameters.AddWithValue("$scope", scope);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is null ? 0 : Convert.ToInt32(value);
    }

    private static async Task SetVersionAsync(SqliteConnection connection, string scope, int version, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO hermaeus_schema_versions (scope, version, updated_at)
            VALUES ($scope, $version, $updated)
            ON CONFLICT(scope) DO UPDATE SET
                version = excluded.version,
                updated_at = excluded.updated_at";
        cmd.Parameters.AddWithValue("$scope", scope);
        cmd.Parameters.AddWithValue("$version", version);
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
