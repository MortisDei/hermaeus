using System;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aether.Services;
using Microsoft.Data.Sqlite;
using static Aether.Tests.Helpers;

namespace Aether.Tests
{
    internal static class BackupMigrationTests
    {
        public static async Task DataRootMigrationPreview()
        {
            using var temp = new TempDir();
            var previous = temp.PathFor("previous");
            var next = temp.PathFor("next");
            Directory.CreateDirectory(previous);
            File.WriteAllText(Path.Combine(previous, "conversations.db"), "db");
            File.WriteAllText(Path.Combine(previous, "conversations.db-wal"), "wal");

            var service = NewSettings(temp);
            var plan = service.PreviewDataRootMigration(previous, next);

            Equal(true, plan.WillMove, "migration should be allowed");
            Equal(2, plan.FilesToMove, "all conversation db files should be counted");
            Equal(0, plan.Conflicts.Count, "clean target should have no conflicts");
            await Task.CompletedTask;
        }

        public static async Task DataRootMigrationRefusesConflicts()
        {
            using var temp = new TempDir();
            var previous = temp.PathFor("previous");
            var next = temp.PathFor("next");
            Directory.CreateDirectory(previous);
            Directory.CreateDirectory(next);
            File.WriteAllText(Path.Combine(previous, "conversations.db"), "old");
            File.WriteAllText(Path.Combine(next, "conversations.db"), "existing");

            var service = NewSettings(temp);
            var plan = service.PreviewDataRootMigration(previous, next);
            Equal(false, plan.WillMove, "migration preview should refuse conflicts");
            Equal(1, plan.Conflicts.Count, "conflicting db should be reported");

            service.Settings.DataManagement.DataRootDirectory = next;
            await ThrowsAsync<IOException>(() => service.SaveAsync(previous));
        }

        public static async Task SaveWithoutPreviousDataRootDoesNotAttemptMigration()
        {
            using var temp = new TempDir();
            var next = temp.PathFor("next");

            var service = NewSettings(temp);
            service.Settings.DataManagement.DataRootDirectory = next;
            var result = await service.SaveAsync();

            Equal(false, result.DataMigrated, "routine saves should not run migration without an explicit previous data root");
            Equal(null, result.PreviousDataRoot, "previous data root should be null when migration was not requested");
            Equal(Path.GetFullPath(next), result.CurrentDataRoot, "save result should still report the resolved current data root");
            True(Directory.Exists(next), "save should still ensure the current data root directory exists");
        }

        public static async Task DataRootMigrationMovesFiles()
        {
            using var temp = new TempDir();
            var previous = temp.PathFor("previous");
            var next = temp.PathFor("next");
            Directory.CreateDirectory(previous);
            File.WriteAllText(Path.Combine(previous, "conversations.db"), "db");
            File.WriteAllText(Path.Combine(previous, "conversations.db-shm"), "shm");
            Directory.CreateDirectory(Path.Combine(previous, "agent", "tasks", "task-1"));
            File.WriteAllText(Path.Combine(previous, "agent", "task_index.db"), "index");
            File.WriteAllText(Path.Combine(previous, "agent", "tasks", "task-1", "task_state.json"), "state");

            var service = NewSettings(temp);
            service.Settings.DataManagement.DataRootDirectory = next;
            var result = await service.SaveAsync(previous);

            Equal(true, result.DataMigrated, "migration should report moved data");
            Equal(4, result.FilesMoved, "all db and Agent files should move");
            True(File.Exists(Path.Combine(next, "conversations.db")), "db should exist in new root");
            True(File.Exists(Path.Combine(next, "conversations.db-shm")), "sidecar db file should exist in new root");
            True(File.Exists(Path.Combine(next, "agent", "task_index.db")), "Agent task index should move");
            True(File.Exists(Path.Combine(next, "agent", "tasks", "task-1", "task_state.json")), "Agent task JSON should move");
            False(File.Exists(Path.Combine(previous, "conversations.db")), "old db should not be left behind");
            True(result.BackupDirectory is not null && File.Exists(Path.Combine(result.BackupDirectory, "conversations.db")),
                "migration should keep a backup copy in the target backup folder");
            True(result.BackupDirectory is not null && File.Exists(Path.Combine(result.BackupDirectory, "agent", "tasks", "task-1", "task_state.json")),
                "migration should keep a backup copy of Agent state");
        }

        /// <summary>r11 3.1: migration used to move only conversations.db*/memories.db*/benchmarks.db*/agent/, stranding everything else the app writes to the data root. A fixture containing every other known family must move completely.</summary>
        public static async Task DataRootMigrationMovesEveryKnownFileFamily()
        {
            using var temp = new TempDir();
            var previous = temp.PathFor("previous");
            var next = temp.PathFor("next");
            Directory.CreateDirectory(previous);
            File.WriteAllText(Path.Combine(previous, "conversations.db"), "db");
            File.WriteAllText(Path.Combine(previous, "secrets.local.json"), "secret");
            File.WriteAllText(Path.Combine(previous, "secrets.local.key"), "key");
            File.WriteAllText(Path.Combine(previous, "traces.db"), "traces");
            File.WriteAllText(Path.Combine(previous, "eval_runs.db"), "evals");
            Directory.CreateDirectory(Path.Combine(previous, "logs"));
            File.WriteAllText(Path.Combine(previous, "logs", "app.log"), "log");
            Directory.CreateDirectory(Path.Combine(previous, "voice"));
            File.WriteAllText(Path.Combine(previous, "voice", "lexicon.txt"), "lexicon");
            Directory.CreateDirectory(Path.Combine(previous, "agent-scenarios"));
            File.WriteAllText(Path.Combine(previous, "agent-scenarios", "scenario.json"), "scenario");
            Directory.CreateDirectory(Path.Combine(previous, "eval-runs"));
            File.WriteAllText(Path.Combine(previous, "eval-runs", "run.json"), "run");
            // A never-before-seen file family must also move without any manifest edit.
            File.WriteAllText(Path.Combine(previous, "some-future-store.db"), "future");

            var service = NewSettings(temp);
            var plan = service.PreviewDataRootMigration(previous, next);

            service.Settings.DataManagement.DataRootDirectory = next;
            var result = await service.SaveAsync(previous);

            Equal(10, result.FilesMoved, "every known and unknown file family should move");
            Equal(plan.FilesToMove, result.FilesMoved, "preview counts should match what migration actually moves");
            True(File.Exists(Path.Combine(next, "secrets.local.json")), "secrets store should move");
            True(File.Exists(Path.Combine(next, "secrets.local.key")), "secrets key should move");
            True(File.Exists(Path.Combine(next, "traces.db")), "trace store should move");
            True(File.Exists(Path.Combine(next, "eval_runs.db")), "eval store should move");
            True(File.Exists(Path.Combine(next, "logs", "app.log")), "logs should move");
            True(File.Exists(Path.Combine(next, "voice", "lexicon.txt")), "voice lexicon should move");
            True(File.Exists(Path.Combine(next, "agent-scenarios", "scenario.json")), "agent scenarios should move");
            True(File.Exists(Path.Combine(next, "eval-runs", "run.json")), "eval runs should move");
            True(File.Exists(Path.Combine(next, "some-future-store.db")), "an unmodeled future file family should still move");
            False(Directory.Exists(previous), "old root should retain nothing after a full migration");

            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(Path.Combine(next, "secrets.local.json"));
                True(mode.HasFlag(UnixFileMode.UserRead) && !mode.HasFlag(UnixFileMode.GroupRead) && !mode.HasFlag(UnixFileMode.OtherRead),
                    "moved secrets file should keep restrictive owner-only permissions");
            }
        }

        /// <summary>r11 3.1 acceptance: secrets stored through ISecretStore before a data-root move must still resolve correctly afterward.</summary>
        public static async Task SecretsResolveCorrectlyAfterDataRootMigration()
        {
            var previousEnv = Environment.GetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN");
            Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", "1");
            try
            {
                using var temp = new TempDir();
                var previous = temp.PathFor("previous");
                var next = temp.PathFor("next");
                Directory.CreateDirectory(previous);

                var service = NewSettings(temp);
                service.Settings.DataManagement.DataRootDirectory = previous;
                await service.SaveAsync();

                var secrets = new SecretStore(service);
                var reference = await secrets.StoreAsync("test-provider-key", "super-secret-value");
                True(secrets.IsReference(reference), "storing a secret should return a reference");

                service.Settings.DataManagement.DataRootDirectory = next;
                var migration = await service.SaveAsync(previous);
                True(migration.DataMigrated, "migration should have moved the fallback secrets vault");

                var resolved = await secrets.ResolveAsync(reference);
                Equal("super-secret-value", resolved, "secret should resolve correctly after its data root moved");
            }
            finally
            {
                Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", previousEnv);
            }
        }

        /// <summary>
        /// r11 3.1 regression guard: settings.json bootstraps the data root
        /// and is explicitly rejected as a migration target
        /// (docs/review/r11/05-roadmap.md). When the data root has never
        /// been customized, settings.json's directory IS the data root, so
        /// "move everything under the root" must not sweep it up the first
        /// time a user points DataRootDirectory somewhere new.
        /// </summary>
        public static async Task DataRootMigrationNeverMovesSettingsJson()
        {
            using var temp = new TempDir();
            var previousRoot = temp.PathFor("previous");
            var next = temp.PathFor("next");
            Directory.CreateDirectory(previousRoot);

            var settingsPath = Path.Combine(previousRoot, "settings.json");
            var service = new SettingsService(settingsPath);
            await service.SaveAsync();
            File.WriteAllText(Path.Combine(previousRoot, "conversations.db"), "db");

            service.Settings.DataManagement.DataRootDirectory = next;
            var result = await service.SaveAsync(previousRoot);

            True(result.DataMigrated, "migration should still move other data");
            True(File.Exists(settingsPath), "settings.json must never move out of its bootstrap location");
            False(File.Exists(Path.Combine(next, "settings.json")), "settings.json must not be copied into the new data root either");
        }

        public static async Task BackupExcludesSecretsAndRefusesOverwrite()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("root");
            var backupTarget = temp.PathFor("backup");
            Directory.CreateDirectory(root);
            await CreateMarkerSqliteDbAsync(Path.Combine(root, "conversations.db"), "db");
            File.WriteAllText(Path.Combine(root, "secrets.local.json"), "secret");
            File.WriteAllText(Path.Combine(root, "secrets.local.key"), "key");

            var service = NewSettings(temp);
            service.Settings.DataManagement.DataRootDirectory = root;
            var backups = new BackupService(service);
            var backup = await backups.BackupAsync(backupTarget);

            using (var archive = System.IO.Compression.ZipFile.OpenRead(backup.Path))
            {
                True(archive.GetEntry("conversations.db") is not null, "conversation db should be backed up");
                True(archive.GetEntry("secrets.local.json") is null, "local secrets should not be backed up");
                True(archive.GetEntry("secrets.local.key") is null, "local secret key should not be backed up");
            }

            var restoreRoot = temp.PathFor("restore");
            Directory.CreateDirectory(restoreRoot);
            File.WriteAllText(Path.Combine(restoreRoot, "conversations.db"), "existing");
            service.Settings.DataManagement.DataRootDirectory = restoreRoot;
            await ThrowsAsync<IOException>(() => backups.RestoreAsync(backup.Path));
            await backups.RestoreAsync(backup.Path, allowOverwrite: true);
            Equal("db", await ReadMarkerAsync(Path.Combine(restoreRoot, "conversations.db")), "overwrite restore should replace existing files");
        }

        /// <summary>r11 3.6: a raw zip of a live SQLite file risks an internally inconsistent copy if it is mid-write; backup must instead use SQLite's own online-backup API to produce a consistent snapshot.</summary>
        public static async Task BackupOfADatabaseWithAnOpenWriterYieldsAConsistentSnapshot()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("root");
            var backupTarget = temp.PathFor("backup");
            Directory.CreateDirectory(root);
            var dbPath = Path.Combine(root, "conversations.db");
            await CreateMarkerSqliteDbAsync(dbPath, "before-write");

            await using var writer = new SqliteConnection($"Data Source={dbPath}");
            await writer.OpenAsync();
            await using var transaction = await writer.BeginTransactionAsync();
            var insert = writer.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = "INSERT INTO marker (value) VALUES ('mid-transaction-row')";
            await insert.ExecuteNonQueryAsync();
            // Deliberately left open (uncommitted) across the backup call below,
            // simulating a backup taken during an in-flight write.

            var service = NewSettings(temp);
            service.Settings.DataManagement.DataRootDirectory = root;
            var backups = new BackupService(service);
            var backup = await backups.BackupAsync(backupTarget);

            await transaction.RollbackAsync();

            var extractDir = temp.PathFor("extracted");
            Directory.CreateDirectory(extractDir);
            using (var archive = System.IO.Compression.ZipFile.OpenRead(backup.Path))
                archive.GetEntry("conversations.db")!.ExtractToFile(Path.Combine(extractDir, "conversations.db"));

            await using var check = new SqliteConnection($"Data Source={Path.Combine(extractDir, "conversations.db")}");
            await check.OpenAsync();
            var integrityCmd = check.CreateCommand();
            integrityCmd.CommandText = "PRAGMA integrity_check";
            Equal("ok", (string)(await integrityCmd.ExecuteScalarAsync())!, "backed-up database should pass integrity_check");

            Equal("before-write", await ReadMarkerAsync(Path.Combine(extractDir, "conversations.db")),
                "snapshot should reflect committed state only, not the uncommitted in-flight transaction");
        }

        private static async Task CreateMarkerSqliteDbAsync(string path, string marker)
        {
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE marker (value TEXT); INSERT INTO marker (value) VALUES ($v);";
            cmd.Parameters.AddWithValue("$v", marker);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<string> ReadMarkerAsync(string path)
        {
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM marker LIMIT 1";
            return (string)(await cmd.ExecuteScalarAsync())!;
        }

        public static async Task BackupRestoreRejectsUnsafePathPrefix()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("root");
            var unsafePeer = root + "2";
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(unsafePeer);
            var backup = temp.PathFor("unsafe.zip");
            using (var archive = ZipFile.Open(backup, ZipArchiveMode.Create))
            {
                archive.CreateEntry("../root2/escape.txt");
            }

            var service = NewSettings(temp);
            service.Settings.DataManagement.DataRootDirectory = root;
            var backups = new BackupService(service);

            await ThrowsAsync<InvalidOperationException>(() => backups.RestoreAsync(backup));
            False(File.Exists(Path.Combine(unsafePeer, "escape.txt")), "restore should not write outside the data root prefix");
        }

        public static async Task BackupRestoreRejectsCaseVariantSiblingOnCaseSensitiveFileSystems()
        {
            if (OperatingSystem.IsWindows())
                return;

            using var temp = new TempDir();
            var root = temp.PathFor("AetherRoot");
            var unsafePeer = temp.PathFor("aetherroot");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(unsafePeer);
            var backup = temp.PathFor("unsafe-case.zip");
            using (var archive = ZipFile.Open(backup, ZipArchiveMode.Create))
            {
                archive.CreateEntry("../aetherroot/escape.txt");
            }

            var service = NewSettings(temp);
            service.Settings.DataManagement.DataRootDirectory = root;
            var backups = new BackupService(service);

            await ThrowsAsync<InvalidOperationException>(() => backups.RestoreAsync(backup));
            False(File.Exists(Path.Combine(unsafePeer, "escape.txt")), "restore should not treat case-variant siblings as the data root");
        }
    }
}
