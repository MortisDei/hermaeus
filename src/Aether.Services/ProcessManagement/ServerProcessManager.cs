using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Aether.Core.Models;

namespace Aether.Services.ProcessManagement;

/// <summary>
/// Manages a single llama-server (or compatible) child process.
/// Launches with configured args, health-polls /health until ready,
/// pipes stdout+stderr to a capped ring buffer, and kills cleanly on Stop/Dispose.
/// </summary>
public sealed class ServerProcessManager : IDisposable
{
    private Process? _process;
    private CancellationTokenSource? _monitorCts;
    private readonly ConcurrentQueue<string> _logRing = new();
    private const int MaxLogLines = 300;

    public ServerStatus Status { get; private set; } = ServerStatus.Stopped;
    public string       ErrorMessage { get; private set; } = string.Empty;

    public event Action<ServerStatus>? StatusChanged;
    public event Action<string>?       LogLine;

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task StartAsync(ServerConfig cfg, CancellationToken ct = default)
    {
        if (Status is ServerStatus.Running or ServerStatus.Starting) return;

        ErrorMessage = string.Empty;
        ClearLog();
        SetStatus(ServerStatus.Starting);

        try
        {
            _process = BuildProcess(cfg);
            _process.OutputDataReceived += (_, e) => { if (e.Data != null) Emit(e.Data); };
            _process.ErrorDataReceived  += (_, e) => { if (e.Data != null) Emit(e.Data); };
            _process.Exited += OnProcessExited;

            if (!_process.Start())
                throw new InvalidOperationException($"Failed to start '{cfg.ExecutablePath}'");

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Emit($"[aether] Launched PID {_process.Id} — waiting for /health on port {cfg.Port}...");

            _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await WaitForHealthAsync(cfg.Port, _monitorCts.Token);

            SetStatus(ServerStatus.Running);
            Emit($"[aether] Server ready on port {cfg.Port}.");
        }
        catch (OperationCanceledException)
        {
            KillProcess();
            SetStatus(ServerStatus.Stopped);
        }
        catch (Exception ex)
        {
            KillProcess();
            ErrorMessage = ex.Message;
            SetStatus(ServerStatus.Error);
            Emit($"[aether] ERROR: {ex.Message}");
        }
    }

    public void Stop()
    {
        Emit("[aether] Stopping...");
        KillProcess();
        SetStatus(ServerStatus.Stopped);
    }

    public string GetLog() => string.Join('\n', _logRing);

    public void ClearLog()
    {
        while (_logRing.TryDequeue(out _)) { }
    }

    public void Dispose()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        KillProcess();
    }

    // ── Process build ─────────────────────────────────────────────────────────

    private static Process BuildProcess(ServerConfig cfg)
    {
        var args = BuildArgs(cfg);
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = cfg.ExecutablePath,
                Arguments              = args,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                WorkingDirectory       = Path.GetDirectoryName(
                    Path.IsPathFullyQualified(cfg.ExecutablePath)
                        ? cfg.ExecutablePath
                        : Environment.CurrentDirectory)
                    ?? Environment.CurrentDirectory
            },
            EnableRaisingEvents = true
        };
    }

    private static string BuildArgs(ServerConfig cfg)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(cfg.ModelPath))
            parts.Add($"-m \"{cfg.ModelPath}\"");

        parts.Add($"--port {cfg.Port}");
        parts.Add("--host 127.0.0.1");
        parts.Add($"--ctx-size {cfg.ContextSize}");

        if (cfg.Threads > 0)
            parts.Add($"--threads {cfg.Threads}");

        if (cfg.GpuLayers > 0)
            parts.Add($"--n-gpu-layers {cfg.GpuLayers}");

        if (cfg.EmbeddingsMode)
            parts.Add("--embeddings");

        if (!string.IsNullOrWhiteSpace(cfg.ExtraArgs))
            parts.Add(cfg.ExtraArgs.Trim());

        return string.Join(' ', parts);
    }

    // ── Health poll ───────────────────────────────────────────────────────────

    private static async Task WaitForHealthAsync(int port, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var url      = $"http://127.0.0.1:{port}/health";
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var r = await http.GetAsync(url, ct);
                if (r.IsSuccessStatusCode) return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }

            await Task.Delay(600, ct);
        }
        throw new TimeoutException($"llama-server on port {port} did not respond within 60s");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void OnProcessExited(object? sender, EventArgs e)
    {
        var code = _process?.ExitCode ?? -1;
        Emit($"[aether] Process exited with code {code}.");
        if (Status == ServerStatus.Running)
            SetStatus(code == 0 ? ServerStatus.Stopped : ServerStatus.Error);
    }

    private void KillProcess()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch { }
        _process?.Dispose();
        _process = null;
    }

    private void SetStatus(ServerStatus s)
    {
        Status = s;
        StatusChanged?.Invoke(s);
    }

    private void Emit(string line)
    {
        _logRing.Enqueue($"[{DateTime.Now:HH:mm:ss}] {line}");
        while (_logRing.Count > MaxLogLines)
            _logRing.TryDequeue(out _);
        LogLine?.Invoke(line);
    }
}
