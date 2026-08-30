using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using System.Net;
using System.Text;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r24: a Doctor check that compares the running app version against the
/// newest GitHub release and, if newer, opens the releases page so the user
/// can download and install it themselves. Never downloads or applies
/// anything automatically.
/// </summary>
public sealed class DoctorAppUpdateTests
{
    [Theory]
    [InlineData("v0.30.0-alpha", "0.30.0")]
    [InlineData("0.30.0-alpha", "0.30.0")]
    [InlineData("V0.9.5", "0.9.5")]
    [InlineData("0.30.0", "0.30.0")]
    public void Valid_version_strings_parse_to_their_numeric_core(string raw, string expected)
    {
        Assert.True(DoctorService.TryParseCoreVersion(raw, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("vunknown")]
    public void Invalid_version_strings_fail_to_parse(string? raw)
    {
        Assert.False(DoctorService.TryParseCoreVersion(raw, out var version));
        Assert.Null(version);
    }

    [Fact]
    public void A_newer_release_tag_compares_greater_than_the_running_version()
    {
        Assert.True(DoctorService.TryParseCoreVersion("v0.31.0-alpha", out var latest));
        Assert.True(DoctorService.TryParseCoreVersion("0.30.0-alpha", out var current));
        Assert.True(latest > current);
    }

    [Fact]
    public async Task App_update_request_uses_the_canonical_repository_and_headers()
    {
        HttpRequestMessage? request = null;
        using var http = new HttpClient(new RecordingHandler(message =>
        {
            request = message;
            return Json("[{\"tag_name\":\"v0.31.0-beta\",\"published_at\":\"2026-08-29T00:00:00Z\"}]");
        }));

        var result = await DoctorService.FetchLatestHermaeusReleaseAsync(http, CancellationToken.None);

        Assert.Equal(GitHubReleaseFetchStatus.Success, result.Status);
        Assert.Equal("v0.31.0-beta", result.Release!.TagName);
        Assert.Equal("https://api.github.com/repos/MortisDei/hermaeus/releases?per_page=1", request!.RequestUri!.ToString());
        Assert.Contains(request.Headers.UserAgent, value => value.Product?.Name == "Hermaeus-Doctor");
        Assert.Contains(request.Headers.Accept, value => value.MediaType == "application/vnd.github+json");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "RepositoryNotFound")]
    [InlineData(HttpStatusCode.Forbidden, "RateLimited")]
    [InlineData(HttpStatusCode.TooManyRequests, "RateLimited")]
    [InlineData(HttpStatusCode.BadGateway, "GitHubRejected")]
    public async Task App_update_request_classifies_github_http_failures(HttpStatusCode status, string expected)
    {
        using var http = new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(status)));

        var result = await DoctorService.FetchLatestHermaeusReleaseAsync(http, CancellationToken.None);

        Assert.Equal(Enum.Parse<GitHubReleaseFetchStatus>(expected), result.Status);
        Assert.Equal((int)status, result.HttpStatusCode);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task App_update_request_classifies_invalid_payload()
    {
        using var http = new HttpClient(new RecordingHandler(_ => Json("{\"not\":\"a release list\"}")));

        var result = await DoctorService.FetchLatestHermaeusReleaseAsync(http, CancellationToken.None);

        Assert.Equal(GitHubReleaseFetchStatus.ResponseInvalid, result.Status);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task App_update_request_classifies_timeout_and_network_failure()
    {
        using var timeoutHttp = new HttpClient(new RecordingHandler(_ => throw new TaskCanceledException("timed out")));
        var timeout = await DoctorService.FetchLatestHermaeusReleaseAsync(timeoutHttp, CancellationToken.None);
        Assert.Equal(GitHubReleaseFetchStatus.NetworkUnavailable, timeout.Status);
        Assert.Contains("timed out", timeout.Detail, StringComparison.OrdinalIgnoreCase);

        using var networkHttp = new HttpClient(new RecordingHandler(_ => throw new HttpRequestException("offline")));
        var network = await DoctorService.FetchLatestHermaeusReleaseAsync(networkHttp, CancellationToken.None);
        Assert.Equal(GitHubReleaseFetchStatus.NetworkUnavailable, network.Status);
        Assert.Contains("offline", network.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_real_scan_always_includes_an_app_update_check_under_the_system_category()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        using var http = new HttpClient(new RecordingHandler(_ => Json("[]")));

        var doctor = new DoctorService(
            settings,
            new RuntimeProfileService(settings),
            new FakeVoiceProviderRegistry(settings),
            new FakeSecretStore(),
            new SqliteRagStore(settings),
            new FakeEmbeddingService(),
            new FakeSystemInfo(),
            new PythonHealthValidator(),
            new NoOpReranker(),
            updateHttp: http);

        var report = await doctor.ScanAsync();

        var check = Assert.Single(report.Checks, c => c.Key == "app-update");
        Assert.Equal("System", check.Category);
        Assert.True(check.CanFix, "the check should always offer to open the releases page");
    }

    private sealed class StubDoctorService : IDoctorService
    {
        public Task<DoctorReport> ScanAsync(CancellationToken ct = default) =>
            Task.FromResult(new DoctorReport([], DateTime.UtcNow, "ok"));
        public Task<bool> InstallRerankerAssetsAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallRerankerAssetsAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallEmbeddingModelAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallEmbeddingModelAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallLlamaServerUpdateAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallLlamaServerUpdateAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallNativeKokoroAssetsAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallNativeKokoroAssetsAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallSpeechRecognitionAssetsAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<LlamaUpdateOutcome> InstallLlamaServerUpdateDetailedAsync(IProgress<string>? progress, CancellationToken ct = default) =>
            Task.FromResult(LlamaUpdateOutcome.Ok("/new/llama-server", "/new", []));
    }

    private static readonly DoctorCheck AppUpdateCheck = new(
        "app-update", "Hermaeus update check", DoctorCheckStatus.Warning,
        "Hermaeus v0.31.0-alpha is available (running 0.30.0-alpha)", "detail", "Open Releases", true, string.Empty, "System");

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    [Fact]
    public async Task Running_the_fix_opens_the_releases_page_and_does_not_navigate()
    {
        using var temp = new TempDir();
        var vm = new DoctorViewModel(new StubDoctorService(), new FakeToasts(), NewSettings(temp));

        string? openedUrl = null;
        var navigated = false;
        vm.RequestOpenUrl = url => openedUrl = url;
        vm.RequestNavigate = _ => navigated = true;

        await vm.RunFixCommand.ExecuteAsync(AppUpdateCheck);

        Assert.Equal("https://github.com/MortisDei/hermaeus/releases/latest", openedUrl);
        Assert.False(navigated, "the app-update fix action must not also trigger in-app navigation");
    }

    [Fact]
    public async Task Running_the_fix_without_a_configured_open_url_handler_shows_a_toast_instead_of_throwing()
    {
        using var temp = new TempDir();
        var toasts = new FakeToasts();
        var vm = new DoctorViewModel(new StubDoctorService(), toasts, NewSettings(temp));

        await vm.RunFixCommand.ExecuteAsync(AppUpdateCheck);

        Assert.NotNull(toasts.LastShown);
    }
}
