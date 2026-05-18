using System.Runtime.InteropServices;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aether.Core.Models;

namespace Aether.Services;

/// <summary>
/// Manages llama-server binary installation and detection.
/// Handles platform-specific binary downloads from GitHub releases.
/// </summary>
public sealed class LlamaServerSetupService
{
    private static readonly DownloadDefinition[] DownloadDefinitions =
    [
        new(() => OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "Windows x64 AVX2",
            "llama-server-b4341-win-avx2.exe"),
        new(() => OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64,
            "Windows ARM64",
            "llama-server-b4341-win-arm64.exe"),
        new(() => OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "Linux x64",
            "llama-server-b4341-linux-x64"),
        new(() => OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64,
            "Linux ARM64",
            "llama-server-b4341-linux-arm64"),
        new(() => OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "macOS x64",
            "llama-server-b4341-macos-x64"),
        new(() => OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64,
            "macOS ARM64",
            "llama-server-b4341-macos-arm64")
    ];

    private readonly ModelDownloadService _downloader;
    private readonly HttpClient _http;

    public LlamaServerSetupService(ModelDownloadService? downloader = null, HttpClient? http = null)
    {
        _downloader = downloader ?? new ModelDownloadService();
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    /// <summary>
    /// Detects if llama-server binary is already installed and accessible.
    /// </summary>
    public bool IsInstalled(string? installPath = null)
    {
        try
        {
            var exeName = GetExecutableName();
            var searchPaths = new List<string>();

            if (!string.IsNullOrWhiteSpace(installPath))
            {
                if (File.Exists(installPath))
                    searchPaths.Add(installPath);
                searchPaths.Add(Path.Combine(installPath, exeName));
            }

            var pathResult = FindInPath(exeName);
            if (pathResult != null)
                searchPaths.Add(pathResult);
            
            searchPaths.Add(Path.Combine(AppContext.BaseDirectory, exeName));

            return searchPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Any(File.Exists);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the default installation path for llama-server.
    /// </summary>
    public string GetDefaultInstallPath(string baseAssetsRoot)
    {
        return Path.Combine(baseAssetsRoot, "llama-server");
    }

    /// <summary>
    /// Gets the full path to the llama-server executable.
    /// </summary>
    public string GetExecutablePath(string installPath)
    {
        return Path.Combine(installPath, GetExecutableName());
    }

    /// <summary>
    /// Generates setup actions for installing llama-server if needed.
    /// </summary>
    public List<LocalAiSetupAction> GetSetupActions(string installPath)
    {
        var actions = new List<LocalAiSetupAction>();

        if (IsInstalled(installPath))
            return actions;

        var (downloadUrl, displayName) = GetDownloadInfo();
        var exePath = GetExecutablePath(installPath);

        actions.Add(new(
            "download-llama-server",
            LocalAiSetupActionKind.DownloadLlamaServer,
            $"Download llama-server ({displayName})",
            exePath,
            [downloadUrl],
            LocalAiSetupRiskLevel.Low,
            $"llama-server binary downloaded to {exePath}",
            RequiresNetwork: true,
            RequiresApproval: true,
            CanRun: true));

        return actions;
    }

    public IReadOnlyList<LlamaServerReleaseInfo> GetSupportedReleaseInfo()
    {
        var baseUrl = "https://github.com/ggerganov/llama.cpp/releases/download/b4341";
        return DownloadDefinitions
            .Select(def => new LlamaServerReleaseInfo(def.DisplayName, $"{baseUrl}/{def.AssetName}"))
            .ToList();
    }

    /// <summary>
    /// Downloads and installs llama-server binary.
    /// </summary>
    public async Task<LocalAiSetupResult> InstallAsync(
        string installPath,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            progress?.Report("Preparing llama-server installation...");
            Directory.CreateDirectory(installPath);

            var (downloadUrl, displayName) = GetDownloadInfo();
            var exePath = GetExecutablePath(installPath);

            if (File.Exists(exePath))
            {
                progress?.Report($"llama-server already exists at {exePath}");
                return new LocalAiSetupResult(true, $"llama-server is ready at {exePath}", exePath);
            }

            progress?.Report($"Downloading llama-server ({displayName})...");
            var lastPercent = -1;
            var downloadProgress = progress is null
                ? null
                : new Progress<DownloadProgress>(state =>
                {
                    var percent = (int)Math.Floor(state.PercentComplete);
                    if (percent <= lastPercent)
                        return;

                    lastPercent = percent;
                    progress.Report($"Downloading llama-server ({displayName})... {percent}%");
                });
            var downloadResult = await _downloader.DownloadAsync(
                downloadUrl,
                exePath,
                progress: downloadProgress,
                ct: ct);

            if (!downloadResult.Success)
                return new LocalAiSetupResult(false, $"Failed to download: {downloadResult.Message}");

            progress?.Report("Making executable...");
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    var fileInfo = new System.IO.FileInfo(exePath);
                    var unixFileMode = fileInfo.UnixFileMode | System.IO.UnixFileMode.UserExecute |
                                      System.IO.UnixFileMode.GroupExecute | System.IO.UnixFileMode.OtherExecute;
                    fileInfo.UnixFileMode = unixFileMode;
                }
                catch { }
            }

            progress?.Report($"llama-server installed successfully at {exePath}");
            return new LocalAiSetupResult(true, $"llama-server installed at {exePath}", exePath);
        }
        catch (OperationCanceledException)
        {
            return new LocalAiSetupResult(false, "Installation cancelled");
        }
        catch (Exception ex)
        {
            return new LocalAiSetupResult(false, $"Installation failed: {ex.Message}");
        }
    }

    public async Task<LocalAiSetupResult> InstallLatestAsync(
        string installPath,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            progress?.Report("Checking latest llama.cpp release...");
            Directory.CreateDirectory(installPath);
            var release = await GetLatestDownloadInfoAsync(ct);
            var exePath = GetExecutablePath(installPath);
            var tempPath = $"{exePath}.download";
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            progress?.Report($"Downloading llama-server {release.TagName} ({release.DisplayName})...");
            var lastPercent = -1;
            var downloadProgress = progress is null
                ? null
                : new Progress<DownloadProgress>(state =>
                {
                    var percent = (int)Math.Floor(state.PercentComplete);
                    if (percent <= lastPercent)
                        return;

                    lastPercent = percent;
                    progress.Report($"Downloading llama-server {release.TagName}... {percent}%");
                });

            var downloadResult = await _downloader.DownloadAsync(release.Url, tempPath, downloadProgress, ct);
            if (!downloadResult.Success)
                return new LocalAiSetupResult(false, $"Failed to download: {downloadResult.Message}");

            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    var fileInfo = new FileInfo(tempPath);
                    fileInfo.UnixFileMode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                }
                catch { }
            }

            if (File.Exists(exePath))
                File.Delete(exePath);
            File.Move(tempPath, exePath);

            progress?.Report($"llama-server {release.TagName} installed at {exePath}");
            return new LocalAiSetupResult(true, $"llama-server {release.TagName} installed at {exePath}", exePath);
        }
        catch (OperationCanceledException)
        {
            return new LocalAiSetupResult(false, "Installation cancelled");
        }
        catch (Exception ex)
        {
            return new LocalAiSetupResult(false, $"Installation failed: {ex.Message}");
        }
    }

    public async Task<LlamaServerLatestDownload> GetLatestDownloadInfoAsync(CancellationToken ct = default)
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Aether-Doctor/1.0");
        var release = await _http.GetFromJsonAsync<GitHubRelease>(
            "https://api.github.com/repos/ggerganov/llama.cpp/releases/latest",
            ct) ?? throw new InvalidOperationException("GitHub did not return llama.cpp release metadata.");
        var asset = SelectDownloadAsset(release.Assets ?? []);
        if (asset is null)
            throw new InvalidOperationException($"No llama-server asset matched this platform in release {release.TagName}.");

        return new LlamaServerLatestDownload(
            release.TagName,
            asset.Name,
            asset.BrowserDownloadUrl,
            CurrentPlatformDisplayName());
    }

    public static GitHubReleaseAsset? SelectDownloadAsset(IReadOnlyList<GitHubReleaseAsset> assets)
    {
        var candidates = assets
            .Where(asset => asset.Name.Contains("llama-server", StringComparison.OrdinalIgnoreCase))
            .ToList();

        bool Has(string value, string token) => value.Contains(token, StringComparison.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            var arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            return candidates.FirstOrDefault(asset =>
                Has(asset.Name, "win") && (arm ? Has(asset.Name, "arm64") : !Has(asset.Name, "arm64") && (Has(asset.Name, "x64") || Has(asset.Name, "avx2"))));
        }

        if (OperatingSystem.IsLinux())
        {
            var arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            return candidates.FirstOrDefault(asset =>
                Has(asset.Name, "linux") && (arm ? Has(asset.Name, "arm64") : Has(asset.Name, "x64")));
        }

        if (OperatingSystem.IsMacOS())
        {
            var arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            return candidates.FirstOrDefault(asset =>
                (Has(asset.Name, "macos") || Has(asset.Name, "darwin")) && (arm ? Has(asset.Name, "arm64") : Has(asset.Name, "x64")));
        }

        return null;
    }

    private static string GetExecutableName()
    {
        return OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server";
    }

    private static (string url, string displayName) GetDownloadInfo()
    {
        var match = DownloadDefinitions.FirstOrDefault(def => def.MatchesCurrentPlatform());
        if (match is null)
            throw new NotSupportedException($"Unsupported platform: {(OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsMacOS() ? "macOS" : "Unknown")} {RuntimeInformation.ProcessArchitecture}");

        return ($"https://github.com/ggerganov/llama.cpp/releases/download/b4341/{match.AssetName}", match.DisplayName);
    }

    private static string? FindInPath(string exeName)
    {
        try
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathEnv))
                return null;

            var separator = OperatingSystem.IsWindows() ? ';' : ':';
            foreach (var dir in pathEnv.Split(separator))
            {
                var trimmed = dir.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                var fullPath = Path.Combine(trimmed, exeName);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }
        catch { }

        return null;
    }

    private sealed record DownloadDefinition(Func<bool> MatchesCurrentPlatform, string DisplayName, string AssetName);

    private static string CurrentPlatformDisplayName()
    {
        var os = OperatingSystem.IsWindows() ? "Windows"
            : OperatingSystem.IsLinux() ? "Linux"
            : OperatingSystem.IsMacOS() ? "macOS"
            : "Unknown";
        return $"{os} {RuntimeInformation.ProcessArchitecture}";
    }
}

public sealed record LlamaServerReleaseInfo(string DisplayName, string Url);
public sealed record LlamaServerLatestDownload(string TagName, string AssetName, string Url, string DisplayName);
public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("assets")] List<GitHubReleaseAsset>? Assets);
public sealed record GitHubReleaseAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
