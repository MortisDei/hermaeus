using System.Diagnostics;
using System.Text;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services.ProcessManagement;

public sealed class KokoroProcessManager : IDisposable
{
    private Process? _process;
    private CancellationTokenSource? _healthCts;
    private string? _serverScriptPath;
    private readonly IProcessJobObject _jobObject;
    private readonly IRuntimeLogService? _runtimeLogs;
    /// <summary>
    /// r28 doc 03 3.3: the managed voice process is one of the four sources
    /// r24 named and never wired. Fire-and-forget at points where the outcome
    /// is already known; a recorder failure never fails a voice start.
    /// </summary>
    private readonly IActivityRecorder? _activity;

    public KokoroProcessManager(IProcessJobObject? jobObject = null, IRuntimeLogService? runtimeLogs = null, IActivityRecorder? activity = null)
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
        var defaultVoice = string.IsNullOrWhiteSpace(settings.Tts.Speaker) ? "af_heart" : settings.Tts.Speaker.Trim();
        var scriptPath = await EnsureServerScriptAsync(ct);

        var psi = new ProcessStartInfo
        {
            FileName = python,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        };

        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add("--host");
        psi.ArgumentList.Add(baseUrl.Host);
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(baseUrl.Port.ToString());
        psi.ArgumentList.Add("--voice");
        psi.ArgumentList.Add(defaultVoice);
        psi.ArgumentList.Add("--speed");
        psi.ArgumentList.Add(Math.Clamp(settings.Tts.Speed, 0.5, 2.0).ToString("0.0################", System.Globalization.CultureInfo.InvariantCulture));

        if (settings.Tts.Device.Equals("cpu", StringComparison.OrdinalIgnoreCase))
            psi.Environment["CUDA_VISIBLE_DEVICES"] = string.Empty;
        else if (settings.Tts.Device.Equals("cuda", StringComparison.OrdinalIgnoreCase))
            psi.Environment["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True";

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += (_, _) =>
        {
            StatusLabel = "Stopped";
            StatusChanged?.Invoke();
        };

        try
        {
            if (!_process.Start())
                throw new InvalidOperationException("Failed to start Kokoro voice service.");

            if (OperatingSystem.IsWindows() && !_jobObject.TryAssign(_process))
                _runtimeLogs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Voice,
                    "Could not attach Kokoro process to the app's job object; it may survive an abnormal app exit."));

            _healthCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await WaitForHealthAsync(settings.Tts.ServiceUrl, _healthCts.Token);
            StatusLabel = "Running";
            StatusChanged?.Invoke();
            _activity.RecordSafe("voice.backend-start", "kokoro", ActivityOutcome.Succeeded, "Kokoro voice service started");
        }
        catch (Exception ex)
        {
            _activity.RecordSafe("voice.backend-start", "kokoro", ActivityOutcome.Failed, "Kokoro voice service failed to start", ex.Message);
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
            _activity.RecordSafe("voice.backend-stop", "kokoro", ActivityOutcome.Succeeded, "Kokoro voice service stopped");

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
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var response = await http.GetAsync(url, ct);
                if (response.IsSuccessStatusCode) return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }

            await Task.Delay(400, ct);
        }

        throw new TimeoutException("Kokoro voice service did not become healthy within 2 minutes.");
    }

    private static string ResolvePython(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Tts.PythonPath))
            return settings.Tts.PythonPath.Trim();

        return OperatingSystem.IsWindows() ? "python" : "python3";
    }

    private async Task<string> EnsureServerScriptAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_serverScriptPath) && File.Exists(_serverScriptPath))
            return _serverScriptPath;

        var path = Path.Combine(Path.GetTempPath(), "hermaeus-kokoro-server.py");
        var script = EmbeddedPythonScriptLoader.Load("kokoro_server.py");
        await File.WriteAllTextAsync(path, script, Encoding.UTF8, ct);
        _serverScriptPath = path;
        return path;
    }

    public void Dispose()
    {
        Stop();
        if (!string.IsNullOrWhiteSpace(_serverScriptPath))
        {
            try { File.Delete(_serverScriptPath); }
            catch { }
        }
    }
}
