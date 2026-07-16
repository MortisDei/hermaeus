using System.Runtime.InteropServices;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aether.Core.Models;
using Aether.Services.ProcessManagement;

namespace Aether.Services;

/// <summary>
/// The supported llama.cpp release platforms. Kept independent of
/// OperatingSystem.Is*/RuntimeInformation.ProcessArchitecture so
/// SelectDownloadAsset can be exercised against every platform's asset
/// naming from a single test run instead of only whichever OS runs CI
/// (r11 1.2 acceptance).
/// </summary>
public enum LlamaPlatform { WinX64, WinArm64, LinuxX64, LinuxArm64, MacX64, MacArm64 }

/// <summary>
/// Manages llama-server binary installation and detection.
///
/// r11 1.1/1.2: the previously pinned asset names (llama-server-b4341-*)
/// named files that do not exist in any llama.cpp release; every release
/// ships zip (Windows) / tar.gz (Linux/macOS) archives named
/// llama-&lt;tag&gt;-bin-&lt;os&gt;[-cpu]-&lt;arch&gt;.&lt;ext&gt;, verified against the live
/// GitHub API for tag b10034 on 2026-07-16. Both the pinned path
/// (InstallAsync) and the latest-release path (InstallLatestAsync) now
/// download the real archive and extract it through ArchiveExtractor
/// (zip-slip guarded) instead of moving a raw archive into place as if it
/// were the executable.
/// </summary>
public sealed class LlamaServerSetupService
{
    /// <summary>
    /// llama.cpp release tag this pinned install downloads. Verified against
    /// the live GitHub releases API on 2026-07-16: tag b10034, published
    /// 2026-07-15, assets confirmed for every platform below. GitHub does not
    /// publish per-asset hashes in the release API; provenance for the
    /// pinned path is tag+HTTPS+GitHub-origin only (recorded in
    /// docs/security-review.md).
    /// </summary>
    public const string PinnedTag = "b10034";

    private const string ReleaseRepo = "ggerganov/llama.cpp";
    private const string ReleaseBaseUrl = $"https://github.com/{ReleaseRepo}/releases/download";
    private const string ReleaseApiBaseUrl = $"https://api.github.com/repos/{ReleaseRepo}/releases";

    private readonly ModelDownloadService _downloader;
    private readonly HttpClient _http;

    public LlamaServerSetupService(ModelDownloadService? downloader = null, HttpClient? http = null)
    {
        _downloader = downloader ?? new ModelDownloadService();
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        // Set once at construction (r11 1.2): GetLatestDownloadInfoAsync used to
        // call ParseAdd on every invocation, appending a duplicate UA product
        // to the shared client's DefaultRequestHeaders on each call.
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Aether-Doctor/1.0");
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

                var found = FindInstalledExecutable(installPath);
                if (found is not null)
                    searchPaths.Add(found);
            }

            var pathResult = ExecutableResolver.FindOnPath(exeName);
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
    /// Gets the expected path to the llama-server executable directly inside
    /// installPath. The archive may actually extract it into a nested
    /// subdirectory; use <see cref="FindInstalledExecutable"/> after install
    /// to get the real, possibly-nested, location.
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

        var platform = CurrentPlatform();
        if (platform is null)
            return actions;

        var displayName = DisplayNameFor(platform.Value);
        var downloadUrl = $"{ReleaseBaseUrl}/{PinnedTag}/{AssetNameFor(platform.Value, PinnedTag)}";
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
        return Enum.GetValues<LlamaPlatform>()
            .Select(platform => new LlamaServerReleaseInfo(
                DisplayNameFor(platform),
                $"{ReleaseBaseUrl}/{PinnedTag}/{AssetNameFor(platform, PinnedTag)}"))
            .ToList();
    }

    /// <summary>
    /// Downloads and installs llama-server binary at the pinned tag.
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

            var existing = FindInstalledExecutable(installPath);
            if (existing is not null)
            {
                progress?.Report($"llama-server already exists at {existing}");
                return new LocalAiSetupResult(true, $"llama-server is ready at {existing}", existing);
            }

            var platform = CurrentPlatform();
            if (platform is null)
                return new LocalAiSetupResult(false, $"Unsupported platform: {CurrentPlatformDisplayName()}");

            var assetName = AssetNameFor(platform.Value, PinnedTag);
            var displayName = DisplayNameFor(platform.Value);
            var url = $"{ReleaseBaseUrl}/{PinnedTag}/{assetName}";

            return await DownloadExtractAndLocateAsync(installPath, url, assetName, $"llama-server {PinnedTag} ({displayName})", progress, ct);
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

    /// <summary>
    /// Downloads and installs the latest llama.cpp release for the current platform.
    /// Extracts into a tag-versioned subdirectory of installPath rather than
    /// installPath itself: installPath usually already holds a previous
    /// install whose exe/DLLs are memory-mapped by any llama-server process
    /// still running (e.g. a chat and an embeddings server sharing one
    /// binary), and overwriting those files in place fails on Windows with a
    /// file-in-use error. A fresh subdirectory never touches files a running
    /// process holds open; existing servers keep running unaffected until
    /// they're next restarted against the new path.
    /// </summary>
    public async Task<LocalAiSetupResult> InstallLatestAsync(
        string installPath,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            progress?.Report("Checking latest llama.cpp release...");
            var release = await GetLatestDownloadInfoAsync(ct);
            var versionedInstallPath = Path.Combine(installPath, release.TagName);
            Directory.CreateDirectory(versionedInstallPath);

            return await DownloadExtractAndLocateAsync(
                versionedInstallPath, release.Url, release.AssetName, $"llama-server {release.TagName} ({release.DisplayName})", progress, ct);
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

    private async Task<LocalAiSetupResult> DownloadExtractAndLocateAsync(
        string installPath,
        string url,
        string assetName,
        string label,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var archivePath = Path.Combine(installPath, assetName);
        progress?.Report($"Downloading {label}...");
        var lastPercent = -1;
        var downloadProgress = progress is null
            ? null
            : new Progress<DownloadProgress>(state =>
            {
                var percent = (int)Math.Floor(state.PercentComplete);
                if (percent <= lastPercent)
                    return;

                lastPercent = percent;
                progress.Report($"Downloading {label}... {percent}%");
            });

        var downloadResult = await _downloader.DownloadAsync(url, archivePath, progress: downloadProgress, ct: ct);
        if (!downloadResult.Success)
            return new LocalAiSetupResult(false, $"Failed to download: {downloadResult.Message}");

        try
        {
            progress?.Report("Extracting archive...");
            await ArchiveExtractor.ExtractAsync(archivePath, installPath, ct);
        }
        finally
        {
            try { File.Delete(archivePath); }
            catch { }
        }

        var exePath = FindInstalledExecutable(installPath);
        if (exePath is null)
            return new LocalAiSetupResult(false, $"Archive extracted but no llama-server executable was found under {installPath}.");

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var fileInfo = new FileInfo(exePath);
                fileInfo.UnixFileMode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            }
            catch { }
        }

        progress?.Report($"{label} installed at {exePath}");
        return new LocalAiSetupResult(true, $"{label} installed at {exePath}", exePath);
    }

    /// <summary>Direct probe, then a recursive search: archives sometimes nest the binary under a build/bin-style subdirectory.</summary>
    private static string? FindInstalledExecutable(string installPath)
    {
        if (!Directory.Exists(installPath))
            return null;

        var direct = ExecutableResolver.ResolveInDirectory(installPath, "llama-server");
        if (direct is not null)
            return direct;

        var matches = ExecutableResolver.FindAllInDirectory(installPath, "llama-server", SearchOption.AllDirectories);
        return matches.Count > 0 ? matches[0] : null;
    }

    public async Task<LlamaServerLatestDownload> GetLatestDownloadInfoAsync(CancellationToken ct = default)
    {
        var release = await _http.GetFromJsonAsync<GitHubRelease>($"{ReleaseApiBaseUrl}/latest", ct)
            ?? throw new InvalidOperationException("GitHub did not return llama.cpp release metadata.");
        var asset = SelectDownloadAsset(release.Assets ?? []);
        if (asset is null)
            throw new InvalidOperationException($"No llama-server asset matched this platform in release {release.TagName}.");

        return new LlamaServerLatestDownload(
            release.TagName,
            asset.Name,
            asset.BrowserDownloadUrl,
            CurrentPlatformDisplayName());
    }

    /// <summary>
    /// Selects the default (no accelerator) build for a platform: Windows
    /// publishes an explicit "-cpu-" segment; Linux/macOS CPU builds carry no
    /// extra variant token between the os and arch segments, so an exact
    /// suffix match on "-bin-&lt;os&gt;[-cpu]-&lt;arch&gt;.&lt;ext&gt;" is what tells the
    /// plain build apart from the cuda/rocm/hip/vulkan/sycl/opencl/openvino
    /// variants published alongside it (r11 1.1/1.2, verified against the
    /// live b10034 asset list on 2026-07-16).
    /// </summary>
    public static GitHubReleaseAsset? SelectDownloadAsset(IReadOnlyList<GitHubReleaseAsset> assets, LlamaPlatform? platform = null)
    {
        var resolvedPlatform = platform ?? CurrentPlatform();
        if (resolvedPlatform is null)
            return null;

        var suffix = SuffixFor(resolvedPlatform.Value);
        return assets.FirstOrDefault(asset =>
            asset.Name.StartsWith("llama-", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static string AssetNameFor(LlamaPlatform platform, string tag) => $"llama-{tag}{SuffixFor(platform)}";

    private static string SuffixFor(LlamaPlatform platform) => platform switch
    {
        LlamaPlatform.WinX64 => "-bin-win-cpu-x64.zip",
        LlamaPlatform.WinArm64 => "-bin-win-cpu-arm64.zip",
        LlamaPlatform.LinuxX64 => "-bin-ubuntu-x64.tar.gz",
        LlamaPlatform.LinuxArm64 => "-bin-ubuntu-arm64.tar.gz",
        LlamaPlatform.MacX64 => "-bin-macos-x64.tar.gz",
        LlamaPlatform.MacArm64 => "-bin-macos-arm64.tar.gz",
        _ => throw new ArgumentOutOfRangeException(nameof(platform))
    };

    private static string DisplayNameFor(LlamaPlatform platform) => platform switch
    {
        LlamaPlatform.WinX64 => "Windows x64 CPU",
        LlamaPlatform.WinArm64 => "Windows ARM64 CPU",
        LlamaPlatform.LinuxX64 => "Linux x64",
        LlamaPlatform.LinuxArm64 => "Linux ARM64",
        LlamaPlatform.MacX64 => "macOS x64",
        LlamaPlatform.MacArm64 => "macOS ARM64",
        _ => throw new ArgumentOutOfRangeException(nameof(platform))
    };

    public static LlamaPlatform? CurrentPlatform()
    {
        var arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        if (OperatingSystem.IsWindows()) return arm ? LlamaPlatform.WinArm64 : LlamaPlatform.WinX64;
        if (OperatingSystem.IsLinux()) return arm ? LlamaPlatform.LinuxArm64 : LlamaPlatform.LinuxX64;
        if (OperatingSystem.IsMacOS()) return arm ? LlamaPlatform.MacArm64 : LlamaPlatform.MacX64;
        return null;
    }

    private static string GetExecutableName() => OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server";

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
