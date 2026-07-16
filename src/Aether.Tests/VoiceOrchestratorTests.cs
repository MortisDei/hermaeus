using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

public sealed class VoiceOrchestratorTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not met within the timeout.");
    }

    [Fact]
    public async Task EnqueueAsync_never_overlaps_playback()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var provider = new RecordingVoiceProvider { DelayMs = 40 };
        using var orchestrator = new VoiceOrchestrator(settings, new SingleProviderRegistry(provider), new FakeToasts());

        await orchestrator.EnqueueAsync(new VoiceUtterance("first message text", VoiceChannel.Chat));
        await orchestrator.EnqueueAsync(new VoiceUtterance("second message text", VoiceChannel.Chat));

        await WaitUntilAsync(() => provider.Calls.Count >= 2, TimeSpan.FromSeconds(3));

        Assert.True(provider.Calls[0].End <= provider.Calls[1].Start,
            "second utterance should not start playing before the first finishes");
    }

    [Fact]
    public async Task Critical_utterance_preempts_playing_normal_and_plays_next()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var provider = new RecordingVoiceProvider { DelayMs = 250 };
        using var orchestrator = new VoiceOrchestrator(settings, new SingleProviderRegistry(provider), new FakeToasts());

        await orchestrator.EnqueueAsync(new VoiceUtterance("normal one text here", VoiceChannel.Chat, VoicePriority.Normal));
        await Task.Delay(60);
        await orchestrator.EnqueueAsync(new VoiceUtterance("normal two text here", VoiceChannel.Chat, VoicePriority.Normal));
        await orchestrator.EnqueueAsync(new VoiceUtterance("critical text here", VoiceChannel.Chat, VoicePriority.Critical));

        await WaitUntilAsync(() => provider.Calls.Count >= 2, TimeSpan.FromSeconds(3));

        Assert.DoesNotContain(provider.Calls, c => c.Text == "normal one text here");
        Assert.Equal("critical text here", provider.Calls[0].Text);
        Assert.Equal("normal two text here", provider.Calls[1].Text);
    }

    [Fact]
    public async Task Low_priority_utterance_is_dropped_once_queue_holds_three_items()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var provider = new RecordingVoiceProvider { DelayMs = 400 };
        using var orchestrator = new VoiceOrchestrator(settings, new SingleProviderRegistry(provider), new FakeToasts());

        await orchestrator.EnqueueAsync(new VoiceUtterance("first blocks the queue", VoiceChannel.Chat));
        await Task.Delay(30);
        await orchestrator.EnqueueAsync(new VoiceUtterance("q1", VoiceChannel.Chat));
        await orchestrator.EnqueueAsync(new VoiceUtterance("q2", VoiceChannel.Chat));
        await orchestrator.EnqueueAsync(new VoiceUtterance("q3", VoiceChannel.Chat));
        await orchestrator.EnqueueAsync(new VoiceUtterance("low priority dropped", VoiceChannel.Chat, VoicePriority.Low));

        await WaitUntilAsync(() => provider.Calls.Count >= 4, TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        Assert.DoesNotContain(provider.Calls, c => c.Text == "low priority dropped");
    }

    [Fact]
    public async Task Duplicate_dedupe_key_drops_the_second_queued_utterance()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var provider = new RecordingVoiceProvider { DelayMs = 250 };
        using var orchestrator = new VoiceOrchestrator(settings, new SingleProviderRegistry(provider), new FakeToasts());

        await orchestrator.EnqueueAsync(new VoiceUtterance("blocks queue while playing", VoiceChannel.Chat));
        await Task.Delay(30);
        await orchestrator.EnqueueAsync(new VoiceUtterance("first version of message", VoiceChannel.Chat, DedupeKey: "dupe"));
        await orchestrator.EnqueueAsync(new VoiceUtterance("second version of message", VoiceChannel.Chat, DedupeKey: "dupe"));

        await WaitUntilAsync(() => provider.Calls.Count >= 2, TimeSpan.FromSeconds(3));
        await Task.Delay(50);

        Assert.Contains(provider.Calls, c => c.Text == "first version of message");
        Assert.DoesNotContain(provider.Calls, c => c.Text == "second version of message");
    }

    [Fact]
    public async Task Muted_orchestrator_never_calls_the_provider()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var provider = new RecordingVoiceProvider();
        using var orchestrator = new VoiceOrchestrator(settings, new SingleProviderRegistry(provider), new FakeToasts()) { IsMuted = true };

        await orchestrator.EnqueueAsync(new VoiceUtterance("should never be spoken", VoiceChannel.Chat));
        await Task.Delay(150);

        Assert.Empty(provider.Calls);
    }

    [Fact]
    public async Task Channel_disabled_by_default_never_calls_the_provider()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var provider = new RecordingVoiceProvider();
        using var orchestrator = new VoiceOrchestrator(settings, new SingleProviderRegistry(provider), new FakeToasts());

        await orchestrator.EnqueueAsync(new VoiceUtterance("agent channel is off by default", VoiceChannel.Agent));
        await Task.Delay(150);

        Assert.Empty(provider.Calls);
    }

    [Fact]
    public async Task Enqueue_resolves_voice_from_the_channels_configured_profile()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Tts.Speaker = "default-speaker";
        settings.Settings.Tts.Profiles.Add(new VoiceProfile { Name = "narrator", VoiceId = "narrator-voice" });
        settings.Settings.Tts.Channels["Agent"] = new VoiceChannelConfig { Enabled = true, ProfileName = "narrator" };
        var provider = new RecordingVoiceProvider();
        using var orchestrator = new VoiceOrchestrator(settings, new SingleProviderRegistry(provider), new FakeToasts());

        await orchestrator.EnqueueAsync(new VoiceUtterance("narrated line of text", VoiceChannel.Agent));
        await WaitUntilAsync(() => provider.Calls.Count >= 1, TimeSpan.FromSeconds(2));

        Assert.Equal("narrator-voice", provider.Calls[0].Voice);
    }

    [Fact]
    public async Task VoiceOverride_takes_precedence_over_the_channel_profile()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var provider = new RecordingVoiceProvider();
        using var orchestrator = new VoiceOrchestrator(settings, new SingleProviderRegistry(provider), new FakeToasts());

        await orchestrator.EnqueueAsync(new VoiceUtterance("explicit voice line", VoiceChannel.Chat, VoiceOverride: "explicit-voice"));
        await WaitUntilAsync(() => provider.Calls.Count >= 1, TimeSpan.FromSeconds(2));

        Assert.Equal("explicit-voice", provider.Calls[0].Voice);
    }

    [Fact]
    public async Task StopChannel_cancels_current_playback_and_clears_its_queue()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var provider = new RecordingVoiceProvider { DelayMs = 300 };
        using var orchestrator = new VoiceOrchestrator(settings, new SingleProviderRegistry(provider), new FakeToasts());

        await orchestrator.EnqueueAsync(new VoiceUtterance("first chat message", VoiceChannel.Chat));
        await Task.Delay(30);
        await orchestrator.EnqueueAsync(new VoiceUtterance("second chat message", VoiceChannel.Chat));
        orchestrator.StopChannel(VoiceChannel.Chat);

        await Task.Delay(300);

        Assert.Empty(provider.Calls);
    }

    /// <summary>r11 4.6: _toastedProviderFailures never reset, so after one failure toast for a provider, a later distinct failure stayed silent for the app's lifetime. A success in between must reset the key so the next failure toasts again.</summary>
    [Fact]
    public async Task Failure_toasts_once_per_episode_reset_by_an_intervening_success()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var provider = new RecordingVoiceProvider { ShouldFail = true };
        var toasts = new FakeToasts();
        var toastCount = 0;
        toasts.ToastRaised += _ => Interlocked.Increment(ref toastCount);
        using var orchestrator = new VoiceOrchestrator(settings, new SingleProviderRegistry(provider), toasts);

        await orchestrator.EnqueueAsync(new VoiceUtterance("first failure", VoiceChannel.Chat));
        await WaitUntilAsync(() => provider.Calls.Count >= 1, TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => toastCount >= 1, TimeSpan.FromSeconds(2));

        await orchestrator.EnqueueAsync(new VoiceUtterance("second consecutive failure", VoiceChannel.Chat));
        await WaitUntilAsync(() => provider.Calls.Count >= 2, TimeSpan.FromSeconds(2));
        await Task.Delay(100);
        Assert.Equal(1, toastCount);

        provider.ShouldFail = false;
        await orchestrator.EnqueueAsync(new VoiceUtterance("recovers", VoiceChannel.Chat));
        await WaitUntilAsync(() => provider.Calls.Count >= 3, TimeSpan.FromSeconds(2));
        await Task.Delay(100);
        Assert.Equal(1, toastCount);

        provider.ShouldFail = true;
        await orchestrator.EnqueueAsync(new VoiceUtterance("new failure episode", VoiceChannel.Chat));
        await WaitUntilAsync(() => toastCount >= 2, TimeSpan.FromSeconds(2));
        Assert.Equal(2, toastCount);
    }

    private sealed class RecordingVoiceProvider : IVoiceProvider
    {
        public VoiceProvider Id => VoiceProvider.Kokoro;
        public string DisplayName => "Recording";
        public VoiceCapability Capabilities => VoiceCapability.TextToSpeech | VoiceCapability.Local;
        public (int Major, int Minor)? RequiredPythonVersion => null;
        public bool IsInstalled => true;
        public int DelayMs { get; set; } = 10;
        public bool ShouldFail { get; set; }
        public List<(DateTime Start, DateTime End, string Voice, string Text)> Calls { get; } = [];

        public VoiceProviderDetection Detect() => new(true, "ok", "ok");
        public VoiceInstallPlan InstallPlan() => new("none", [], "low");
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<VoiceHealth> HealthCheckAsync(CancellationToken ct = default) =>
            Task.FromResult(new VoiceHealth(VoiceHealthStatus.Healthy, "ok", "ok"));
        public Task<IReadOnlyList<VoiceDefinition>> ListVoicesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VoiceDefinition>>([]);

        public async Task<VoiceSynthesisResult> GenerateSpeechAsync(VoiceSynthesisRequest request, CancellationToken ct = default)
        {
            var start = DateTime.UtcNow;
            await Task.Delay(DelayMs, ct);
            var end = DateTime.UtcNow;
            Calls.Add((start, end, request.Voice ?? string.Empty, request.Text));
            return ShouldFail
                ? new VoiceSynthesisResult(false, "simulated failure")
                : new VoiceSynthesisResult(true, "ok");
        }
    }

    private sealed class SingleProviderRegistry : IVoiceProviderRegistry
    {
        private readonly IVoiceProvider _provider;
        public SingleProviderRegistry(IVoiceProvider provider) => _provider = provider;
        public IReadOnlyList<VoiceProviderInfo> GetAvailableProviders() => [];
        public VoiceProvider GetActiveProvider() => _provider.Id;
        public IVoiceProvider GetActiveVoiceProvider() => _provider;
        public IVoiceProvider GetVoiceProvider(VoiceProvider provider) => _provider;
        public Task SetActiveProviderAsync(VoiceProvider provider) => Task.CompletedTask;
        public VoiceProviderConfig? GetProviderConfig(VoiceProvider provider) => null;
        public Task SetProviderConfigAsync(VoiceProvider provider, VoiceProviderConfig config) => Task.CompletedTask;
        public ITtsService GetActiveTtsService() => throw new NotSupportedException("Orchestrator tests only exercise IVoiceProvider.GenerateSpeechAsync.");
    }
}
