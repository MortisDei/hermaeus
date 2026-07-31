using System.IO.Compression;
using System.Net;
using System.Text;
using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class LlamaServerInstallTests
{
    /// <summary>r11 1.1/1.2: InstallAsync must download the real pinned archive, extract it (zip-slip guarded), and hand back a path ServerProcessManager can actually start, not a raw archive moved into place as an .exe.</summary>
    [WindowsOnlyFact]
    public async Task InstallAsync_extracts_the_pinned_archive_and_produces_a_runnable_executable()
    {
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

    [WindowsOnlyFact]
    public async Task InstallAsync_is_idempotent_when_the_executable_already_exists()
    {
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

    /// <summary>
    /// r24 field report: a real 403 from GitHub's anonymous rate limit
    /// surfaced to the user as the framework's generic
    /// "Response status code does not indicate success: 403 (rate limit
    /// exceeded)." GetLatestDownloadInfoAsync must turn that into an
    /// actionable message instead.
    /// </summary>
    [Fact]
    public async Task GetLatestDownloadInfoAsync_turns_a_403_into_an_actionable_rate_limit_message()
    {
        using var http = new HttpClient(new FixedStatusHandler(HttpStatusCode.Forbidden));
        var service = new LlamaServerSetupService(new ModelDownloadService(http), http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetLatestDownloadInfoAsync(LlamaRuntimeVariant.Cpu));

        Assert.Contains("rate limit", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hour", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixedStatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}
