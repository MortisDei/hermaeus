using System.Security.Cryptography;
using System.Net.Http.Headers;

namespace Aether.Services;

/// <summary>
/// Handles downloading model files (GGUF, binaries) from remote sources.
/// Supports resumable downloads, progress tracking, and file integrity verification.
/// </summary>
public sealed class ModelDownloadService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);
    private static readonly HttpClient _defaultHttp = new() { Timeout = DefaultTimeout };
    private readonly HttpClient _http;

    public ModelDownloadService(HttpClient? http = null)
    {
        _http = http ?? _defaultHttp;
    }

    /// <summary>
    /// Downloads a file from a URL to a destination path with progress reporting.
    /// Supports resumable downloads using temporary .tmp files.
    /// </summary>
    public async Task<DownloadResult> DownloadAsync(
        string url,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        try
        {
            var tempPath = destinationPath + ".tmp";
            var destDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destDir))
                Directory.CreateDirectory(destDir);

            var existingSize = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0L;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existingSize > 0)
                request.Headers.Range = new RangeHeaderValue(existingSize, null);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                return new DownloadResult(
                    false,
                    $"Failed to download from {url}: HTTP {(int)response.StatusCode}",
                    null);
            }

            var canResume = existingSize > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
            var totalBytes = canResume
                ? existingSize + (response.Content.Headers.ContentLength ?? 0L)
                : response.Content.Headers.ContentLength ?? 0L;

            var fileMode = canResume ? FileMode.Append : FileMode.Create;
            var startByte = canResume ? existingSize : 0L;

            using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
            using (var fileStream = new FileStream(tempPath, fileMode, FileAccess.Write, FileShare.None, 81920, FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                long downloadedBytes = startByte;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) != 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                    downloadedBytes += bytesRead;

                    var progressBytes = totalBytes > 0 ? downloadedBytes : 0L;
                    var percentComplete = totalBytes > 0 ? (double)progressBytes / totalBytes * 100 : 0;
                    progress?.Report(new DownloadProgress(progressBytes, totalBytes, percentComplete));
                }
            }

            File.Move(tempPath, destinationPath, overwrite: true);

            var finalSize = new FileInfo(destinationPath).Length;
            return new DownloadResult(true, $"Downloaded {FormatBytes(finalSize)} to {destinationPath}", destinationPath);
        }
        catch (OperationCanceledException)
        {
            return new DownloadResult(false, "Download cancelled", null);
        }
        catch (Exception ex)
        {
            return new DownloadResult(false, $"Download failed: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Verifies a file's integrity using SHA256 hash if provided.
    /// </summary>
    public async Task<bool> VerifyHashAsync(
        string filePath,
        string? expectedSha256 = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
            return true;

        if (!File.Exists(filePath))
        {
            progress?.Report($"File not found: {filePath}");
            return false;
        }

        try
        {
            progress?.Report("Computing SHA256 hash...");
            using var fs = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            var hash = await Task.Run(() => sha256.ComputeHash(fs), ct);
            var hashHex = Convert.ToHexString(hash).ToLowerInvariant();
            var expectedLower = expectedSha256.Trim().ToLowerInvariant();

            var isValid = hashHex == expectedLower;
            if (isValid)
                progress?.Report($"Hash verified: {hashHex}");
            else
                progress?.Report($"Hash mismatch. Expected: {expectedLower}, Got: {hashHex}");

            return isValid;
        }
        catch (Exception ex)
        {
            progress?.Report($"Hash verification failed: {ex.Message}");
            return false;
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        > 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        > 1_048_576 => $"{bytes / 1_048_576.0:F2} MB",
        > 1024 => $"{bytes / 1024.0:F2} KB",
        _ => $"{bytes} B"
    };
}

/// <summary>
/// Result of a download operation.
/// </summary>
public sealed record DownloadResult(bool Success, string Message, string? DownloadedPath);

/// <summary>
/// Progress information for an ongoing download.
/// </summary>
public sealed record DownloadProgress(long BytesDownloaded, long TotalBytes, double PercentComplete);
