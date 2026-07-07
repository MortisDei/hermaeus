using System.Diagnostics;

namespace Aether.Voice;

/// <summary>
/// Minimal cross-platform WAV playback, duplicated in miniature from
/// Aether.Services' VoiceProviderProcessRunner rather than shared, since
/// Aether.Services will reference Aether.Voice (for provider registration)
/// and a reverse reference would create a cycle.
/// </summary>
internal static class KokoroAudioPlayback
{
    public static async Task PlayAsync(string wavFilePath, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows() && await TryRunAsync("powershell", [
                "-NoProfile", "-Command",
                $"(New-Object Media.SoundPlayer '{wavFilePath}').PlaySync();"
            ], ct))
            return;

        if (await TryRunAsync("paplay", [wavFilePath], ct)) return;
        if (await TryRunAsync("pw-play", [wavFilePath], ct)) return;
        if (await TryRunAsync("aplay", ["-q", wavFilePath], ct)) return;
        if (await TryRunAsync("afplay", [wavFilePath], ct)) return;
        if (await TryRunAsync("ffplay", ["-nodisp", "-autoexit", wavFilePath], ct)) return;

        throw new InvalidOperationException("Could not find a system audio player for the generated WAV file.");
    }

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
