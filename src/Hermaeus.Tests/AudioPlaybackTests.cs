using Hermaeus.Voice;
using Xunit;

namespace Hermaeus.Tests;

public sealed class AudioPlaybackTests
{
    /// <summary>r11 4.2: on Windows, powershell (Media.SoundPlayer) must be tried first; the previous Linux-only implementation would never resolve any player on a stock Windows machine.</summary>
    [Fact]
    public void SelectPlayerCommand_prefers_powershell_on_windows()
    {
        var selected = AudioPlayback.SelectPlayerCommand(command => command is "powershell" or "aplay", isWindowsOverride: true);
        Assert.Equal("powershell", selected);
    }

    [Fact]
    public void SelectPlayerCommand_does_not_try_powershell_on_non_windows()
    {
        var selected = AudioPlayback.SelectPlayerCommand(command => command is "powershell" or "aplay", isWindowsOverride: false);
        Assert.Equal("aplay", selected);
    }

    [Fact]
    public void SelectPlayerCommand_falls_through_in_order_when_earlier_players_are_missing()
    {
        var selected = AudioPlayback.SelectPlayerCommand(command => command == "ffplay", isWindowsOverride: false);
        Assert.Equal("ffplay", selected);
    }

    [Fact]
    public void SelectPlayerCommand_returns_null_when_nothing_is_available()
    {
        var selected = AudioPlayback.SelectPlayerCommand(_ => false, isWindowsOverride: true);
        Assert.Null(selected);
    }

    [Fact]
    public void Windows_arguments_keep_the_path_out_of_the_fixed_command_body()
    {
        var args = AudioPlayback.BuildArguments("powershell", "C:\\private\\tone's.wav");

        Assert.Contains("param([string]$path)", args[3]);
        Assert.Contains("C:\\private\\tone's.wav", args);
        Assert.DoesNotContain("tone's.wav", args[3]);
    }

    [Fact]
    public async Task Cancellation_stops_fallback_sequence_without_audio_playback()
    {
        using var cts = new CancellationTokenSource();
        var attempted = new List<string>();
        var first = true;
        var players = new[]
        {
            ("first", (IReadOnlyList<string>)["tone.wav"]),
            ("fallback", (IReadOnlyList<string>)["tone.wav"])
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => AudioPlayback.PlayCandidatesAsync(
            players,
            async (command, _, ct) =>
            {
                attempted.Add(command);
                if (first)
                {
                    first = false;
                    cts.Cancel();
                    await Task.Yield();
                    ct.ThrowIfCancellationRequested();
                }
                return false;
            },
            cts.Token));

        Assert.Equal(["first"], attempted);
    }

    [Fact]
    public async Task Ordinary_player_failure_still_tries_the_next_fallback()
    {
        var attempted = new List<string>();
        var players = new[]
        {
            ("first", (IReadOnlyList<string>)["tone.wav"]),
            ("fallback", (IReadOnlyList<string>)["tone.wav"])
        };

        await AudioPlayback.PlayCandidatesAsync(
            players,
            (command, _, _) =>
            {
                attempted.Add(command);
                return Task.FromResult(command == "fallback");
            },
            CancellationToken.None);

        Assert.Equal(["first", "fallback"], attempted);
    }
}
