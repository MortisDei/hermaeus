using System.Diagnostics;

namespace Hermaeus.Voice;

/// <summary>
/// Minimal cross-platform WAV playback: PowerShell's Media.SoundPlayer on
/// Windows, then the first of paplay/pw-play/aplay/afplay/ffplay found on
/// PATH elsewhere. Public so Hermaeus.Services (which already project-references
/// Hermaeus.Voice for provider registration) can share this instead of each
/// voice provider carrying its own playback logic (r11 4.2): the previous
/// VoiceProviderProcessRunner.PlayWavFileAsync tried only Linux players, so
/// every non-default provider (Kokoro Python, F5-TTS, XTTS, OpenAI voice) was
/// synthesize-only on a stock Windows machine, and XttsV2VoiceProvider
/// separately hardcoded ffplay with no Windows fallback at all.
/// </summary>
public static class AudioPlayback
{
    public static async Task PlayAsync(string wavFilePath, CancellationToken ct)
    {
        var players = new List<(string Command, IReadOnlyList<string> Arguments)>();
        if (OperatingSystem.IsWindows())
            players.Add(("powershell", BuildArguments("powershell", wavFilePath)));
        players.Add(("paplay", [wavFilePath]));
        players.Add(("pw-play", [wavFilePath]));
        players.Add(("aplay", ["-q", wavFilePath]));
        players.Add(("afplay", [wavFilePath]));
        players.Add(("ffplay", ["-nodisp", "-autoexit", wavFilePath]));

        await PlayCandidatesAsync(players, TryRunAsync, ct);
    }

    internal static async Task PlayCandidatesAsync(
        IReadOnlyList<(string Command, IReadOnlyList<string> Arguments)> players,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<bool>> tryRun,
        CancellationToken ct)
    {
        foreach (var player in players)
        {
            ct.ThrowIfCancellationRequested();
            if (await tryRun(player.Command, player.Arguments, ct))
                return;
        }

        throw new InvalidOperationException("Could not find a system audio player for the generated WAV file.");
    }

    /// <summary>Selection-logic seam for tests: reports which player command would be tried and picked, without actually invoking the OS audio subsystem.</summary>
    public static string? SelectPlayerCommand(Func<string, bool> isOnPath, bool? isWindowsOverride = null)
    {
        var isWindows = isWindowsOverride ?? OperatingSystem.IsWindows();
        if (isWindows && isOnPath("powershell")) return "powershell";
        if (isOnPath("paplay")) return "paplay";
        if (isOnPath("pw-play")) return "pw-play";
        if (isOnPath("aplay")) return "aplay";
        if (isOnPath("afplay")) return "afplay";
        if (isOnPath("ffplay")) return "ffplay";
        return null;
    }

    /// <summary>
    /// Builds process arguments without embedding the user-controlled WAV path
    /// in a shell command. PowerShell receives the path as an argument to a
    /// fixed script body.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(string command, string wavFilePath) =>
        command.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            ? ["-NoProfile", "-NonInteractive", "-Command",
                "param([string]$path); (New-Object Media.SoundPlayer $path).PlaySync();",
                wavFilePath]
            : [wavFilePath];

    private static async Task<bool> TryRunAsync(string command, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = BuildStartInfo(command, args);
        try
        {
            using var process = new Process { StartInfo = psi };
            if (!process.Start())
                return false;

            return await RunProcessLifecycleAsync(
                async token =>
                {
                    await process.WaitForExitAsync(token);
                    return process.ExitCode;
                },
                () => TerminateOwnedProcessAsync(process),
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    internal static ProcessStartInfo BuildStartInfo(string command, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        return psi;
    }

    internal static async Task<bool> RunProcessLifecycleAsync(
        Func<CancellationToken, Task<int>> waitForExit,
        Func<Task> terminateOwnedProcess,
        CancellationToken ct)
    {
        try
        {
            return await waitForExit(ct) == 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            try { await terminateOwnedProcess(); }
            catch { }
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static async Task TerminateOwnedProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
        catch (System.ComponentModel.Win32Exception) when (process.HasExited)
        {
        }
        catch (NotSupportedException)
        {
            if (!process.HasExited)
                process.Kill();
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
    }
}
