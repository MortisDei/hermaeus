using System.Runtime.InteropServices;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Hermaeus.Core.Models;
using Hermaeus.Services.ProcessManagement;

namespace Hermaeus.Services;

/// <summary>
/// The supported llama.cpp release platforms. Kept independent of
/// OperatingSystem.Is*/RuntimeInformation.ProcessArchitecture so
/// SelectDownloadAsset can be exercised against every platform's asset
/// naming from a single test run instead of only whichever OS runs CI
/// (r11 1.2 acceptance).
/// </summary>
public enum LlamaPlatform { WinX64, WinArm64, LinuxX64, LinuxArm64, MacX64, MacArm64 }

/// <summary>GPU vendor inferred from an adapter name string (r14 1.1).</summary>
public enum GpuVendor { Unknown, Nvidia, Amd, Intel }

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
    /// 2026-07-15, assets confirmed for every platform below. The pinned path
    /// verifies source-controlled SHA256 values captured from GitHub's release
    /// asset digests before extracting or executing an archive.
    /// </summary>
    public const string PinnedTag = "b10034";

    // The project moved from ggerganov/ to the ggml-org/ organisation. GitHub
    // still redirects the old slug, so this kept working, but a redirect is not
    // a guarantee and the API path is the one that matters for release lookups.
    private const string ReleaseRepo = "ggml-org/llama.cpp";
    private const string ReleaseBaseUrl = $"https://github.com/{ReleaseRepo}/releases/download";
    private const string ReleaseApiBaseUrl = $"https://api.github.com/repos/{ReleaseRepo}/releases";

    private readonly ModelDownloadService _downloader;
    private readonly HttpClient _http;
    private readonly Func<LlamaPlatform, string> _pinnedSha256;

    public LlamaServerSetupService(ModelDownloadService? downloader = null, HttpClient? http = null)
        : this(downloader, http, null)
    {
    }

    internal LlamaServerSetupService(
        ModelDownloadService? downloader,
        HttpClient? http,
        Func<LlamaPlatform, string>? pinnedSha256)
    {
        _downloader = downloader ?? new ModelDownloadService();
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _pinnedSha256 = pinnedSha256 ?? PinnedSha256For;
        // Set once at construction (r11 1.2): GetLatestDownloadInfoAsync used to
        // call ParseAdd on every invocation, appending a duplicate UA product
        // to the shared client's DefaultRequestHeaders on each call.
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Hermaeus-Doctor/1.0");
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

                var found = ResolveInstalledExecutable(installPath);
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
    /// subdirectory; use <see cref="ResolveInstalledExecutable"/> after install
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

            var existing = ResolveInstalledExecutable(installPath);
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

            return await DownloadExtractAndLocateAsync(
                installPath, url, assetName, _pinnedSha256(platform.Value),
                $"llama-server {PinnedTag} ({displayName})", progress, ct);
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
    public Task<LocalAiSetupResult> InstallLatestAsync(
        string installPath,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
        => InstallLatestAsync(installPath, LlamaRuntimeVariant.Cpu, progress, ct);

    public async Task<LocalAiSetupResult> InstallLatestAsync(
        string installPath,
        LlamaRuntimeVariant variant,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            progress?.Report("Checking latest llama.cpp release...");
            var release = await GetLatestDownloadInfoAsync(variant, ct);
            var versionedInstallPath = Path.Combine(installPath, release.TagName);
            Directory.CreateDirectory(versionedInstallPath);

            // CUDA builds link against the toolkit runtime shipped separately
            // (r14 1.2): extract it into the same versioned directory first so
            // the DLLs sit beside llama-server.exe before it is located/started.
            // r19 2.3: the companion asset only changes when llama.cpp bumps its
            // CUDA toolkit version, so a previous version directory's identical,
            // verified copy is reused instead of re-downloading several hundred
            // MB on every single update.
            if (!string.IsNullOrWhiteSpace(release.CompanionAssetName) && !string.IsNullOrWhiteSpace(release.CompanionUrl))
            {
                var reusedFrom = CudaRuntimeReuse.TryReuse(installPath, versionedInstallPath, release.CompanionAssetName!);
                if (reusedFrom is not null)
                {
                    progress?.Report($"Reusing CUDA runtime from {reusedFrom}");
                }
                else
                {
                    var companion = await DownloadAndExtractArchiveAsync(
                        versionedInstallPath, release.CompanionUrl!, release.CompanionAssetName!, release.CompanionSha256!,
                        $"CUDA runtime ({release.CompanionAssetName})", progress, ct);
                    if (!companion.Success)
                        return companion;

                    var extractedFiles = Directory.Exists(versionedInstallPath)
                        ? Directory.EnumerateFiles(versionedInstallPath, "*", SearchOption.AllDirectories)
                        : [];
                    CudaRuntimeReuse.WriteMarker(versionedInstallPath, release.CompanionAssetName!, extractedFiles);
                }
            }

            return await DownloadExtractAndLocateAsync(
                versionedInstallPath, release.Url, release.AssetName, release.Sha256,
                $"llama-server {release.TagName} ({release.DisplayName})", progress, ct);
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

    private async Task<LocalAiSetupResult> DownloadAndExtractArchiveAsync(
        string installPath,
        string url,
        string assetName,
        string expectedSha256,
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

        var verified = await _downloader.VerifyHashAsync(archivePath, expectedSha256, progress, ct);
        if (!verified)
        {
            try { File.Delete(archivePath); }
            catch { }
            return new LocalAiSetupResult(false, $"{label} failed SHA256 verification and was not extracted.");
        }

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

        return new LocalAiSetupResult(true, $"{label} extracted");
    }

    private async Task<LocalAiSetupResult> DownloadExtractAndLocateAsync(
        string installPath,
        string url,
        string assetName,
        string expectedSha256,
        string label,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var extracted = await DownloadAndExtractArchiveAsync(installPath, url, assetName, expectedSha256, label, progress, ct);
        if (!extracted.Success)
            return extracted;

        var exePath = ResolveInstalledExecutable(installPath);
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

    /// <summary>
    /// Nearest ancestor directory whose leaf is a release tag ("bNNNNN"), or
    /// null when the path is not inside a versioned directory (r14 3.2). Pure.
    /// </summary>
    public static string? NearestTagDirectoryName(string? path)
    {
        var current = string.IsNullOrWhiteSpace(path) ? null : Path.TrimEndingDirectorySeparator(path);
        while (!string.IsNullOrEmpty(current))
        {
            var leaf = Path.GetFileName(current);
            if (TagDirectoryPattern.IsMatch(leaf))
                return leaf;
            current = Path.GetDirectoryName(current);
        }
        return null;
    }

    /// <summary>
    /// From a set of sibling directory paths, selects the tag-pattern
    /// directories that are safe to prune: everything named "bNNNNN" except the
    /// tags to keep (the newly installed and the previously configured
    /// versions). Non-tag directories are always ignored (r14 3.2). Pure.
    /// </summary>
    public static IReadOnlyList<string> SelectPrunableVersionDirectories(IEnumerable<string> siblingDirectoryPaths, params string?[] keepTags)
    {
        var keep = new HashSet<string>(
            keepTags.Where(t => !string.IsNullOrWhiteSpace(t))!,
            StringComparer.OrdinalIgnoreCase);
        return siblingDirectoryPaths
            .Where(dir =>
            {
                var leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));
                return TagDirectoryPattern.IsMatch(leaf) && !keep.Contains(leaf);
            })
            .ToList();
    }

    /// <summary>
    /// Convenience overload that enumerates the install root and derives the
    /// keep set from the new and previous executable paths (r14 3.2). Returns
    /// an empty list when the root does not exist.
    /// </summary>
    public static IReadOnlyList<string> SelectPrunableVersionDirectories(string installRoot, string newExecutablePath, string? previousExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
            return [];
        var siblings = Directory.EnumerateDirectories(installRoot);
        return SelectPrunableVersionDirectories(
            siblings,
            NearestTagDirectoryName(newExecutablePath),
            NearestTagDirectoryName(previousExecutablePath));
    }

    /// <summary>
    /// Deletes the given version directories, returning bytes reclaimed. A
    /// directory whose files are still held open by a running server aborts
    /// only its own deletion (r14 3.2); it is offered again next time. Only
    /// ever call with paths from <see cref="SelectPrunableVersionDirectories"/>.
    /// </summary>
    public static long PruneVersionDirectories(IEnumerable<string> directories)
    {
        long reclaimed = 0;
        foreach (var dir in directories)
        {
            try
            {
                if (!Directory.Exists(dir))
                    continue;
                var size = DirectorySizeBytes(dir);
                Directory.Delete(dir, recursive: true);
                reclaimed += size;
            }
            catch
            {
                // Locked (server still running the old binary) or otherwise
                // undeletable: skip this directory without failing the rest.
            }
        }
        return reclaimed;
    }

    public static long DirectorySizeBytes(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Resolves an installed managed executable. Versioned b-number directories
    /// are ordered numerically so discovery selects the newest installed build
    /// rather than filesystem enumeration order; unversioned files are fallback.
    /// </summary>
    public static string? ResolveInstalledExecutable(string installPath)
    {
        if (!Directory.Exists(installPath))
            return null;

        var matches = ExecutableResolver.FindAllInDirectory(installPath, "llama-server", SearchOption.AllDirectories);
        return matches
            .OrderByDescending(path => TryParseBuildTag(NearestTagDirectoryName(path)) ?? -1)
            .ThenByDescending(path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();
    }

    public Task<LlamaServerLatestDownload> GetLatestDownloadInfoAsync(CancellationToken ct = default)
        => GetLatestDownloadInfoAsync(LlamaRuntimeVariant.Cpu, ct);

    public async Task<LlamaServerLatestDownload> GetLatestDownloadInfoAsync(LlamaRuntimeVariant variant, CancellationToken ct = default)
    {
        List<GitHubRelease>? releases;
        try
        {
            releases = await _http.GetFromJsonAsync<List<GitHubRelease>>($"{ReleaseApiBaseUrl}?per_page=30", ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                "GitHub's anonymous API rate limit (60 requests/hour per IP) was reached while checking for the "
                + "latest llama.cpp release. Wait about an hour and try again.", ex);
        }

        if (releases is null)
            throw new InvalidOperationException("GitHub did not return llama.cpp release metadata.");
        var platform = CurrentPlatform();
        var selection = SelectLatestCompatibleRelease(releases, platform, variant)
            ?? throw new InvalidOperationException("No b-numbered llama.cpp release contained a supported llama-server asset for this platform.");
        var release = selection.Release;
        var asset = selection.Asset;
        var effectiveVariant = selection.Variant;

        string? companionName = null;
        string? companionUrl = null;
        string? companionSha256 = null;
        if (effectiveVariant == LlamaRuntimeVariant.Cuda)
        {
            var companion = SelectCudartAsset(release.Assets ?? [], asset.Name);
            companionName = companion?.Name;
            companionUrl = companion?.BrowserDownloadUrl;
            companionSha256 = companion is null ? null : RequireSha256Digest(companion);
        }

        var display = platform is null
            ? CurrentPlatformDisplayName()
            : DisplayNameFor(platform.Value, effectiveVariant);

        return new LlamaServerLatestDownload(
            release.TagName,
            asset.Name,
            asset.BrowserDownloadUrl,
            display,
            RequireSha256Digest(asset),
            companionName,
            companionUrl,
            release.PublishedAt,
            companionSha256);
    }

    internal static string RequireSha256Digest(GitHubReleaseAsset asset)
    {
        const string prefix = "sha256:";
        var digest = asset.Digest;
        if (string.IsNullOrWhiteSpace(digest)
            || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"GitHub did not publish a SHA256 digest for release asset '{asset.Name}'.");
        }

        var hash = digest[prefix.Length..];
        if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidOperationException($"GitHub published an invalid SHA256 digest for release asset '{asset.Name}'.");

        return hash.ToLowerInvariant();
    }

    /// <summary>
    /// Selects the highest b-numbered release that also contains a compatible
    /// asset. Unrelated semver releases and incomplete release objects are
    /// skipped instead of becoming a false "latest" answer.
    /// </summary>
    public static LlamaReleaseSelection? SelectLatestCompatibleRelease(
        IReadOnlyList<GitHubRelease> releases,
        LlamaPlatform? platform,
        LlamaRuntimeVariant variant)
    {
        foreach (var release in releases
                     .Select(release => (Release: release, Build: TryParseBuildTag(release.TagName)))
                     .Where(candidate => candidate.Build is not null)
                     .OrderByDescending(candidate => candidate.Build))
        {
            var assets = release.Release.Assets ?? [];
            var asset = SelectDownloadAsset(assets, platform, variant);
            var effectiveVariant = variant;
            if (asset is null && variant != LlamaRuntimeVariant.Cpu)
            {
                asset = SelectDownloadAsset(assets, platform, LlamaRuntimeVariant.Cpu);
                effectiveVariant = LlamaRuntimeVariant.Cpu;
            }

            if (asset is not null)
                return new LlamaReleaseSelection(release.Release, asset, effectiveVariant, release.Build!.Value);
        }

        return null;
    }

    public static int? TryParseBuildTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        var match = Regex.Match(tag, @"^b(?<build>\d{3,7})$", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["build"].Value, out var build) ? build : null;
    }

    private static readonly Regex TagDirectoryPattern = new(@"^b\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CudaVersionPattern = new(@"-cuda-(?<ver>\d+(?:\.\d+)?)-", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Classifies a GPU adapter name into a vendor (r14 1.1). Pure string
    /// function so the Auto variant decision is unit-testable without a real
    /// adapter. Matching is case-insensitive substring; the first vendor whose
    /// marker appears wins.
    /// </summary>
    public static GpuVendor ClassifyGpuVendor(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName))
            return GpuVendor.Unknown;

        var name = gpuName.ToLowerInvariant();
        if (name.Contains("nvidia") || name.Contains("geforce") || name.Contains("rtx") ||
            name.Contains("gtx") || name.Contains("quadro") || name.Contains("tesla"))
            return GpuVendor.Nvidia;
        if (name.Contains("radeon") || name.Contains("amd") || name.Contains("firepro"))
            return GpuVendor.Amd;
        if (name.Contains("arc") || name.Contains("iris") || name.Contains("uhd") || name.Contains("intel"))
            return GpuVendor.Intel;
        return GpuVendor.Unknown;
    }

    /// <summary>
    /// Resolves a configured (possibly Auto) variant against the hardware
    /// snapshot into a concrete build to install (r14 1.1). Auto: an NVIDIA GPU
    /// picks Cuda, any other real GPU (with reported VRAM) picks Vulkan, and no
    /// GPU picks Cpu. An explicit non-Auto choice always wins.
    /// </summary>
    public static LlamaRuntimeVariant ResolveVariant(LlamaRuntimeVariant configured, HardwareProfile? profile)
    {
        if (configured != LlamaRuntimeVariant.Auto)
            return configured;

        var hasRealGpu = profile is { MaxGpuVramBytes: > 0 } || !string.IsNullOrWhiteSpace(profile?.GpuName);
        if (!hasRealGpu)
            return LlamaRuntimeVariant.Cpu;

        return ClassifyGpuVendor(profile?.GpuName) == GpuVendor.Nvidia
            ? LlamaRuntimeVariant.Cuda
            : LlamaRuntimeVariant.Vulkan;
    }

    /// <summary>
    /// Walks up from a llama-server executable's directory to the install root
    /// (r14 3.1). Each successful update extracts into a new "bNNNNN" tag
    /// subdirectory, so the executable's own directory is usually a version
    /// directory; installing the next update there would nest one level deeper
    /// forever. This skips consecutive tag-pattern leaves and returns the first
    /// non-tag ancestor, but never crosses into a drive/filesystem root, so an
    /// install root that is itself legitimately named like a tag is preserved.
    /// Pure path arithmetic, no filesystem access.
    /// </summary>
    public static string ResolveInstallRoot(string executableDirectory)
    {
        if (string.IsNullOrWhiteSpace(executableDirectory))
            return executableDirectory;

        var current = Path.TrimEndingDirectorySeparator(executableDirectory.Trim());
        while (TagDirectoryPattern.IsMatch(Path.GetFileName(current)))
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent))
                break;
            // Do not walk a tag-named directory that sits directly under a
            // drive/filesystem root into that root: it is the install root.
            if (string.IsNullOrEmpty(Path.GetDirectoryName(parent)))
                break;
            current = parent;
        }

        return current;
    }

    /// <summary>
    /// Selects the download asset for a platform and build variant (r14 1.1).
    /// Non-Windows platforms keep the r11 default-build selection untouched
    /// (exact os/arch suffix, no accelerator token). Windows matches by
    /// os/arch plus the variant token ("-cpu-", "-cuda-", "-vulkan-"); when a
    /// release ships more than one CUDA build (e.g. 12.4 and 13.3) the lowest
    /// version is chosen for the broadest driver compatibility. Returns null
    /// when no asset matches, letting the caller fall back to Cpu.
    /// </summary>
    public static GitHubReleaseAsset? SelectDownloadAsset(
        IReadOnlyList<GitHubReleaseAsset> assets,
        LlamaPlatform? platform = null,
        LlamaRuntimeVariant variant = LlamaRuntimeVariant.Cpu)
    {
        var resolvedPlatform = platform ?? CurrentPlatform();
        if (resolvedPlatform is null)
            return null;

        var p = resolvedPlatform.Value;
        if (!IsWindows(p) || variant is LlamaRuntimeVariant.Auto or LlamaRuntimeVariant.Cpu)
        {
            var suffix = SuffixFor(p);
            return assets.FirstOrDefault(asset =>
                asset.Name.StartsWith("llama-", StringComparison.OrdinalIgnoreCase) &&
                asset.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }

        var arch = ArchToken(p);
        var token = variant switch
        {
            LlamaRuntimeVariant.Cuda => "-cuda-",
            LlamaRuntimeVariant.Vulkan => "-vulkan-",
            _ => "-cpu-"
        };

        var matches = assets.Where(asset =>
        {
            var name = asset.Name.ToLowerInvariant();
            return name.StartsWith("llama-", StringComparison.Ordinal)
                && name.EndsWith($"-{arch}.zip", StringComparison.Ordinal)
                && name.Contains("-bin-win-", StringComparison.Ordinal)
                && name.Contains(token, StringComparison.Ordinal);
        }).ToList();

        if (matches.Count == 0)
            return null;
        if (variant == LlamaRuntimeVariant.Cuda && matches.Count > 1)
            return matches.OrderBy(a => ParseCudaVersion(a.Name)).First();
        return matches[0];
    }

    /// <summary>
    /// Finds the CUDA runtime companion archive (cudart-...) that matches a
    /// chosen CUDA llama build (r14 1.2). CUDA builds link against the toolkit
    /// runtime shipped in a sibling asset of the same version and arch.
    /// </summary>
    public static GitHubReleaseAsset? SelectCudartAsset(IReadOnlyList<GitHubReleaseAsset> assets, string cudaAssetName)
    {
        var version = ParseCudaVersionString(cudaAssetName);
        if (version is null)
            return null;
        var arch = cudaAssetName.Contains("-arm64.", StringComparison.OrdinalIgnoreCase) ? "arm64" : "x64";
        return assets.FirstOrDefault(asset =>
        {
            var name = asset.Name.ToLowerInvariant();
            return name.StartsWith("cudart-", StringComparison.Ordinal)
                && name.Contains($"-cuda-{version}-", StringComparison.Ordinal)
                && name.EndsWith($"-{arch}.zip", StringComparison.Ordinal);
        });
    }

    private static (int Major, int Minor) ParseCudaVersion(string assetName)
    {
        var text = ParseCudaVersionString(assetName);
        if (text is null)
            return (int.MaxValue, int.MaxValue);
        var parts = text.Split('.');
        var major = int.TryParse(parts[0], out var m) ? m : int.MaxValue;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
        return (major, minor);
    }

    private static string? ParseCudaVersionString(string assetName)
    {
        var match = CudaVersionPattern.Match(assetName);
        return match.Success ? match.Groups["ver"].Value : null;
    }

    private static string AssetNameFor(LlamaPlatform platform, string tag) => $"llama-{tag}{SuffixFor(platform)}";

    internal static string PinnedSha256For(LlamaPlatform platform) => platform switch
    {
        LlamaPlatform.WinX64 => "936539730059f642374c42d07eab51d974da3d0e50fcf59fd3b88d20293502e2",
        LlamaPlatform.WinArm64 => "7137bb4638ccd31555167c2c1c3e1b79d49745f5e8bfa54d7312660cdf7b1d42",
        LlamaPlatform.LinuxX64 => "5a6370a6d27e508e5efa0a348be8f52a4fbbc7f3d2403c1a22cadc41210792ca",
        LlamaPlatform.LinuxArm64 => "3e6df9bd6d956bd48e9d85903097d6e2d75c37f0a217aa1c341449d426a06276",
        LlamaPlatform.MacX64 => "54be7b368bcafcb7cd969834f802ac257001ee26cc90db598aef727ab2151fb7",
        LlamaPlatform.MacArm64 => "a57a68565bd68509d860bc3cef5bfce91d3e3c47360c10132eb078c5eb4cd4db",
        _ => throw new ArgumentOutOfRangeException(nameof(platform))
    };

    private static bool IsWindows(LlamaPlatform platform) => platform is LlamaPlatform.WinX64 or LlamaPlatform.WinArm64;

    private static string ArchToken(LlamaPlatform platform) => platform switch
    {
        LlamaPlatform.WinX64 or LlamaPlatform.LinuxX64 or LlamaPlatform.MacX64 => "x64",
        _ => "arm64"
    };

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

    /// <summary>Human-readable variant label for install UI (r14 1.1).</summary>
    public static string VariantLabel(LlamaRuntimeVariant variant) => variant switch
    {
        LlamaRuntimeVariant.Cuda => "CUDA",
        LlamaRuntimeVariant.Vulkan => "Vulkan",
        LlamaRuntimeVariant.Cpu => "CPU",
        _ => "Auto"
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

    private static string DisplayNameFor(LlamaPlatform platform, LlamaRuntimeVariant variant)
    {
        if (!IsWindows(platform) || variant is LlamaRuntimeVariant.Auto or LlamaRuntimeVariant.Cpu)
            return DisplayNameFor(platform);
        var arch = ArchToken(platform);
        return $"Windows {arch} {VariantLabel(variant)}";
    }

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
public sealed record LlamaServerLatestDownload(
    string TagName,
    string AssetName,
    string Url,
    string DisplayName,
    string Sha256,
    string? CompanionAssetName = null,
    string? CompanionUrl = null,
    DateTimeOffset? PublishedAt = null,
    string? CompanionSha256 = null);
public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("assets")] List<GitHubReleaseAsset>? Assets,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt = null);
public sealed record GitHubReleaseAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
    [property: JsonPropertyName("digest")] string? Digest = null);
public sealed record LlamaReleaseSelection(
    GitHubRelease Release,
    GitHubReleaseAsset Asset,
    LlamaRuntimeVariant Variant,
    int BuildNumber);
