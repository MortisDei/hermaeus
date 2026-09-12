using System.IO.Compression;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class BackupRestoreSafetyTests
{
    [Fact]
    public async Task Restore_refuses_an_entry_over_the_per_file_budget_before_writing()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var backup = CreateBackup(temp, ("large.txt", "12345"));
        var limits = new BackupRestoreLimits(MaxEntries: 10, MaxEntryBytes: 4, MaxTotalUncompressedBytes: 100);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new BackupService(settings).RestoreAsync(backup, allowOverwrite: false, limits: limits));

        Assert.False(File.Exists(Path.Combine(temp.PathFor("data"), "large.txt")));
    }

    [Fact]
    public async Task Restore_refuses_when_total_uncompressed_size_exceeds_the_budget()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var backup = CreateBackup(temp, ("one.txt", "1234"), ("two.txt", "5678"));
        var limits = new BackupRestoreLimits(MaxEntries: 10, MaxEntryBytes: 10, MaxTotalUncompressedBytes: 7);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new BackupService(settings).RestoreAsync(backup, allowOverwrite: false, limits: limits));

        Assert.False(File.Exists(Path.Combine(temp.PathFor("data"), "one.txt")));
        Assert.False(File.Exists(Path.Combine(temp.PathFor("data"), "two.txt")));
    }

    [Fact]
    public async Task Restore_rejects_duplicate_targets_even_when_overwrite_is_allowed()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var backup = CreateBackup(temp, ("same.txt", "first"), ("same.txt", "second"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new BackupService(settings).RestoreAsync(backup, allowOverwrite: true));

        Assert.False(File.Exists(Path.Combine(temp.PathFor("data"), "same.txt")));
    }

    [Fact]
    public async Task Restore_cancellation_leaves_no_target_or_restore_temporary_file()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var backup = CreateBackup(temp, ("cancelled.txt", "content"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new BackupService(settings).RestoreAsync(backup, allowOverwrite: false, limits: null, ct: cancellation.Token));

        var dataRoot = temp.PathFor("data");
        Assert.False(File.Exists(Path.Combine(dataRoot, "cancelled.txt")));
        if (Directory.Exists(dataRoot))
            Assert.DoesNotContain(Directory.EnumerateFiles(dataRoot, "*", SearchOption.AllDirectories),
                path => path.EndsWith(".restore.tmp", StringComparison.Ordinal));
    }

    private static string CreateBackup(TempDir temp, params (string Name, string Content)[] entries)
    {
        var path = temp.PathFor("input.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var item in entries)
        {
            var entry = archive.CreateEntry(item.Name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(item.Content);
        }

        return path;
    }
}
