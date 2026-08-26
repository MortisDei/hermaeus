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
        if (OperatingSystem.IsWindows() && await TryRunAsync("powershell", BuildArguments("powershell", wavFilePath), ct))
            return;

        if (await TryRunAsync("paplay", [wavFilePath], ct)) return;
        if (await TryRunAsync("pw-play", [wavFilePath], ct)) return;
        if (await TryRunAsync("aplay", ["-q", wavFilePath], ct)) return;
        if (await TryRunAsync("afplay", [wavFilePath], ct)) return;
        if (await TryRunAsync("ffplay", ["-nodisp", "-autoexit", wavFilePath], ct)) return;

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

        try
        {
            using var process = new Process { StartInfo = psi };
            if (!process.Start())
                return false;

            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
