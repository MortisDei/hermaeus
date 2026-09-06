using Hermaeus.Voice;
using Xunit;

namespace Hermaeus.Tests;

public sealed class AudioPlaybackTests
{
    /// <summary>r11 4.2: Windows uses the native winmm player and does not depend on a media-player file association.</summary>
    [Fact]
    public void SelectPlayerCommand_uses_native_winmm_on_windows()
    {
        var selected = AudioPlayback.SelectPlayerCommand(command => command is "powershell" or "aplay", isWindowsOverride: true);
        Assert.Equal("winmm", selected);
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
    public void SelectPlayerCommand_uses_native_winmm_without_a_path_probe()
    {
        var selected = AudioPlayback.SelectPlayerCommand(_ => false, isWindowsOverride: true);
        Assert.Equal("winmm", selected);
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
    public void Legacy_process_arguments_do_not_use_default_file_association()
    {
        var args = AudioPlayback.BuildArguments("powershell", "C:\\private\\preview.wav");
        var psi = AudioPlayback.BuildStartInfo("powershell", args);

        Assert.False(psi.UseShellExecute);
        Assert.Equal("powershell", psi.FileName);
        Assert.DoesNotContain("wmplayer", string.Join(" ", psi.ArgumentList), StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task Cancellation_stays_cancellation_when_owned_process_termination_fails()
    {
        using var cts = new CancellationTokenSource();
        var attempted = new List<string>();
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
                return await AudioPlayback.RunProcessLifecycleAsync(
                    async token =>
                    {
                        cts.Cancel();
                        await Task.Yield();
                        token.ThrowIfCancellationRequested();
                        return 0;
                    },
                    () => Task.FromException(new InvalidOperationException("termination test failure")),
                    ct);
            },
            cts.Token));

        Assert.Equal(["first"], attempted);
    }

    [Fact]
    public async Task Successful_first_player_stops_candidate_traversal()
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
                return Task.FromResult(true);
            },
            CancellationToken.None);

        Assert.Equal(["first"], attempted);
    }

    [Fact]
    public async Task Successful_player_reports_the_selected_backend()
    {
        var selected = string.Empty;
        await AudioPlayback.PlayCandidatesAsync(
            [("first", (IReadOnlyList<string>)["tone.wav"])],
            (_, _, _) => Task.FromResult(true),
            CancellationToken.None,
            value => selected = value);

        Assert.Equal("first", selected);
    }
}
