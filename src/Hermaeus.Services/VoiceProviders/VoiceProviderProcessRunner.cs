using System.Diagnostics;
using System.Text;
using Hermaeus.Core.Services;
using Hermaeus.Services.ProcessManagement;

namespace Hermaeus.Services;

internal static class VoiceProviderProcessRunner
{
    internal static async Task<(bool Success, string Log)> RunPythonScriptAsync(
        string pythonPath,
        string scriptContents,
        IReadOnlyList<string> args,
        CancellationToken ct,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var tempScript = Path.Combine(Path.GetTempPath(), $"hermaeus-voice-{Guid.NewGuid():N}.py");
        var log = new StringBuilder();
        try
        {
            await File.WriteAllTextAsync(tempScript, scriptContents, Encoding.UTF8, ct);
            return await RunProcessAsync(pythonPath, [tempScript, ..args], Path.GetTempPath(), log, ct, environment);
        }
        finally
        {
            try { File.Delete(tempScript); }
            catch { }
        }
    }

    internal static async Task<(bool Success, string Log)> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        StringBuilder? log,
        CancellationToken ct,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        log ??= new StringBuilder();
        log.AppendLine($"Command: {fileName} {string.Join(" ", args.Select(QuoteIfNeeded))}");

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                if (pair.Value is null)
                    psi.Environment.Remove(pair.Key);
                else
                    psi.Environment[pair.Key] = pair.Value;
            }
        }

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => AppendLine(e.Data, log);
        process.ErrorDataReceived += (_, e) => AppendLine(e.Data, log);

        if (!process.Start())
            return (false, log.ToString());

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return (process.ExitCode == 0, log.ToString());
    }

    /// <summary>
    /// r11 4.2: this used to try only paplay/pw-play/aplay/ffplay - Linux
    /// audio players - so on a stock Windows machine playback threw
    /// "Could not find paplay...". Delegates to Hermaeus.Voice.AudioPlayback,
    /// the one playback helper shared by every voice provider now.
    /// </summary>
    internal static Task PlayWavFileAsync(string wavFilePath, CancellationToken ct) =>
        Hermaeus.Voice.AudioPlayback.PlayAsync(wavFilePath, ct);

    internal static bool IsOnPath(string command) => ExecutableResolver.FindOnPath(command) is not null;

    internal static string ResolvePythonPath(ISettingsService settings)
    {
        var configured = settings.Settings.Tts.PythonPath.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        return OperatingSystem.IsWindows() ? "python" : "python3";
    }

    internal static bool IsExecutableAvailable(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        if (Path.IsPathFullyQualified(command))
            return File.Exists(command);

        return ExecutableResolver.FindOnPath(command) is not null;
    }

    internal static string? ResolveSpeakerFile(ISettingsService settings)
    {
        var speaker = settings.Settings.Tts.Speaker.Trim();
        if (string.IsNullOrWhiteSpace(speaker))
            return null;

        if (File.Exists(speaker))
            return Path.GetFullPath(speaker);

        var voiceDir = settings.Settings.Tts.VoiceDirectory.Trim();
        if (string.IsNullOrWhiteSpace(voiceDir) || !Directory.Exists(voiceDir))
            return null;

        var matches = Directory.EnumerateFiles(voiceDir, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileNameWithoutExtension(path).Equals(speaker, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count > 0 ? matches[0] : null;
    }

    private static void AppendLine(string? line, StringBuilder log)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (log)
            log.AppendLine(line);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;

}
