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
        var logTruncated = log.Length > 128 * 1024;
        if (logTruncated) log.Remove(0, log.Length - 128 * 1024);
        ct.ThrowIfCancellationRequested();

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

        using var process = new Process { StartInfo = psi };

        if (!process.Start())
            return (false, log.ToString());

        using var readers = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var stdout = ReadOutputAsync(process.StandardOutput, readers);
        var stderr = ReadOutputAsync(process.StandardError, readers);
        try
        {
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(readers.Token));
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            var cleanup = await StopProcessAsync(process, readers, stdout, stderr);
            if (cleanup.Length > 0)
                throw new OperationCanceledException(cleanup, ex, ct);
            throw;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or System.Text.DecoderFallbackException
            or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            var cleanup = await StopProcessAsync(process, readers, stdout, stderr);
            log.AppendLine($"Output capture failed ({ex.GetType().Name}). {cleanup}");
            return (false, CompleteLog(log, string.Empty, string.Empty, true).Text);
        }

        logTruncated |= stdout.Result.Truncated || stderr.Result.Truncated;
        var completedLog = CompleteLog(log, stdout.Result.Text, stderr.Result.Text, logTruncated);
        return (process.ExitCode == 0 && !completedLog.Truncated, completedLog.Text);
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

    private static async Task<(string Text, bool Truncated)> ReadOutputAsync(StreamReader reader, CancellationTokenSource cancellation)
    {
        const int limit = 64 * 1024;
        var text = new StringBuilder();
        var buffer = new char[4096];
        var truncated = false;
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation.Token)) > 0)
            {
                text.Append(buffer, 0, count);
                if (text.Length > limit)
                {
                    text.Remove(0, text.Length - limit);
                    truncated = true;
                }
            }
            return (text.ToString(), truncated);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or System.Text.DecoderFallbackException)
        {
            try { cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            throw;
        }
    }

    private static (string Text, bool Truncated) CompleteLog(StringBuilder log, string stdout, string stderr, bool truncated)
    {
        const int limit = 128 * 1024;
        const string notice = "[output truncated; final diagnostics retained; protocol incomplete]\n";
        log.Append(stdout).AppendLine().Append(stderr);
        truncated |= log.Length > limit;
        var keep = truncated ? limit - notice.Length : limit;
        if (log.Length > keep) log.Remove(0, log.Length - keep);
        if (truncated) log.Insert(0, notice);
        return (log.ToString(), truncated);
    }

    private static async Task<string> StopProcessAsync(Process process, CancellationTokenSource readers, Task stdout, Task stderr)
    {
        var errors = new List<string>();
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
        {
            errors.Add($"Voice process tree termination could not be confirmed ({ex.GetType().Name}).");
        }
        readers.Cancel();
        var joined = Task.WhenAll(stdout, stderr, process.WaitForExitAsync());
        try { await joined.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (OperationCanceledException) when (joined.IsCanceled) { }
        catch (Exception ex) when (ex is TimeoutException or IOException or ObjectDisposedException or System.Text.DecoderFallbackException
            or InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
        {
            errors.Add($"Voice process root or output cleanup could not be confirmed ({ex.GetType().Name}).");
            _ = joined.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        return string.Join(" ", errors);
    }

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;

}
