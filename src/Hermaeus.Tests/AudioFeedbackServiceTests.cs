using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class AudioFeedbackServiceTests
{
    [Fact]
    public void Default_policy_enables_only_restrained_events()
    {
        var settings = new AudioFeedbackSettings();

        Assert.True(settings.IsEnabled(AudioFeedbackEventKind.TaskNeedsApproval));
        Assert.True(settings.IsEnabled(AudioFeedbackEventKind.TaskFailed));
        Assert.False(settings.IsEnabled(AudioFeedbackEventKind.ManagedRuntimeReady));
        Assert.False(settings.IsEnabled(AudioFeedbackEventKind.RecordingStarted));
    }

    [Fact]
    public async Task Publish_applies_settings_dedupe_and_tts_suppression_before_playback()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.Tts.AudioFeedback.Volume = 25;
        var voice = new FakeVoiceOrchestrator { IsSpeaking = true };
        var played = new List<string>();
        await using var service = new AudioFeedbackService(settings, voice,
            playback: (path, _) => { played.Add(path); return Task.CompletedTask; });

        await service.PublishAsync(AudioFeedbackEventKind.TaskCompleted);
        await Task.Delay(100);
        Assert.Empty(played);

        voice.IsSpeaking = false;
        await service.PublishAsync(AudioFeedbackEventKind.TaskFailed);
        await service.PublishAsync(AudioFeedbackEventKind.TaskFailed);
        await Helpers.WaitForAsync(() => played.Count == 1, "one deduplicated cue");
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
}
