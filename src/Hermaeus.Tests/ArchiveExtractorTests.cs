using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ArchiveExtractorTests
{
    [Fact]
    public async Task ExtractAsync_zip_places_nested_entries_under_the_destination()
    {
        using var temp = new TempDir();
        var archivePath = temp.PathFor("fixture.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        BuildZip(archivePath, archive =>
        {
            AddZipEntry(archive, "llama-server.exe", "stub-binary");
            AddZipEntry(archive, "lib/ggml.dll", "sibling-dll");
        });

        var destination = temp.PathFor("out");
        await ArchiveExtractor.ExtractAsync(archivePath, destination);

        Assert.Equal("stub-binary", await File.ReadAllTextAsync(Path.Combine(destination, "llama-server.exe")));
        Assert.Equal("sibling-dll", await File.ReadAllTextAsync(Path.Combine(destination, "lib", "ggml.dll")));
    }

    [Fact]
    public async Task ExtractAsync_zip_rejects_an_entry_that_escapes_the_destination()
    {
        using var temp = new TempDir();
        var archivePath = temp.PathFor("evil.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        BuildZip(archivePath, archive => AddZipEntry(archive, "../evil.exe", "malicious"));

        var destination = temp.PathFor("out");
        await Assert.ThrowsAsync<InvalidOperationException>(() => ArchiveExtractor.ExtractAsync(archivePath, destination));
    }

    [Fact]
    public async Task ExtractAsync_zip_can_strip_one_known_upstream_wrapper_directory()
    {
        using var temp = new TempDir();
        var archivePath = temp.PathFor("wrapped.zip");
        BuildZip(archivePath, archive =>
        {
            AddZipEntry(archive, "llama-b10679/llama-server", "stub-binary");
            AddZipEntry(archive, "llama-b10679/libggml.so", "sibling-library");
        });

        var destination = temp.PathFor("out");
        await ArchiveExtractor.ExtractAsync(archivePath, destination, "llama-b10679");

        Assert.True(File.Exists(Path.Combine(destination, "llama-server")));
        Assert.False(Directory.Exists(Path.Combine(destination, "llama-b10679")));
    }

    [Fact]
    public async Task ExtractAsync_targz_places_nested_entries_under_the_destination()
    {
        using var temp = new TempDir();
        var archivePath = temp.PathFor("fixture.tar.gz");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await BuildTarGzAsync(archivePath, async writer =>
        {
            await AddTarEntryAsync(writer, "build/bin/llama-server", "stub-binary");
        });

        var destination = temp.PathFor("out");
        await ArchiveExtractor.ExtractAsync(archivePath, destination);

        Assert.Equal("stub-binary", await File.ReadAllTextAsync(Path.Combine(destination, "build", "bin", "llama-server")));
    }

    [Fact]
    public async Task ExtractAsync_targz_rejects_an_entry_that_escapes_the_destination()
    {
        using var temp = new TempDir();
        var archivePath = temp.PathFor("evil.tar.gz");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await BuildTarGzAsync(archivePath, async writer =>
        {
            await AddTarEntryAsync(writer, "../evil", "malicious");
        });

        var destination = temp.PathFor("out");
        await Assert.ThrowsAsync<InvalidOperationException>(() => ArchiveExtractor.ExtractAsync(archivePath, destination));
    }

    [Fact]
    public async Task ExtractAsync_targz_materializes_safe_relative_library_links()
    {
        using var temp = new TempDir();
        var archivePath = temp.PathFor("fixture.tar.gz");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await BuildTarGzAsync(archivePath, async writer =>
        {
            await AddTarEntryAsync(writer, "lib/libllama-common.so.0.0.10034", "shared-library");
            await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "lib/libllama-common.so.0")
            {
                LinkName = "libllama-common.so.0.0.10034"
            });
        });

        var destination = temp.PathFor("out");
        await ArchiveExtractor.ExtractAsync(archivePath, destination);

        var soname = Path.Combine(destination, "lib", "libllama-common.so.0");
        Assert.True(File.Exists(soname));
        Assert.Equal("shared-library", await File.ReadAllTextAsync(soname));
        Assert.Null(new FileInfo(soname).LinkTarget);
    }

    [Fact]
    public async Task ExtractAsync_targz_rejects_a_link_target_that_escapes_the_destination()
    {
        using var temp = new TempDir();
        var archivePath = temp.PathFor("evil-link.tar.gz");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await BuildTarGzAsync(archivePath, async writer =>
        {
            await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "lib/libllama.so.0")
            {
                LinkName = "../../outside"
            });
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ArchiveExtractor.ExtractAsync(archivePath, temp.PathFor("out")));
    }

    private static void BuildZip(string path, Action<ZipArchive> populate)
    {
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        populate(archive);
    }

    private static void AddZipEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8);
        writer.Write(content);
    }

    private static async Task BuildTarGzAsync(string path, Func<TarWriter, Task> populate)
    {
        await using var fileStream = File.Create(path);
        await using var gzip = new GZipStream(fileStream, CompressionLevel.Fastest);
        await using var writer = new TarWriter(gzip, TarEntryFormat.Pax);
        await populate(writer);
    }

    private static async Task AddTarEntryAsync(TarWriter writer, string name, string content)
    {
        var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
        {
            DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content))
        };
        await writer.WriteEntryAsync(entry);
    }
}
