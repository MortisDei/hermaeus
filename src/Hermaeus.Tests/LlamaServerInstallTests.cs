using System.IO.Compression;
using System.Formats.Tar;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class LlamaServerInstallTests
{
    /// <summary>r11 1.1/1.2: InstallAsync must download the real pinned archive, extract it (zip-slip guarded), and hand back a path ServerProcessManager can actually start, not a raw archive moved into place as an .exe.</summary>
    [Fact]
    public async Task InstallAsync_extracts_the_pinned_archive_and_produces_a_runnable_executable()
    {
        using var temp = new TempDir();
        var installDir = temp.PathFor("install");

        var archiveBytes = BuildPlatformArchiveBytes();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant();

        using var http = new HttpClient(new FixedContentHandler(archiveBytes));
        var service = new LlamaServerSetupService(new ModelDownloadService(http), http, _ => expectedSha256);

        var result = await service.InstallAsync(installDir);

        Assert.True(result.Success, result.Log);
        Assert.NotNull(result.UpdatedPath);
        Assert.True(File.Exists(result.UpdatedPath), "installed executable should exist on disk");
        Assert.Equal("stub-binary-content", await File.ReadAllTextAsync(result.UpdatedPath!));

        var siblingLibrary = Path.Combine(
            Path.GetDirectoryName(result.UpdatedPath)!,
            OperatingSystem.IsWindows() ? "ggml.dll" : "libggml.so");
        Assert.True(File.Exists(siblingLibrary), "runtime companion libraries must extract next to the executable");

        // Downloaded archive must not be left behind next to the extracted binary.
        Assert.DoesNotContain(Directory.EnumerateFiles(installDir, "*", SearchOption.AllDirectories),
            path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase));
    }

    [WindowsOnlyFact]
    public async Task InstallAsync_accepts_a_flat_windows_archive_when_a_wrapper_is_expected()
    {
        using var temp = new TempDir();
        var installDir = temp.PathFor("install");
        var archiveBytes = BuildZipBytes(archive =>
        {
            AddZipEntry(archive, "llama-server.exe", "stub-binary-content");
            AddZipEntry(archive, "ggml.dll", "sibling-dll-content");
            AddZipEntry(archive, "ggml-cuda.dll", "cuda-dll-content");
        });
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant();

        using var http = new HttpClient(new FixedContentHandler(archiveBytes));
        var service = new LlamaServerSetupService(new ModelDownloadService(http), http, _ => expectedSha256);

        var result = await service.InstallAsync(installDir);

        Assert.True(result.Success, result.Log);
        Assert.NotNull(result.UpdatedPath);
        Assert.Equal("stub-binary-content", await File.ReadAllTextAsync(result.UpdatedPath!));
        Assert.Equal("cuda-dll-content", await File.ReadAllTextAsync(Path.Combine(installDir, "ggml-cuda.dll")));
    }

    [Fact]
    public async Task InstallAsync_refuses_an_archive_that_fails_SHA256_verification()
    {
        using var temp = new TempDir();
        var installDir = temp.PathFor("install");
        var archiveBytes = BuildPlatformArchiveBytes();

        using var http = new HttpClient(new FixedContentHandler(archiveBytes));
        var service = new LlamaServerSetupService(new ModelDownloadService(http), http, _ => new string('0', 64));

        var result = await service.InstallAsync(installDir);

        Assert.False(result.Success);
        Assert.Contains("SHA256", result.Log, StringComparison.Ordinal);
        Assert.Null(LlamaServerSetupService.ResolveInstalledExecutable(installDir));
        Assert.Empty(Directory.EnumerateFiles(installDir, "*", SearchOption.AllDirectories));
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

    private static byte[] BuildPlatformArchiveBytes()
    {
        if (OperatingSystem.IsWindows())
        {
            return BuildZipBytes(archive =>
            {
                AddZipEntry(archive, "llama-b10034/llama-server.exe", "stub-binary-content");
                AddZipEntry(archive, "llama-b10034/ggml.dll", "sibling-dll-content");
            });
        }

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var archive = new TarWriter(gzip, leaveOpen: true))
        {
            AddTarEntry(archive, "llama-b10034/llama-server", "stub-binary-content");
            AddTarEntry(archive, "llama-b10034/libggml.so", "sibling-library-content");
        }
        return output.ToArray();
    }

    private static void AddTarEntry(TarWriter archive, string name, string content)
    {
        var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
        {
            DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content))
        };
        archive.WriteEntry(entry);
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
    /// A rejected request stays actionable without asserting a rate limit
    /// unless GitHub's response establishes one.
    /// </summary>
    [Fact]
    public async Task GetLatestDownloadInfoAsync_preserves_uncertainty_for_an_unqualified_403()
    {
        using var http = new HttpClient(new FixedStatusHandler(HttpStatusCode.Forbidden));
        var service = new LlamaServerSetupService(new ModelDownloadService(http), http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetLatestDownloadInfoAsync(LlamaRuntimeVariant.Cpu));

        Assert.Contains("rate limit", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HTTP 403", ex.Message);
        Assert.Contains("may be responsible", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "0")]
    [InlineData(HttpStatusCode.TooManyRequests, null)]
    public async Task GetLatestDownloadInfoAsync_reports_a_confirmed_rate_limit(HttpStatusCode status, string? remaining)
    {
        using var http = new HttpClient(new FixedStatusHandler(status, remaining));
        var service = new LlamaServerSetupService(new ModelDownloadService(http), http);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetLatestDownloadInfoAsync());

        Assert.Contains("GitHub rate-limited", error.Message);
        Assert.Contains($"HTTP {(int)status}", error.Message);
        Assert.Contains("reset", error.Message);
    }

    [Fact]
    public async Task Doctor_preserves_the_failed_llama_release_lookup_reason()
    {
        using var http = new HttpClient(new FixedStatusHandler(HttpStatusCode.Forbidden, "0"));
        var setup = new LlamaServerSetupService(new ModelDownloadService(http), http);

        var lookup = await DoctorService.FetchLatestLlamaReleaseAsync(setup, CancellationToken.None);

        Assert.Null(lookup.Release);
        Assert.Contains("GitHub rate-limited", lookup.Error);
        Assert.Contains("HTTP 403", lookup.Error);
    }

    [Fact]
    public async Task Doctor_preserves_non_rate_limit_http_failures_without_inventing_a_limit()
    {
        using var http = new HttpClient(new FixedStatusHandler(HttpStatusCode.ServiceUnavailable));
        var setup = new LlamaServerSetupService(new ModelDownloadService(http), http);

        var lookup = await DoctorService.FetchLatestLlamaReleaseAsync(setup, CancellationToken.None);

        Assert.Null(lookup.Release);
        Assert.Contains("503", lookup.Error);
        Assert.DoesNotContain("rate limit", lookup.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Doctor_llama_lookup_preserves_owner_cancellation()
    {
        using var http = new HttpClient(new FixedStatusHandler(HttpStatusCode.OK));
        var setup = new LlamaServerSetupService(new ModelDownloadService(http), http);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DoctorService.FetchLatestLlamaReleaseAsync(setup, cts.Token));
    }

    private sealed class FixedStatusHandler(HttpStatusCode status, string? remaining = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Response());

        private HttpResponseMessage Response()
        {
            var response = new HttpResponseMessage(status);
            if (remaining is not null)
                response.Headers.Add("X-RateLimit-Remaining", remaining);
            return response;
        }
    }
}
