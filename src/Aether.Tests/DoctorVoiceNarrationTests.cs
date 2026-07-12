using Aether.Core.Models;
using Aether.Core.Services;
using Aether.ViewModels;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

public sealed class DoctorVoiceNarrationTests
{
    [Fact]
    public async Task ScanAsync_narrates_a_single_critical_utterance_when_errors_are_found()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var report = new DoctorReport(
            [
                new DoctorCheck("check-a", "Check A", DoctorCheckStatus.Error, "Broken", "detail", "Fix", true, "diag", "System"),
                new DoctorCheck("check-b", "Check B", DoctorCheckStatus.Error, "Also broken", "detail", "Fix", true, "diag", "System")
            ],
            DateTime.UtcNow,
            "Doctor scan found 2 error(s) and 0 warning(s).");
        var voice = new FakeVoiceOrchestrator();
        var vm = new DoctorViewModel(new StaticDoctorService(report), new FakeToasts(), settings, voice);

        await vm.ScanCommand.ExecuteAsync(null);

        var utterance = Assert.Single(voice.Enqueued);
        Assert.Equal(VoiceChannel.Doctor, utterance.Channel);
        Assert.Equal(VoicePriority.Critical, utterance.Priority);
        Assert.Contains("2 critical issues", utterance.Text);
    }

    [Fact]
    public async Task ScanAsync_stays_silent_when_only_warnings_are_found()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var report = new DoctorReport(
            [new DoctorCheck("check-a", "Check A", DoctorCheckStatus.Warning, "Needs attention", "detail", "Fix", true, "diag", "System")],
            DateTime.UtcNow,
            "Doctor scan found 0 error(s) and 1 warning(s).");
        var voice = new FakeVoiceOrchestrator();
        var vm = new DoctorViewModel(new StaticDoctorService(report), new FakeToasts(), settings, voice);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Empty(voice.Enqueued);
    }

    private sealed class StaticDoctorService : IDoctorService
    {
        private readonly DoctorReport _report;
        public StaticDoctorService(DoctorReport report) => _report = report;

        public Task<DoctorReport> ScanAsync(CancellationToken ct = default) => Task.FromResult(_report);
        public Task<bool> InstallRerankerAssetsAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallRerankerAssetsAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallEmbeddingModelAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallEmbeddingModelAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallLlamaServerUpdateAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallLlamaServerUpdateAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallNativeKokoroAssetsAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallNativeKokoroAssetsAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
    }
}
