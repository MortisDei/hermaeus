using System.IO.Compression;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class BackupSourceReparseTests
{
    [Fact]
    public void SQLite_snapshot_staging_is_under_Temp_and_uses_private_Unix_mode()
    {
        var path = BackupService.CreateSnapshotDirectory();
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var prefix = Path.EndsInDirectorySeparator(tempRoot) ? tempRoot : tempRoot + Path.DirectorySeparatorChar;
        Assert.StartsWith(prefix, Path.GetFullPath(path), ModelPathSafety.LocalPathComparison);
        Assert.True(Guid.TryParseExact(Path.GetFileName(path)["hermaeus-backup-snapshot-".Length..], "N", out _));
        try
        {
            Assert.True(Directory.Exists(path));
            Assert.Empty(Directory.EnumerateFileSystemEntries(path));
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(path));
            // Windows keeps its existing per-user TEMP ACL inheritance. This
            // assertion does not claim an independent native ACL audit.
        }
        finally { Directory.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Backup_refuses_redirected_source_root_or_descendant(bool rootIsRedirected)
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var root = SettingsService.ResolveDataRoot(settings.Settings);
        var outside = temp.PathFor("outside-source");
        Directory.CreateDirectory(outside);
        var canary = Path.Combine(outside, "canary.txt");
        await File.WriteAllTextAsync(canary, "synthetic outside-root canary");
        if (!rootIsRedirected) Directory.CreateDirectory(root);
        var link = rootIsRedirected ? root : Path.Combine(root, "redirected");
        BackupMigrationTests.CreateDirectoryReparsePoint(link, outside);
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
        var target = temp.PathFor("backups");

        var operation = new BackupService(settings).BackupAsync(target);
        var failure = await Record.ExceptionAsync(() => operation);
        if (failure is null)
        {
            using var archive = ZipFile.OpenRead((await operation).Path);
            var expectedName = rootIsRedirected ? "canary.txt" : "redirected/canary.txt";
            var entry = archive.Entries.SingleOrDefault(item => item.FullName.Replace('\\', '/') == expectedName);
            var copied = false;
            if (entry is not null)
            {
                using var reader = new StreamReader(entry.Open());
                copied = await reader.ReadToEndAsync() == "synthetic outside-root canary";
            }
            Assert.Fail($"Redirected source was accepted; outside canary copied: {copied}.");
        }
        Assert.IsType<InvalidOperationException>(failure);

        Assert.Empty(Directory.Exists(target) ? Directory.GetFiles(target) : Array.Empty<string>());
        Assert.Equal("synthetic outside-root canary", await File.ReadAllTextAsync(canary));
    }

    [Fact]
    public async Task Backup_refuses_a_redirected_destination_before_writing_an_archive()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var root = SettingsService.ResolveDataRoot(settings.Settings);
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "notes.txt"), "owner notes");
        var outside = temp.PathFor("outside-target");
        Directory.CreateDirectory(outside);
        var target = temp.PathFor("backups");
        BackupMigrationTests.CreateDirectoryReparsePoint(target, outside);
        Assert.True((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0);

        var operation = new BackupService(settings).BackupAsync(target);
        var failure = await Record.ExceptionAsync(() => operation);
        if (failure is null)
            Assert.Fail($"Redirected destination was accepted; outside archives created: {Directory.GetFiles(outside).Length}.");
        Assert.IsType<InvalidOperationException>(failure);

        Assert.Empty(Directory.GetFiles(outside));
        Assert.Equal("owner notes", await File.ReadAllTextAsync(Path.Combine(root, "notes.txt")));
    }
}
