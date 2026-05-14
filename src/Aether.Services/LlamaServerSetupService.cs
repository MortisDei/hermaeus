using System.Runtime.InteropServices;
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

    public LlamaServerSetupService(ModelDownloadService? downloader = null)
    {
        _downloader = downloader ?? new ModelDownloadService();
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
                searchPaths.Add(Path.Combine(installPath, exeName));

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
                var fullPath = Path.Combine(dir.Trim(), exeName);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }
        catch { }

        return null;
    }

    private sealed record DownloadDefinition(Func<bool> MatchesCurrentPlatform, string DisplayName, string AssetName);
}

public sealed record LlamaServerReleaseInfo(string DisplayName, string Url);
