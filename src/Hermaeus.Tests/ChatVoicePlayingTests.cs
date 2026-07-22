using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>r19 4.4: ChatViewModel.IsVoicePlaying tracks the orchestrator's UtteranceStarted/UtteranceCompleted events for the Chat channel, driving the speak/stop icon swap.</summary>
public sealed class ChatVoicePlayingTests
{
    private static (ChatViewModel Vm, FakeVoiceOrchestratorForTest Voice) Build(TempDir temp)
    {
        var settings = NewSettings(temp);
        var memoryStore = new MemoryStore(settings);
        memoryStore.InitializeAsync().GetAwaiter().GetResult();
        var voice = new FakeVoiceOrchestratorForTest();
        var vm = new ChatViewModel(
            new FakeLlm(), new ThrowingSaveConversationStore(), memoryStore, settings,
            new FakeTts(), new ModelProfileService(settings), new FakeToasts(),
            new FakeConversationMemoryService(), new RuntimeLogService(settings), new ConversationExportService(),
            voice: voice);
        return (vm, voice);
    }

    [Fact]
    public void IsVoicePlaying_tracks_UtteranceStarted_and_UtteranceCompleted_for_the_chat_channel()
    {
        using var temp = new TempDir();
        var (vm, voice) = Build(temp);

        Assert.False(vm.IsVoicePlaying);

        voice.RaiseStarted(VoiceChannel.Chat, "hello");
        Assert.True(vm.IsVoicePlaying);

        voice.RaiseCompleted(VoiceChannel.Chat);
        Assert.False(vm.IsVoicePlaying);
    }

    [Fact]
    public void IsVoicePlaying_ignores_other_channels()
    {
        using var temp = new TempDir();
        var (vm, voice) = Build(temp);

        voice.RaiseStarted(VoiceChannel.Agent, "narration");

        Assert.False(vm.IsVoicePlaying);
    }

    [Fact]
    public void StopSpeakingCommand_stops_only_the_chat_channel()
    {
        using var temp = new TempDir();
        var (vm, voice) = Build(temp);

        vm.StopSpeakingCommand.Execute(null);

        Assert.Contains(VoiceChannel.Chat, voice.StoppedChannels);
    }

    private sealed class FakeVoiceOrchestratorForTest : IVoiceOrchestrator
    {
        public List<VoiceChannel> StoppedChannels { get; } = [];
        public bool IsMuted { get; set; }
        public bool IsSpeaking { get; private set; }
        public event Action<VoiceChannel, string>? UtteranceStarted;
        public event Action<VoiceChannel>? UtteranceCompleted;

        public Task EnqueueAsync(VoiceUtterance utterance, CancellationToken ct = default) => Task.CompletedTask;

        public void StopChannel(VoiceChannel channel)
        {
            StoppedChannels.Add(channel);
            IsSpeaking = false;
            UtteranceCompleted?.Invoke(channel);
        }

        public void StopAll() => IsSpeaking = false;

        public void RaiseStarted(VoiceChannel channel, string text)
        {
            IsSpeaking = true;
            UtteranceStarted?.Invoke(channel, text);
        }

        public void RaiseCompleted(VoiceChannel channel)
        {
            IsSpeaking = false;
            UtteranceCompleted?.Invoke(channel);
        }
    }
}
