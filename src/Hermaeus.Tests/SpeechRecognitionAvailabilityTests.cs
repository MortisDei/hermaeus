using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// Whether a mic button is usable, and whether it says why when it is not.
///
/// Both halves were broken on the r25 build: availability was computed once when
/// the view model was constructed at app startup and never revisited, so enabling
/// speech recognition mid-session left every mic button in the app disabled until
/// the next restart. <c>Refresh</c>'s own doc comment said "call after
/// Settings > Voice changes"; nothing ever called it.
/// </summary>
public sealed class SpeechRecognitionAvailabilityTests
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
    /// The bug the owner hit: speech recognition is off by default, so every mic
    /// button in the app was constructed Unavailable at startup and stayed that
    /// way for the whole session after enabling it. `Refresh`'s own doc comment
    /// said "call after Settings > Voice changes"; nothing ever called it.
    /// </summary>
    [Fact]
    public async Task Enabling_speech_recognition_makes_the_mic_button_usable_without_a_restart()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Stt.Enabled = false;

        using var mic = new MicButtonViewModel(new FakeCapture(), new FakeSttRegistry(), settings);
        Assert.Equal(MicButtonState.Unavailable, mic.State);

        settings.Settings.Stt.Enabled = true;
        await settings.SaveAsync();

        await WaitForAsync(() => mic.State == MicButtonState.Ready, "the mic button becoming usable after enabling STT");
    }

    /// <summary>
    /// An enabled mic with no model is a button that records and then fails. It
    /// reports unavailable, and the tooltip says which of the two things is
    /// missing rather than a generic "no microphone".
    /// </summary>
    [Fact]
    public void A_missing_speech_model_disables_the_mic_and_says_so()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Stt.Enabled = true;

        using var mic = new MicButtonViewModel(
            new FakeCapture(), new FakeSttRegistry { ModelInstalled = false }, settings);

        Assert.Equal(MicButtonState.Unavailable, mic.State);
        Assert.Contains("not installed", mic.TooltipText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Doctor", mic.TooltipText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_microphone_still_reports_the_microphone_reason()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Stt.Enabled = true;

        using var mic = new MicButtonViewModel(
            new FakeCapture { IsAvailable = false, UnavailableReason = "no input device found" },
            new FakeSttRegistry(),
            settings);

        Assert.Equal(MicButtonState.Unavailable, mic.State);
        Assert.Contains("no input device found", mic.TooltipText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_ready_mic_reports_ready()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Stt.Enabled = true;

        using var mic = new MicButtonViewModel(new FakeCapture(), new FakeSttRegistry(), settings);

        Assert.Equal(MicButtonState.Ready, mic.State);
        Assert.Equal("Start dictation", mic.TooltipText);
    }
}
