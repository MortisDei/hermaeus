using System.IO.Compression;
using System.Net;
using System.Text;
using Aether.Services;
using Xunit;

namespace Aether.Tests;

public sealed class LlamaServerInstallTests
{
    /// <summary>r11 1.1/1.2: InstallAsync must download the real pinned archive, extract it (zip-slip guarded), and hand back a path ServerProcessManager can actually start, not a raw archive moved into place as an .exe.</summary>
    [Fact]
    public async Task InstallAsync_extracts_the_pinned_archive_and_produces_a_runnable_executable()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TempDir();
        var installDir = temp.PathFor("install");

        var zipBytes = BuildZipBytes(archive =>
        {
            AddZipEntry(archive, "llama-server.exe", "stub-binary-content");
            AddZipEntry(archive, "ggml.dll", "sibling-dll-content");
        });

        using var http = new HttpClient(new FixedContentHandler(zipBytes));
        var service = new LlamaServerSetupService(new ModelDownloadService(http), http);

        var result = await service.InstallAsync(installDir);

        Assert.True(result.Success, result.Log);
        Assert.NotNull(result.UpdatedPath);
        Assert.True(File.Exists(result.UpdatedPath), "installed executable should exist on disk");
        Assert.Equal("stub-binary-content", await File.ReadAllTextAsync(result.UpdatedPath!));

        var siblingDll = Path.Combine(Path.GetDirectoryName(result.UpdatedPath)!, "ggml.dll");
        Assert.True(File.Exists(siblingDll), "sibling DLLs the Windows build needs must extract next to the executable");

        // Downloaded archive must not be left behind next to the extracted binary.
        Assert.Empty(Directory.EnumerateFiles(installDir, "*.zip", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task InstallAsync_is_idempotent_when_the_executable_already_exists()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TempDir();
        var installDir = temp.PathFor("install");
        Directory.CreateDirectory(installDir);
        var existing = Path.Combine(installDir, "llama-server.exe");
        await File.WriteAllTextAsync(existing, "already-here");

        using var http = new HttpClient(new ThrowingHandler());
        var service = new LlamaServerSetupService(new ModelDownloadService(http), http);

        var result = await service.InstallAsync(installDir);

        Assert.True(result.Success);
        // Windows path resolution is case-insensitive; the resolver's candidate
        // casing (from PATHEXT) need not match the on-disk file's casing.
        Assert.Equal(existing, result.UpdatedPath, StringComparer.OrdinalIgnoreCase);
    }

    private static byte[] BuildZipBytes(Action<ZipArchive> populate)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            populate(archive);
        return ms.ToArray();
    }

    private static void AddZipEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8);
        writer.Write(content);
    }

    private sealed class FixedContentHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
            response.Content.Headers.ContentLength = content.Length;
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Install should not hit the network when the executable already exists.");
    }
}
