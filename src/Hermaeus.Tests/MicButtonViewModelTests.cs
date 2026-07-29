using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>r24 doc 05 5.4: the shared dictation control's state machine. Uses fakes
/// for IAudioCapture/ISpeechRecognitionService so these run with no real microphone
/// or ONNX session, per the round's "no live microphone in the building" constraint.</summary>
public sealed class MicButtonViewModelTests
{
    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(condition(), "Condition was not met within the timeout.");
    }

    [Fact]
    public void State_is_Unavailable_when_Stt_is_disabled()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Stt.Enabled = false;
        var vm = new MicButtonViewModel(new FakeAudioCapture(true), new FakeSttRegistry(new FakeStt("hello")), settings);

        Assert.Equal(MicButtonState.Unavailable, vm.State);
    }

    [Fact]
    public void State_is_Unavailable_when_no_microphone_is_available()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Stt.Enabled = true;
        var vm = new MicButtonViewModel(new FakeAudioCapture(false), new FakeSttRegistry(new FakeStt("hello")), settings);

        Assert.Equal(MicButtonState.Unavailable, vm.State);
    }

    [Fact]
    public void State_is_Ready_when_enabled_with_a_device_and_a_provider()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Stt.Enabled = true;
        var vm = new MicButtonViewModel(new FakeAudioCapture(true), new FakeSttRegistry(new FakeStt("hello")), settings);

        Assert.Equal(MicButtonState.Ready, vm.State);
    }

    [Fact]
    public async Task Toggle_records_then_transcribes_then_fires_TranscriptReady_and_returns_to_Ready()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Stt.Enabled = true;
        var capture = new FakeAudioCapture(true);
        var vm = new MicButtonViewModel(capture, new FakeSttRegistry(new FakeStt("hello there")), settings);

        string? received = null;
        vm.TranscriptReady += t => received = t;

        await vm.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(MicButtonState.Recording, vm.State);

        await vm.ToggleCommand.ExecuteAsync(null);
        await WaitForAsync(() => vm.State == MicButtonState.Ready);

        Assert.Equal("hello there", received);
        Assert.True(capture.LastSession!.Disposed);
    }

    [Fact]
    public async Task A_low_confidence_transcript_never_fires_TranscriptReady()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Stt.Enabled = true;
        var vm = new MicButtonViewModel(new FakeAudioCapture(true), new FakeSttRegistry(new FakeStt(string.Empty, isLowConfidence: true)), settings);

        var fired = false;
        vm.TranscriptReady += _ => fired = true;

        await vm.ToggleCommand.ExecuteAsync(null);
        await vm.ToggleCommand.ExecuteAsync(null);
        await WaitForAsync(() => vm.State == MicButtonState.Ready);

        Assert.False(fired, "a low-confidence/empty transcript must never be handed to the host");
    }

    [Fact]
    public async Task Stopping_a_session_that_captured_nothing_never_calls_the_stt_provider()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Stt.Enabled = true;
        var stt = new FakeStt("should not be called");
        var vm = new MicButtonViewModel(new FakeAudioCapture(true, stopReturnsNull: true), new FakeSttRegistry(stt), settings);

        await vm.ToggleCommand.ExecuteAsync(null);
        await vm.ToggleCommand.ExecuteAsync(null);
        await WaitForAsync(() => vm.State == MicButtonState.Ready);

        Assert.Equal(0, stt.CallCount);
    }

    [Fact]
    public void TooltipText_reflects_each_state()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Stt.Enabled = false;
        var vm = new MicButtonViewModel(new FakeAudioCapture(true), new FakeSttRegistry(new FakeStt("x")), settings);
        Assert.Contains("off", vm.TooltipText, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeAudioCapture : IAudioCapture
    {
        private readonly bool _available;
        private readonly bool _stopReturnsNull;
        public FakeSession? LastSession { get; private set; }

        public FakeAudioCapture(bool available, bool stopReturnsNull = false)
        {
            _available = available;
            _stopReturnsNull = stopReturnsNull;
        }

        public bool IsAvailable => _available;
        public string? UnavailableReason => _available ? null : "no device";
        public IReadOnlyList<AudioInputDevice> EnumerateDevices() => _available ? [new AudioInputDevice("default", "Default")] : [];

        public Task<ICaptureSession> StartAsync(string? deviceId, CancellationToken ct = default)
        {
            LastSession = new FakeSession(_stopReturnsNull);
            return Task.FromResult<ICaptureSession>(LastSession);
        }
    }

    private sealed class FakeSession : ICaptureSession
    {
        private readonly bool _stopReturnsNull;
        public bool Disposed { get; private set; }
        public event Action<float>? PeakLevelChanged;

        public FakeSession(bool stopReturnsNull) => _stopReturnsNull = stopReturnsNull;

        public string? Stop()
        {
            PeakLevelChanged?.Invoke(0.5f);
            if (_stopReturnsNull) return null;

            var path = Path.GetTempFileName();
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            return path;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeSttRegistry : ISpeechRecognitionProviderRegistry
    {
        private readonly ISpeechRecognitionService _service;
        public FakeSttRegistry(ISpeechRecognitionService service) => _service = service;
        public SttProvider GetActiveProvider() => SttProvider.OnnxNative;
        public ISpeechRecognitionService GetActiveService() => _service;
        public Task SetActiveProviderAsync(SttProvider provider) => Task.CompletedTask;
    }

    private sealed class FakeStt : ISpeechRecognitionService
    {
        private readonly string _text;
        private readonly bool _isLowConfidence;
        public int CallCount { get; private set; }

        public FakeStt(string text, bool isLowConfidence = false)
        {
            _text = text;
            _isLowConfidence = isLowConfidence;
        }

        public string ProviderName => "fake";
        public bool IsAvailable => true;

        public Task<SpeechTranscript> TranscribeAsync(Stream wavPcm16Mono16k, SpeechTranscribeOptions options, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(new SpeechTranscript(_text, 500, "en", _isLowConfidence));
        }
    }
}
