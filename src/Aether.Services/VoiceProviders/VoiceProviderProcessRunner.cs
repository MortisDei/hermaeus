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
        CancellationToken ct)
    {
        var tempScript = Path.Combine(Path.GetTempPath(), $"aether-voice-{Guid.NewGuid():N}.py");
        var log = new StringBuilder();
        try
        {
            await File.WriteAllTextAsync(tempScript, scriptContents, Encoding.UTF8, ct);
            return await RunProcessAsync(pythonPath, [tempScript, ..args], Path.GetTempPath(), log, ct);
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
        CancellationToken ct)
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
        var psi = new ProcessStartInfo
        {
            FileName = "ffplay",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-nodisp");
        psi.ArgumentList.Add("-autoexit");
        psi.ArgumentList.Add(wavFilePath);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException("ffplay not available; cannot play audio.");

        await process.WaitForExitAsync(ct);
    }

    internal static string ResolvePythonPath(ISettingsService settings)
    {
        var configured = settings.Settings.TtsPythonPath.Trim();
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
        var speaker = settings.Settings.TtsSpeaker.Trim();
        if (string.IsNullOrWhiteSpace(speaker))
            return null;

        if (File.Exists(speaker))
            return Path.GetFullPath(speaker);

        var voiceDir = settings.Settings.TtsVoiceDirectory.Trim();
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

    private static string? FindOnPath(string executableName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;

        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir, executableName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
