using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.ViewModels;
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
    public async Task A_real_scan_always_includes_an_app_update_check_under_the_system_category()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

        var doctor = new DoctorService(
            settings,
            new RuntimeProfileService(settings),
            new FakeVoiceProviderRegistry(settings),
            new FakeSecretStore(),
            new SqliteRagStore(settings),
            new FakeEmbeddingService(),
            new FakeSystemInfo(),
            new PythonHealthValidator(),
            new NoOpReranker());

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
        public Task<LlamaUpdateOutcome> InstallLlamaServerUpdateDetailedAsync(IProgress<string>? progress, CancellationToken ct = default) =>
            Task.FromResult(LlamaUpdateOutcome.Ok("/new/llama-server", "/new", []));
    }

    private static readonly DoctorCheck AppUpdateCheck = new(
        "app-update", "Hermaeus update check", DoctorCheckStatus.Warning,
        "Hermaeus v0.31.0-alpha is available (running 0.30.0-alpha)", "detail", "Open Releases", true, string.Empty, "System");

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
