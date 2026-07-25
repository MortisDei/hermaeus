using System.Net.Http.Json;
using System.Reflection;
using Hermaeus.Core.Models;

namespace Hermaeus.Services;

public sealed partial class DoctorService
{
    private const string UpdateRepoSlug = "MortisDei/hermaeus";
    private const string UpdateReleasesApiUrl = $"https://api.github.com/repos/{UpdateRepoSlug}/releases?per_page=1";
    private const string UpdateReleasesPageUrl = $"https://github.com/{UpdateRepoSlug}/releases/latest";

    /// <summary>
    /// Compares the running app version against the newest published GitHub
    /// release. Uses the release list endpoint (not `/releases/latest`)
    /// because every Hermaeus release is currently marked prerelease and the
    /// `/latest` endpoint deliberately excludes those. Never downloads or
    /// applies anything; the fix action only opens the releases page so the
    /// user can install it themselves (r24, "check for updates").
    /// </summary>
    private async Task<DoctorCheck> CheckAppUpdateAsync(CancellationToken ct)
    {
        var currentRaw = GetCurrentAppVersion();
        if (!TryParseCoreVersion(currentRaw, out var current) || current is null)
        {
            return BuildCheck(
                "app-update",
                "Hermaeus update check",
                DoctorCheckStatus.Info,
                "Update check skipped",
                "Could not determine the running app version.",
                "Open Releases",
                true,
                $"Raw version string: {currentRaw}",
                "System");
        }

        var latest = await TryGetLatestHermaeusReleaseAsync(ct);
        if (latest is null)
        {
            return BuildCheck(
                "app-update",
                "Hermaeus update check",
                DoctorCheckStatus.Info,
                $"Running {currentRaw}",
                "Could not reach GitHub releases to check for an update. This is expected while the repository is private; it will work once the repository is public.",
                "Open Releases",
                true,
                $"Checked {UpdateReleasesApiUrl}",
                "System");
        }

        if (!TryParseCoreVersion(latest.TagName, out var latestVersion) || latestVersion is null)
        {
            return BuildCheck(
                "app-update",
                "Hermaeus update check",
                DoctorCheckStatus.Info,
                $"Running {currentRaw}",
                $"Latest GitHub release tag \"{latest.TagName}\" could not be parsed as a version.",
                "Open Releases",
                true,
                $"Tag: {latest.TagName}",
                "System");
        }

        if (latestVersion > current)
        {
            return BuildCheck(
                "app-update",
                "Hermaeus update check",
                DoctorCheckStatus.Warning,
                $"Hermaeus {latest.TagName} is available (running {currentRaw})",
                "Download and install the new version yourself; Hermaeus never downloads or applies updates automatically.",
                "Open Releases",
                true,
                $"Latest tag: {latest.TagName}, published {latest.PublishedAt:u}",
                "System");
        }

        return BuildCheck(
            "app-update",
            "Hermaeus update check",
            DoctorCheckStatus.Ready,
            $"Running {currentRaw}, up to date",
            $"Latest published release is {latest.TagName}.",
            "Open Releases",
            true,
            $"Latest tag: {latest.TagName}",
            "System");
    }

    private static string GetCurrentAppVersion() =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(DoctorService).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    private Task<GitHubRelease?> TryGetLatestHermaeusReleaseAsync(CancellationToken ct) =>
        GetCachedGitHubReleaseAsync("hermaeus-latest-release", FetchLatestHermaeusReleaseAsync, ct);

    private static async Task<GitHubRelease?> FetchLatestHermaeusReleaseAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Hermaeus-Doctor/1.0");
            var releases = await http.GetFromJsonAsync<List<GitHubRelease>>(UpdateReleasesApiUrl, timeout.Token);
            var latest = releases?.FirstOrDefault();
            return string.IsNullOrWhiteSpace(latest?.TagName) ? null : latest;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a "v0.30.0-alpha" release tag or a "0.30.0-alpha" informational
    /// version string down to its numeric "0.30.0" core so the two can be
    /// compared with <see cref="Version"/>. Public and static so it can be
    /// unit tested directly.
    /// </summary>
    public static bool TryParseCoreVersion(string? raw, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[1..];

        var dashIndex = trimmed.IndexOf('-');
        var core = dashIndex >= 0 ? trimmed[..dashIndex] : trimmed;

        return Version.TryParse(core, out version);
    }
}
