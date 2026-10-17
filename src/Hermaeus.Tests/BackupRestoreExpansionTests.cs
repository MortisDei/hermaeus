using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class BackupRestoreExpansionTests
{
    [Theory]
    [InlineData("entry-budget", 1, 128, 32768)]
    [InlineData("total-budget", 1, 16384, 128)]
    [InlineData("declared-too-small", 1, 16384, 32768)]
    [InlineData("declared-too-large", 12000, 16384, 32768)]
    public async Task Forged_declared_sizes_cannot_publish_expanded_or_partial_restore(
        string scenario, int declaredBytes, long entryLimit, long totalLimit)
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var root = temp.PathFor("data");
        Directory.CreateDirectory(root);
        var first = Path.Combine(root, "first.txt");
        var second = Path.Combine(root, "expanded.txt");
        await File.WriteAllTextAsync(first, "owner first");
        await File.WriteAllTextAsync(second, "owner second");
        var archivePath = temp.PathFor(scenario + ".zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "first.txt", new string('a', 100));
            WriteEntry(archive, "expanded.txt", new string('x', 8192));
        }
        ForgeCentralDirectoryUInt32(archivePath, "expanded.txt", 24, checked((uint)declaredBytes));

        // Establish that declaration-only preflight accepts this malicious
        // metadata. The compressed member still contains the original 8 KiB.
        using (var inspection = ZipFile.OpenRead(archivePath))
        {
            Assert.Equal(declaredBytes, inspection.GetEntry("expanded.txt")!.Length);
            Assert.All(inspection.Entries, entry => Assert.True(entry.Length <= entryLimit));
            Assert.True(inspection.Entries.Sum(entry => entry.Length) <= totalLimit);
            Assert.True(inspection.GetEntry("expanded.txt")!.CompressedLength < 8192);
        }

        var limits = new BackupRestoreLimits(MaxEntries: 10, MaxEntryBytes: entryLimit,
            MaxTotalUncompressedBytes: totalLimit);
        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupService(settings)
            .RestoreAsync(archivePath, allowOverwrite: true, limits: limits));

        Assert.Equal("owner first", await File.ReadAllTextAsync(first));
        Assert.Equal("owner second", await File.ReadAllTextAsync(second));
        Assert.Equal(new[] { "expanded.txt", "first.txt" }, Directory.GetFiles(root)
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
        Assert.Empty(Directory.GetDirectories(root));
        Assert.DoesNotContain(Directory.EnumerateFiles(temp.PathFor(""), "*", SearchOption.AllDirectories),
            path => path.EndsWith(".restore.tmp", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", 0U)]
    [InlineData("123456789", 0xcbf43926U)]
    [InlineData("hello world", 0x0d4a1185U)]
    public async Task Valid_ZIP_entries_with_independent_known_CRC_vectors_restore(string content, uint expectedCrc)
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var path = temp.PathFor("known-vector.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create)) WriteEntry(archive, "vector.txt", content);
        using (var inspection = ZipFile.OpenRead(path)) Assert.Equal(expectedCrc, inspection.GetEntry("vector.txt")!.Crc32);

        await new BackupService(settings).RestoreAsync(path);

        Assert.Equal(content, await File.ReadAllTextAsync(temp.PathFor("data/vector.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Wrong_checksum_is_refused_for_stored_and_deflated_entries(bool compressed)
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var content = new string('a', 200_000) + "final bytes";
        var path = temp.PathFor("wrong-checksum.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("content.txt", compressed ? CompressionLevel.SmallestSize : CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
        uint crc;
        using (var inspection = ZipFile.OpenRead(path)) crc = inspection.GetEntry("content.txt")!.Crc32;
        ForgeCentralDirectoryUInt32(path, "content.txt", 16, crc ^ 1);

        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => new BackupService(settings).RestoreAsync(path));

        Assert.Contains("checksum", failure.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.PathFor("data")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Valid_large_stored_and_deflated_entries_restore_across_reader_buffers(bool compressed)
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var content = string.Concat(Enumerable.Repeat("0123456789abcdef", 20_000)) + "final";
        var path = temp.PathFor("large-valid.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("content.txt", compressed ? CompressionLevel.SmallestSize : CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        await new BackupService(settings).RestoreAsync(path);

        Assert.Equal(content, await File.ReadAllTextAsync(temp.PathFor("data/content.txt")));
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void ForgeCentralDirectoryUInt32(string path, string entryName, int fieldOffset, uint value)
    {
        var bytes = File.ReadAllBytes(path);
        var patched = 0;
        for (var index = 0; index <= bytes.Length - 46; index++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index, 4)) != 0x02014b50) continue;
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index + 28, 2));
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index + 30, 2));
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index + 32, 2));
            Assert.True(index + 46 + nameLength + extraLength + commentLength <= bytes.Length);
            if (Encoding.UTF8.GetString(bytes, index + 46, nameLength) == entryName)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index + fieldOffset, 4), value);
                patched++;
            }
            index += 45 + nameLength + extraLength + commentLength;
        }
        Assert.Equal(1, patched);
        File.WriteAllBytes(path, bytes);
    }
}
