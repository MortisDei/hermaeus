using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class AudioFeedbackServiceTests
{
    [Fact]
    public void Default_policy_enables_task_notifications_and_keeps_ambient_events_off()
    {
        var settings = new AudioFeedbackSettings();

        Assert.True(settings.IsEnabled(AudioFeedbackEventKind.TaskNeedsApproval));
        Assert.True(settings.IsEnabled(AudioFeedbackEventKind.TaskCompleted));
        Assert.True(settings.IsEnabled(AudioFeedbackEventKind.TaskFailed));
        Assert.True(settings.IsEnabled(AudioFeedbackEventKind.ManagedRuntimeFailed));
        Assert.False(settings.IsEnabled(AudioFeedbackEventKind.ManagedRuntimeReady));
        Assert.False(settings.IsEnabled(AudioFeedbackEventKind.RecordingStarted));
    }

    [Fact]
    public async Task Publish_applies_settings_dedupe_and_tts_suppression_before_playback()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.Tts.AudioFeedback.Volume = 25;
        settings.Settings.Tts.AudioFeedback.EventEnabled[nameof(AudioFeedbackEventKind.TaskFailed)] = true;
        settings.Settings.Tts.AudioFeedback.EventEnabled[nameof(AudioFeedbackEventKind.TaskCompleted)] = true;
        var voice = new GatedVoice();
        var played = new List<string>();
        await using var service = new AudioFeedbackService(settings, voice,
            playback: (path, _) => { played.Add(path); return Task.CompletedTask; });

        await service.PublishAsync(AudioFeedbackEventKind.TaskCompleted);
        await voice.SpeakingWasChecked.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(played);

        voice.Speaking = false;
        await Helpers.WaitForAsync(() => played.Count == 1, "deferred cue after TTS");
        await service.PublishAsync(AudioFeedbackEventKind.TaskFailed);
        await service.PublishAsync(AudioFeedbackEventKind.TaskFailed);
        await Helpers.WaitForAsync(() => played.Count == 2, "one deduplicated cue");
    }

    [Fact]
    public async Task Mute_keeps_saved_volume_and_suppresses_playback()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.Tts.AudioFeedback.Muted = true;
        settings.Settings.Tts.AudioFeedback.Volume = 77;
        var played = false;
        await using var service = new AudioFeedbackService(settings,
            playback: (_, _) => { played = true; return Task.CompletedTask; });

        await service.PublishAsync(AudioFeedbackEventKind.TaskFailed);
        await Task.Delay(100);

        Assert.False(played);
        Assert.Equal(77, settings.Settings.Tts.AudioFeedback.Volume);
    }

    [Fact]
    public async Task Publish_generates_a_wav_resource_and_records_selection_and_playback_stages()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var logs = new CollectingRuntimeLog();
        var played = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var service = new AudioFeedbackService(
            settings,
            logs: logs,
            playback: (path, _) =>
            {
                Assert.True(File.Exists(path));
                var header = File.ReadAllBytes(path).Take(12).ToArray();
                Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(header, 0, 4));
                Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(header, 8, 4));
                played.TrySetResult(path);
                return Task.CompletedTask;
            });

        await service.PublishAsync(AudioFeedbackEventKind.TaskCompleted);
        var path = await played.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Helpers.WaitForAsync(() => !File.Exists(path), "feedback WAV cleanup");

        Assert.Contains(logs.Entries, entry => entry.Message.Contains("event=TaskCompleted; stage=resource", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, entry => entry.Message.Contains("event=TaskCompleted; stage=result", StringComparison.Ordinal));
    }

    [Fact]
    public void Event_cues_are_distinct_multi_tone_patterns_instead_of_a_generic_beep()
    {
        var approval = AudioFeedbackAssets.Resolve(AudioFeedbackEventKind.TaskNeedsApproval);
        var completed = AudioFeedbackAssets.Resolve(AudioFeedbackEventKind.TaskCompleted);
        var failed = AudioFeedbackAssets.Resolve(AudioFeedbackEventKind.TaskFailed);

        Assert.NotEqual(approval.Id, completed.Id);
        Assert.NotEqual(completed.Id, failed.Id);
        Assert.True(approval.Frequencies.Count > 1);
        Assert.True(completed.Frequencies.Count > 1);
        Assert.NotEqual(approval.Frequencies, failed.Frequencies);
    }

    private sealed class CollectingRuntimeLog : IRuntimeLogService
    {
        public List<RuntimeLogEntry> Entries { get; } = [];
        public event Action<RuntimeLogEntry>? LogAdded;
        public void Add(RuntimeLogEntry entry) { Entries.Add(entry); LogAdded?.Invoke(entry); }
        public IReadOnlyList<RuntimeLogEntry> GetEntries() => Entries;
        public void ClearInMemory() => Entries.Clear();
        public string GetLogDirectory() => string.Empty;
        public string GetLogFilePath() => string.Empty;
    }

    private sealed class GatedVoice : IVoiceOrchestrator
    {
        public bool Speaking { get; set; } = true;
        public bool IsMuted { get; set; }
        public TaskCompletionSource SpeakingWasChecked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsSpeaking
        {
            get
            {
                SpeakingWasChecked.TrySetResult();
                return Speaking;
            }
        }

        public event Action<VoiceChannel, string>? UtteranceStarted;
        public event Action<VoiceChannel>? UtteranceCompleted;

        public Task EnqueueAsync(VoiceUtterance utterance, CancellationToken ct = default)
        {
            UtteranceStarted?.Invoke(utterance.Channel, utterance.Text);
            UtteranceCompleted?.Invoke(utterance.Channel);
            return Task.CompletedTask;
        }
        public void StopChannel(VoiceChannel channel) { }
        public void StopAll() { }
        public Task ShutdownAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
