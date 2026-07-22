using Hermaeus.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// The migration runner sits under every local store; regressions here can
/// corrupt user data roots, so its contract is pinned directly.
/// </summary>
public sealed class MigrationRunnerTests
{
    private static SqliteConnection OpenInMemory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    private static SqliteMigration Migration(int version, List<int> applied) =>
        new(version, async (c, ct) =>
        {
            applied.Add(version);
            await using var cmd = c.CreateCommand();
            cmd.CommandText = $"CREATE TABLE IF NOT EXISTS t{version} (id INTEGER PRIMARY KEY);";
            await cmd.ExecuteNonQueryAsync(ct);
            return true;
        });

    [Fact]
    public async Task Applies_migrations_in_order_and_records_version()
    {
        await using var c = OpenInMemory();
        var applied = new List<int>();
        var migrations = new[] { Migration(2, applied), Migration(1, applied) };

        var changed = await SqliteMigrationRunner.ApplyAsync(c, "test_scope", 2, migrations, CancellationToken.None);

        Assert.True(changed);
        Assert.Equal([1, 2], applied);

        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT version FROM hermaeus_schema_versions WHERE scope = 'test_scope'";
        Assert.Equal(2L, (long)(await cmd.ExecuteScalarAsync() ?? 0L));
    }

    [Fact]
    public async Task Rerun_is_idempotent_and_skips_applied_migrations()
    {
        await using var c = OpenInMemory();
        var applied = new List<int>();
        var migrations = new[] { Migration(1, applied), Migration(2, applied) };

        await SqliteMigrationRunner.ApplyAsync(c, "test_scope", 2, migrations, CancellationToken.None);
        var changedAgain = await SqliteMigrationRunner.ApplyAsync(c, "test_scope", 2, migrations, CancellationToken.None);

        Assert.False(changedAgain);
        Assert.Equal([1, 2], applied);
    }

    [Fact]
    public async Task Migrations_above_target_version_are_not_applied()
    {
        await using var c = OpenInMemory();
        var applied = new List<int>();
        var migrations = new[] { Migration(1, applied), Migration(2, applied), Migration(3, applied) };

        await SqliteMigrationRunner.ApplyAsync(c, "test_scope", 2, migrations, CancellationToken.None);

        Assert.Equal([1, 2], applied);
    }

    [Fact]
    public async Task Version_scopes_are_independent()
    {
        await using var c = OpenInMemory();
        var appliedA = new List<int>();
        var appliedB = new List<int>();

        await SqliteMigrationRunner.ApplyAsync(c, "scope_a", 1, [Migration(1, appliedA)], CancellationToken.None);
        await SqliteMigrationRunner.ApplyAsync(c, "scope_b", 2, [new SqliteMigration(2, async (conn, ct) =>
        {
            appliedB.Add(2);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS scoped_b (id INTEGER PRIMARY KEY);";
            await cmd.ExecuteNonQueryAsync(ct);
            return true;
        })], CancellationToken.None);

        Assert.Equal([1], appliedA);
        Assert.Equal([2], appliedB);

        await using var cmd2 = c.CreateCommand();
        cmd2.CommandText = "SELECT version FROM hermaeus_schema_versions WHERE scope = 'scope_a'";
        Assert.Equal(1L, (long)(await cmd2.ExecuteScalarAsync() ?? 0L));
    }

    [Fact]
    public async Task Target_version_is_recorded_even_without_pending_migrations()
    {
        await using var c = OpenInMemory();

        await SqliteMigrationRunner.ApplyAsync(c, "test_scope", 5, [], CancellationToken.None);

        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT version FROM hermaeus_schema_versions WHERE scope = 'test_scope'";
        Assert.Equal(5L, (long)(await cmd.ExecuteScalarAsync() ?? 0L));
    }

    [Fact]
    public async Task Legacy_schema_versions_table_is_renamed_in_place()
    {
        await using var c = OpenInMemory();
        await using (var seed = c.CreateCommand())
        {
            seed.CommandText = @"
                CREATE TABLE aether_schema_versions (
                    scope TEXT PRIMARY KEY,
                    version INTEGER NOT NULL,
                    updated_at TEXT NOT NULL
                );
                INSERT INTO aether_schema_versions (scope, version, updated_at)
                VALUES ('test_scope', 3, '2026-01-01T00:00:00Z');";
            await seed.ExecuteNonQueryAsync();
        }

        var applied = new List<int>();
        var migrations = new[] { Migration(1, applied), Migration(2, applied), Migration(3, applied) };

        var changed = await SqliteMigrationRunner.ApplyAsync(c, "test_scope", 3, migrations, CancellationToken.None);

        Assert.False(changed);
        Assert.Empty(applied);

        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT version FROM hermaeus_schema_versions WHERE scope = 'test_scope'";
        Assert.Equal(3L, (long)(await cmd.ExecuteScalarAsync() ?? 0L));
    }
}
