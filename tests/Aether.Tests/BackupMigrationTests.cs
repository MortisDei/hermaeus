using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aether.Services;
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

        public static async Task DataRootMigrationMovesFiles()
        {
            using var temp = new TempDir();
            var previous = temp.PathFor("previous");
            var next = temp.PathFor("next");
            Directory.CreateDirectory(previous);
            File.WriteAllText(Path.Combine(previous, "conversations.db"), "db");
            File.WriteAllText(Path.Combine(previous, "conversations.db-shm"), "shm");

            var service = NewSettings(temp);
            service.Settings.DataManagement.DataRootDirectory = next;
            var result = await service.SaveAsync(previous);

            Equal(true, result.DataMigrated, "migration should report moved data");
            Equal(2, result.FilesMoved, "all db files should move");
            True(File.Exists(Path.Combine(next, "conversations.db")), "db should exist in new root");
            True(File.Exists(Path.Combine(next, "conversations.db-shm")), "sidecar db file should exist in new root");
            False(File.Exists(Path.Combine(previous, "conversations.db")), "old db should not be left behind");
            True(result.BackupDirectory is not null && File.Exists(Path.Combine(result.BackupDirectory, "conversations.db")),
                "migration should keep a backup copy in the target backup folder");
        }

        public static async Task BackupExcludesSecretsAndRefusesOverwrite()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("root");
            var backupTarget = temp.PathFor("backup");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "conversations.db"), "db");
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
            Equal("db", await File.ReadAllTextAsync(Path.Combine(restoreRoot, "conversations.db")), "overwrite restore should replace existing files");
        }
    }
}
