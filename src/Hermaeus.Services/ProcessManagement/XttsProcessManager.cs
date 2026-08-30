using System.Diagnostics;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services.ProcessManagement;

public sealed class XttsProcessManager : IDisposable
{
    private Process? _process;
    private CancellationTokenSource? _healthCts;
    private readonly IProcessJobObject _jobObject;
    private readonly IRuntimeLogService? _runtimeLogs;
    /// <summary>r28 doc 03 3.3. Fire-and-forget; a recorder failure never fails a voice start.</summary>
    private readonly IActivityRecorder? _activity;

    public XttsProcessManager(IProcessJobObject? jobObject = null, IRuntimeLogService? runtimeLogs = null, IActivityRecorder? activity = null)
    {
        _jobObject = jobObject ?? ProcessJobObject.Default;
        _runtimeLogs = runtimeLogs;
        _activity = activity;
    }

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Whether a live OS process is attached, without throwing for one that never started.</summary>
    private bool WasStarted()
    {
        try { return IsRunning; }
        catch (InvalidOperationException) { return false; }
    }

    public string StatusLabel { get; private set; } = "Stopped";
    public event Action? StatusChanged;

    public async Task StartAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (IsRunning) return;

        StatusLabel = "Starting";
        StatusChanged?.Invoke();

        var baseUrl = new Uri(settings.Tts.ServiceUrl.TrimEnd('/'));
        var python = ResolvePython(settings);
        var script = ResolveScript(settings.Tts.ScriptPath);
        var outputDir = ResolveOutputDir(settings);
        Directory.CreateDirectory(outputDir);

        var psi = new ProcessStartInfo
        {
            FileName = python,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(script) ?? Environment.CurrentDirectory
        };
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add("--host");
        psi.ArgumentList.Add(baseUrl.Host);
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(baseUrl.Port.ToString());
        psi.ArgumentList.Add("--model-version");
        psi.ArgumentList.Add(settings.Tts.ModelVersion);
        psi.ArgumentList.Add("--device");
        psi.ArgumentList.Add(settings.Tts.Device);
        psi.ArgumentList.Add("--output-dir");
        psi.ArgumentList.Add(outputDir);
        if (!string.IsNullOrWhiteSpace(settings.Tts.ModelDirectory))
        {
            psi.ArgumentList.Add("--model-dir");
            psi.ArgumentList.Add(Path.GetFullPath(settings.Tts.ModelDirectory.Trim()));
        }
        if (settings.Tts.Preload)
            psi.ArgumentList.Add("--preload");

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += (_, _) =>
        {
            StatusLabel = "Stopped";
            StatusChanged?.Invoke();
        };

        try
        {
            if (!_process.Start())
                throw new InvalidOperationException("Failed to start XTTS v2 server.");

            if (OperatingSystem.IsWindows() && !_jobObject.TryAssign(_process))
                _runtimeLogs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Voice,
                    "Could not attach XTTS v2 process to the app's job object; it may survive an abnormal app exit."));

            _healthCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await WaitForHealthAsync(settings.Tts.ServiceUrl, _healthCts.Token);
            StatusLabel = "Running";
            StatusChanged?.Invoke();
            _activity.RecordSafe("voice.backend-start", "xtts", ActivityOutcome.Succeeded, "XTTS v2 voice service started");
        }
        catch (Exception ex)
        {
            _activity.RecordSafe("voice.backend-start", "xtts", ActivityOutcome.Failed, "XTTS v2 voice service failed to start", ex.Message);
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        // Not IsRunning: Process.HasExited throws when a Process object was
        // constructed but never successfully started, which is exactly the
        // state Stop() is in when it runs from StartAsync's catch.
        if (WasStarted())
            _activity.RecordSafe("voice.backend-stop", "xtts", ActivityOutcome.Succeeded, "XTTS v2 voice service stopped");

        _healthCts?.Cancel();
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch { }
        _process?.Dispose();
        _process = null;
        StatusLabel = "Stopped";
        StatusChanged?.Invoke();
    }

    private static async Task WaitForHealthAsync(string baseUrl, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var url = $"{baseUrl.TrimEnd('/')}/health";
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var response = await http.GetAsync(url, ct);
                if (response.IsSuccessStatusCode) return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }

            await Task.Delay(500, ct);
        }

        throw new TimeoutException("XTTS v2 did not become healthy within 3 minutes.");
    }

    private static string ResolvePython(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Tts.PythonPath))
            return settings.Tts.PythonPath.Trim();

        var script = ResolveScript(settings.Tts.ScriptPath);
        var repo = Path.GetDirectoryName(script) ?? string.Empty;
        var venv = Path.Combine(repo, "models", "tts", "xtts", "venv", OperatingSystem.IsWindows() ? "Scripts" : "bin", OperatingSystem.IsWindows() ? "python.exe" : "python");
        return File.Exists(venv) ? venv : "python3";
    }

    private static string ResolveScript(string scriptPath)
    {
        if (!string.IsNullOrWhiteSpace(scriptPath) && File.Exists(scriptPath.Trim()))
            return Path.GetFullPath(scriptPath.Trim());

        throw new FileNotFoundException("Set the XTTS API server script path first.", scriptPath);
    }

    private static string ResolveOutputDir(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Tts.OutputDirectory))
            return Path.GetFullPath(settings.Tts.OutputDirectory.Trim());

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hermaeus",
            "xtts-output");
    }

    public void Dispose() => Stop();
}
