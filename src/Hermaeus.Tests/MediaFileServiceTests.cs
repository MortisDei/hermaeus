using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class MediaFileServiceTests
{
    private static readonly GeneratedMedia Media = new("generated-1.wav", "audio/wav", "RIFF0000WAVEsynthetic"u8.ToArray());

    [Fact]
    public async Task Export_replaces_atomically_with_exact_bytes_and_private_Unix_mode()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("output.wav");
        await File.WriteAllTextAsync(path, "old content");
        await MediaFileService.SaveAsync(path, Media);
        Assert.Equal(Media.Data, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public async Task Cancelled_export_preserves_previous_file_and_cleans_staging()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("output.wav");
        await File.WriteAllTextAsync(path, "old content");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MediaFileService.SaveAsync(path, Media, cts.Token));
        Assert.Equal("old content", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public async Task Mismatched_extension_is_refused_before_writes()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("output.exe");
        await File.WriteAllTextAsync(path, "preserve");
        await Assert.ThrowsAsync<ArgumentException>(() => MediaFileService.SaveAsync(path, Media));
        Assert.Equal("preserve", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Real_reparse_destination_is_refused_and_outside_file_is_preserved()
    {
        using var temp = new TempDir();
        var outside = temp.PathFor("outside");
        Directory.CreateDirectory(outside);
        var path = Path.Combine(outside, "output.wav");
        await File.WriteAllTextAsync(path, "outside canary");
        var link = temp.PathFor("redirect");
        BackupMigrationTests.CreateDirectoryReparsePoint(link, outside);
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
        await Assert.ThrowsAsync<IOException>(() => MediaFileService.SaveAsync(Path.Combine(link, "output.wav"), Media));
        Assert.Equal("outside canary", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(outside));
    }

    [Fact]
    public async Task Workflow_import_enforces_actual_byte_budget_and_strict_UTF8()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("workflow.json");
        var expected = "{\"text\":\"synthetic\"}";
        await File.WriteAllTextAsync(path, expected);
        Assert.Equal(expected, await MediaFileService.ReadWorkflowAsync(path));
        await File.WriteAllBytesAsync(path, new byte[] { 255, 255 });
        await Assert.ThrowsAsync<System.Text.DecoderFallbackException>(() => MediaFileService.ReadWorkflowAsync(path));
        await File.WriteAllBytesAsync(path, new byte[1_048_577]);
        await Assert.ThrowsAsync<InvalidDataException>(() => MediaFileService.ReadWorkflowAsync(path));
    }
}
