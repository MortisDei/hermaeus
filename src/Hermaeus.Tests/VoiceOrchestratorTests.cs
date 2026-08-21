using System.Threading.Channels;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class VoiceOrchestratorTests
{
    [Fact]
    public async Task EnqueueAsync_never_overlaps_playback()
    {
        using var temp = new TempDir();
        var provider = new ControlledVoiceProvider();
        using var orchestrator = CreateOrchestrator(temp, provider);

        await orchestrator.EnqueueAsync(new VoiceUtterance("first message text", VoiceChannel.Chat));
        await orchestrator.EnqueueAsync(new VoiceUtterance("second message text", VoiceChannel.Chat));

        var first = await provider.NextStartedAsync();
        first.Complete();
        var second = await provider.NextStartedAsync();
        second.Complete();
        await provider.WaitForCompletedAsync(2);

        Assert.True(provider.Completed[0].CompletedAt <= provider.Completed[1].StartedAt);
    }

    [Fact]
    public async Task Critical_utterance_preempts_playing_normal_and_plays_next()
    {
        using var temp = new TempDir();
        var provider = new ControlledVoiceProvider();
        using var orchestrator = CreateOrchestrator(temp, provider);

        await orchestrator.EnqueueAsync(new VoiceUtterance("normal one text here", VoiceChannel.Chat));
        var first = await provider.NextStartedAsync();
        await orchestrator.EnqueueAsync(new VoiceUtterance("normal two text here", VoiceChannel.Chat));
        await orchestrator.EnqueueAsync(new VoiceUtterance("critical text here", VoiceChannel.Chat, VoicePriority.Critical));

        var critical = await provider.NextStartedAsync();
        Assert.Equal("critical text here", critical.Request.Text);
        critical.Complete();
        var normal = await provider.NextStartedAsync();
        Assert.Equal("normal two text here", normal.Request.Text);
        normal.Complete();
        await provider.WaitForCompletedAsync(2);

        Assert.Equal("normal one text here", first.Request.Text);
        Assert.DoesNotContain(provider.Completed, call => call.Request.Text == "normal one text here");
    }

    [Fact]
    public async Task Low_priority_utterance_is_dropped_once_queue_holds_three_items()
    {
        using var temp = new TempDir();
        var provider = new ControlledVoiceProvider();
        using var orchestrator = CreateOrchestrator(temp, provider);

        await orchestrator.EnqueueAsync(new VoiceUtterance("first blocks the queue", VoiceChannel.Chat));
        var first = await provider.NextStartedAsync();
        await orchestrator.EnqueueAsync(new VoiceUtterance("q1", VoiceChannel.Chat));
        await orchestrator.EnqueueAsync(new VoiceUtterance("q2", VoiceChannel.Chat));
        await orchestrator.EnqueueAsync(new VoiceUtterance("q3", VoiceChannel.Chat));
        await orchestrator.EnqueueAsync(new VoiceUtterance("low priority dropped", VoiceChannel.Chat, VoicePriority.Low));

        first.Complete();
        for (var expected = 1; expected <= 3; expected++)
        {
            var queued = await provider.NextStartedAsync();
            Assert.Equal($"q{expected}", queued.Request.Text);
            queued.Complete();
        }
        await provider.WaitForCompletedAsync(4);

        Assert.DoesNotContain(provider.Completed, call => call.Request.Text == "low priority dropped");
    }

    [Fact]
    public async Task Duplicate_dedupe_key_drops_the_second_queued_utterance()
    {
        using var temp = new TempDir();
        var provider = new ControlledVoiceProvider();
        using var orchestrator = CreateOrchestrator(temp, provider);

        await orchestrator.EnqueueAsync(new VoiceUtterance("blocks queue while playing", VoiceChannel.Chat));
        var first = await provider.NextStartedAsync();
        await orchestrator.EnqueueAsync(new VoiceUtterance("first version of message", VoiceChannel.Chat, DedupeKey: "dupe"));
        await orchestrator.EnqueueAsync(new VoiceUtterance("second version of message", VoiceChannel.Chat, DedupeKey: "dupe"));

        first.Complete();
        var queued = await provider.NextStartedAsync();
        Assert.Equal("first version of message", queued.Request.Text);
        queued.Complete();
        await provider.WaitForCompletedAsync(2);

        Assert.DoesNotContain(provider.Completed, call => call.Request.Text == "second version of message");
    }

    [Fact]
    public async Task Muted_orchestrator_never_calls_the_provider()
    {
        using var temp = new TempDir();
        var provider = new ControlledVoiceProvider();
        using var orchestrator = CreateOrchestrator(temp, provider);
        orchestrator.IsMuted = true;

        await orchestrator.EnqueueAsync(new VoiceUtterance("should never be spoken", VoiceChannel.Chat));

        Assert.Empty(provider.Started);
    }

    [Fact]
    public async Task Channel_disabled_by_default_never_calls_the_provider()
    {
        using var temp = new TempDir();
        var provider = new ControlledVoiceProvider();
        using var orchestrator = CreateOrchestrator(temp, provider);

        await orchestrator.EnqueueAsync(new VoiceUtterance("agent channel is off by default", VoiceChannel.Agent));

        Assert.Empty(provider.Started);
    }

    [Fact]
    public async Task Enqueue_resolves_voice_from_the_channels_configured_profile()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Tts.Speaker = "default-speaker";
        settings.Settings.Tts.Profiles.Add(new VoiceProfile { Name = "narrator", VoiceId = "narrator-voice" });
        settings.Settings.Tts.Channels["Agent"] = new VoiceChannelConfig { Enabled = true, ProfileName = "narrator" };
        var provider = new ControlledVoiceProvider();
        using var orchestrator = new VoiceOrchestrator(settings, new SingleProviderRegistry(provider), new FakeToasts());

        await orchestrator.EnqueueAsync(new VoiceUtterance("narrated line of text", VoiceChannel.Agent));
        var call = await provider.NextStartedAsync();
        call.Complete();

        Assert.Equal("narrator-voice", call.Request.Voice);
    }

    [Fact]
    public async Task VoiceOverride_takes_precedence_over_the_channel_profile()
    {
        using var temp = new TempDir();
        var provider = new ControlledVoiceProvider();
        using var orchestrator = CreateOrchestrator(temp, provider);

        await orchestrator.EnqueueAsync(new VoiceUtterance("explicit voice line", VoiceChannel.Chat, VoiceOverride: "explicit-voice"));
        var call = await provider.NextStartedAsync();
        call.Complete();

        Assert.Equal("explicit-voice", call.Request.Voice);
    }

    [Fact]
    public async Task StopChannel_cancels_current_playback_and_clears_its_queue()
    {
        using var temp = new TempDir();
        var provider = new ControlledVoiceProvider();
        using var orchestrator = CreateOrchestrator(temp, provider);
        var completed = CompletionSignal(orchestrator);

        await orchestrator.EnqueueAsync(new VoiceUtterance("first chat message", VoiceChannel.Chat));
        await provider.NextStartedAsync();
        await orchestrator.EnqueueAsync(new VoiceUtterance("second chat message", VoiceChannel.Chat));
        orchestrator.StopChannel(VoiceChannel.Chat);

        Assert.Equal(VoiceChannel.Chat, await completed.Task);
        Assert.Empty(provider.Completed);
        Assert.Single(provider.Started);
    }

    [Fact]
    public async Task StopChannel_fires_UtteranceCompleted_and_leaves_IsSpeaking_false()
    {
        using var temp = new TempDir();
        var provider = new ControlledVoiceProvider();
        using var orchestrator = CreateOrchestrator(temp, provider);
        var completed = CompletionSignal(orchestrator);

        await orchestrator.EnqueueAsync(new VoiceUtterance("first chat message", VoiceChannel.Chat));
        await provider.NextStartedAsync();
        Assert.True(orchestrator.IsSpeaking);
        await orchestrator.EnqueueAsync(new VoiceUtterance("second chat message", VoiceChannel.Chat));
        orchestrator.StopChannel(VoiceChannel.Chat);

        Assert.Equal(VoiceChannel.Chat, await completed.Task);
        Assert.False(orchestrator.IsSpeaking);
        Assert.Empty(provider.Completed);
    }

    [Fact]
    public async Task IsSpeaking_is_true_while_playing_and_false_once_finished()
    {
        using var temp = new TempDir();
        var provider = new ControlledVoiceProvider();
        using var orchestrator = CreateOrchestrator(temp, provider);
        var completed = CompletionSignal(orchestrator);

        Assert.False(orchestrator.IsSpeaking);
        await orchestrator.EnqueueAsync(new VoiceUtterance("plays briefly", VoiceChannel.Chat));
        var call = await provider.NextStartedAsync();
        Assert.True(orchestrator.IsSpeaking);
        call.Complete();

        await completed.Task;
        Assert.False(orchestrator.IsSpeaking);
    }

    [Fact]
    public async Task Failure_toasts_once_per_episode_reset_by_an_intervening_success()
    {
        using var temp = new TempDir();
        var provider = new ControlledVoiceProvider { ShouldFail = true };
        var toasts = new FakeToasts();
        var toastCount = 0;
        var secondToast = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        toasts.ToastRaised += _ =>
        {
            if (Interlocked.Increment(ref toastCount) == 2)
                secondToast.TrySetResult();
        };
        using var orchestrator = new VoiceOrchestrator(NewSettings(temp), new SingleProviderRegistry(provider), toasts);
        var completions = Channel.CreateUnbounded<VoiceChannel>();
        orchestrator.UtteranceCompleted += channel => completions.Writer.TryWrite(channel);

        await orchestrator.EnqueueAsync(new VoiceUtterance("first failure", VoiceChannel.Chat));
        (await provider.NextStartedAsync()).Complete();
        await completions.Reader.ReadAsync();
        Assert.Equal(1, toastCount);

        await orchestrator.EnqueueAsync(new VoiceUtterance("second consecutive failure", VoiceChannel.Chat));
        (await provider.NextStartedAsync()).Complete();
        await completions.Reader.ReadAsync();
        Assert.Equal(1, toastCount);

        provider.ShouldFail = false;
        await orchestrator.EnqueueAsync(new VoiceUtterance("recovers", VoiceChannel.Chat));
        (await provider.NextStartedAsync()).Complete();
        await completions.Reader.ReadAsync();
        Assert.Equal(1, toastCount);

        provider.ShouldFail = true;
        await orchestrator.EnqueueAsync(new VoiceUtterance("new failure episode", VoiceChannel.Chat));
        (await provider.NextStartedAsync()).Complete();
        await completions.Reader.ReadAsync();
        await secondToast.Task;
        Assert.Equal(2, toastCount);
    }

    private static VoiceOrchestrator CreateOrchestrator(TempDir temp, ControlledVoiceProvider provider) =>
        new(NewSettings(temp), new SingleProviderRegistry(provider), new FakeToasts());

    private static TaskCompletionSource<VoiceChannel> CompletionSignal(VoiceOrchestrator orchestrator)
    {
        var completion = new TaskCompletionSource<VoiceChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        orchestrator.UtteranceCompleted += channel => completion.TrySetResult(channel);
        return completion;
    }

    private sealed class ControlledVoiceProvider : IVoiceProvider
    {
        private readonly Channel<VoiceCall> _started = Channel.CreateUnbounded<VoiceCall>();
        private readonly Channel<VoiceCall> _completed = Channel.CreateUnbounded<VoiceCall>();
        private readonly object _gate = new();
        private int _observedCompletionCount;

        public VoiceProvider Id => VoiceProvider.Kokoro;
        public string DisplayName => "Recording";
        public VoiceCapability Capabilities => VoiceCapability.TextToSpeech | VoiceCapability.Local;
        public (int Major, int Minor)? RequiredPythonVersion => null;
        public bool IsInstalled => true;
        public bool ShouldFail { get; set; }
        public List<VoiceCall> Started { get; } = [];
        public List<VoiceCall> Completed { get; } = [];

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
            var call = new VoiceCall(request);
            lock (_gate)
                Started.Add(call);
            await _started.Writer.WriteAsync(call, ct);
            await call.Release.Task.WaitAsync(ct);
            call.CompletedAt = DateTime.UtcNow;
            lock (_gate)
                Completed.Add(call);
            await _completed.Writer.WriteAsync(call, ct);
            return ShouldFail
                ? new VoiceSynthesisResult(false, "simulated failure")
                : new VoiceSynthesisResult(true, "ok");
        }

        public async Task<VoiceCall> NextStartedAsync() => await _started.Reader.ReadAsync();

        public async Task WaitForCompletedAsync(int count)
        {
            while (_observedCompletionCount < count)
            {
                await _completed.Reader.ReadAsync();
                _observedCompletionCount++;
            }
        }
    }

    private sealed class VoiceCall
    {
        public VoiceCall(VoiceSynthesisRequest request)
        {
            Request = request;
            StartedAt = DateTime.UtcNow;
        }

        public VoiceSynthesisRequest Request { get; }
        public DateTime StartedAt { get; }
        public DateTime CompletedAt { get; set; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete() => Release.TrySetResult();
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
        public ITtsService GetActiveTtsService() => throw new NotSupportedException();
    }
}
