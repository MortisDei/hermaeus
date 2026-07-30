using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// The Services > Voice card's report of whether the local speech model is
/// installed, and its handoff to Doctor for installing it.
///
/// The card previously had no notion of installed state at all, so "Install
/// model" was unconditional and was still sitting there after a successful
/// install.
/// </summary>
public sealed class SttModelInstallStatusTests
{
    private sealed class FakeCapture : IAudioCapture
    {
        public bool IsAvailable { get; set; } = true;
        public string? UnavailableReason { get; set; }
        public IReadOnlyList<AudioInputDevice> EnumerateDevices() => [new("dev", "Fake")];
        public Task<ICaptureSession> StartAsync(string? deviceId = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSttRegistry : ISpeechRecognitionProviderRegistry
    {
        public bool ModelInstalled { get; set; } = true;
        public SttProvider GetActiveProvider() => SttProvider.OnnxNative;
        public ISpeechRecognitionService GetActiveService() => new FakeSttService { IsAvailable = ModelInstalled };
        public Task SetActiveProviderAsync(SttProvider provider) => Task.CompletedTask;
    }

    private sealed class FakeSttService : ISpeechRecognitionService
    {
        public string ProviderName => "Fake";
        public bool IsAvailable { get; set; }
        public Task<SpeechTranscript> TranscribeAsync(
            Stream wavPcm16Mono16k, SpeechTranscribeOptions options, CancellationToken ct = default) =>
            Task.FromResult(new SpeechTranscript(string.Empty, 0, "en", true, "no"));
    }


    /// <summary>
    /// The Services card had no notion of whether the model was present, so the
    /// install button was unconditional and was still sitting there after a
    /// successful install.
    /// </summary>
    [Fact]
    public void The_services_card_reports_whether_the_speech_model_is_installed()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var registry = new FakeSttRegistry { ModelInstalled = false };
        var vm = new SttSettingsViewModel(settings, registry, new FakeToasts(), new FakeCapture());

        Assert.False(vm.IsModelInstalled);
        Assert.Contains("not installed", vm.ModelStatus, StringComparison.OrdinalIgnoreCase);

        registry.ModelInstalled = true;
        vm.RefreshModelStatus();

        Assert.True(vm.IsModelInstalled);
        Assert.Contains("installed", vm.ModelStatus, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not installed", vm.ModelStatus, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Installing is a Doctor action; Services hands off rather than carrying a
    /// second entry point that reports nothing.</summary>
    [Fact]
    public void The_services_card_hands_off_to_doctor_to_install()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = new SttSettingsViewModel(
            settings, new FakeSttRegistry { ModelInstalled = false }, new FakeToasts(), new FakeCapture());

        string? navigated = null;
        vm.RequestNavigate = panel => navigated = panel;
        vm.OpenDoctorToInstallCommand.Execute(null);

        Assert.Equal("doctor", navigated);
    }
}
