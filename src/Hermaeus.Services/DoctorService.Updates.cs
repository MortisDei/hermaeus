using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Hermaeus.Core.Models;

namespace Hermaeus.Services;

internal enum GitHubReleaseFetchStatus
{
    Success,
    NetworkUnavailable,
    RepositoryNotFound,
    RateLimited,
    GitHubRejected,
    ResponseInvalid
}

internal sealed record GitHubReleaseFetchResult(
    GitHubRelease? Release,
    GitHubReleaseFetchStatus Status,
    string Detail,
    int? HttpStatusCode = null);

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

        var fetch = await TryGetLatestHermaeusReleaseAsync(ct);
        if (fetch.Status != GitHubReleaseFetchStatus.Success || fetch.Release is null)
        {
            return BuildCheck(
                "app-update",
                "Hermaeus update check",
                fetch.Status is GitHubReleaseFetchStatus.RepositoryNotFound or GitHubReleaseFetchStatus.GitHubRejected
                    or GitHubReleaseFetchStatus.ResponseInvalid ? DoctorCheckStatus.Warning : DoctorCheckStatus.Info,
                $"Running {currentRaw}",
                fetch.Detail,
                "Open Releases",
                true,
                $"Checked {UpdateReleasesApiUrl}; result {fetch.Status}{(fetch.HttpStatusCode is int status ? $" (HTTP {status})" : string.Empty)}",
                "System");
        }

        var latest = fetch.Release;
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

    private Task<GitHubReleaseFetchResult> TryGetLatestHermaeusReleaseAsync(CancellationToken ct) =>
        GetCachedGitHubReleaseAsync("hermaeus-latest-release", FetchLatestHermaeusReleaseAsync, ct);

    private Task<GitHubReleaseFetchResult> FetchLatestHermaeusReleaseAsync(CancellationToken ct) =>
        FetchLatestHermaeusReleaseAsync(_updateHttp, ct);

    internal static async Task<GitHubReleaseFetchResult> FetchLatestHermaeusReleaseAsync(
        HttpClient http, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            using var request = new HttpRequestMessage(HttpMethod.Get, UpdateReleasesApiUrl);
            request.Headers.UserAgent.ParseAdd("Hermaeus-Doctor/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var statusCode = (int)response.StatusCode;
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new(null, GitHubReleaseFetchStatus.RepositoryNotFound,
                    $"GitHub returned 404 for the Hermaeus repository {UpdateRepoSlug}.", statusCode);
            if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests)
                return new(null, GitHubReleaseFetchStatus.RateLimited,
                    "GitHub rate-limited or rejected the anonymous releases request. Try again later.", statusCode);
            if (!response.IsSuccessStatusCode)
                return new(null, GitHubReleaseFetchStatus.GitHubRejected,
                    $"GitHub rejected the releases request with HTTP {statusCode}.", statusCode);

            var releases = await response.Content.ReadFromJsonAsync<List<GitHubRelease>>(timeout.Token);
            var latest = releases?.FirstOrDefault();
            return string.IsNullOrWhiteSpace(latest?.TagName)
                ? new(null, GitHubReleaseFetchStatus.ResponseInvalid,
                    "GitHub returned no release with a usable tag.", statusCode)
                : new(latest, GitHubReleaseFetchStatus.Success, "Release metadata loaded.", statusCode);
        }
        catch (JsonException)
        {
            return new(null, GitHubReleaseFetchStatus.ResponseInvalid,
                "GitHub returned release data that Hermaeus could not parse.");
        }
        catch (HttpRequestException ex)
        {
            return new(null, GitHubReleaseFetchStatus.NetworkUnavailable,
                $"GitHub releases were unavailable: {ex.Message}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(null, GitHubReleaseFetchStatus.NetworkUnavailable,
                "The GitHub releases request timed out.");
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
