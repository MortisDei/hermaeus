using System.Diagnostics;
using System.Text;
using Aether.Core.Services;

namespace Aether.Services;

internal static class VoiceProviderProcessRunner
{
    internal static async Task<(bool Success, string Log)> RunPythonScriptAsync(
        string pythonPath,
        string scriptContents,
        IReadOnlyList<string> args,
        CancellationToken ct,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var tempScript = Path.Combine(Path.GetTempPath(), $"aether-voice-{Guid.NewGuid():N}.py");
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
        await process.WaitForExitAsync(ct);
        return (process.ExitCode == 0, log.ToString());
    }

    internal static async Task PlayWavFileAsync(string wavFilePath, CancellationToken ct)
    {
        if (IsOnPath("paplay") && await TryPlayWavFileAsync("paplay", [wavFilePath], ct)) return;
        if (IsOnPath("pw-play") && await TryPlayWavFileAsync("pw-play", [wavFilePath], ct)) return;
        if (IsOnPath("aplay") && await TryPlayWavFileAsync("aplay", ["-q", wavFilePath], ct)) return;
        if (IsOnPath("ffplay") && await TryPlayWavFileAsync("ffplay", ["-nodisp", "-autoexit", wavFilePath], ct)) return;

        throw new InvalidOperationException("Could not find paplay, pw-play, aplay, or ffplay to play generated audio.");
    }

    internal static bool IsOnPath(string command) => FindOnPath(command) is not null;

    internal static string? FindOnPath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        if (Path.IsPathRooted(command) && File.Exists(command))
            return command;

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [string.Empty];
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, command + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static async Task<bool> TryPlayWavFileAsync(string command, IReadOnlyList<string> args, CancellationToken ct)
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
            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
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

        return FindOnPath(command) is not null;
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

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

}
