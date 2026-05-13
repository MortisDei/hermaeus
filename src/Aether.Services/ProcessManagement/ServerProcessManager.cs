using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Aether.Core.Models;
using Aether.Core.Services;

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
    private readonly IRedactionService? _redactor;
    private const int MaxLogLines = 300;

    public ServerStatus Status { get; private set; } = ServerStatus.Stopped;
    public string       ErrorMessage { get; private set; } = string.Empty;

    public event Action<ServerStatus>? StatusChanged;
    public event Action<string>?       LogLine;

    private static readonly Regex OffloadedLayersRegex =
        new(@"offloaded\s+(?<used>\d+)\s*/\s*(?<total>\d+)\s+layers", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FitLayersRegex =
        new(@"Vulkan\d+.*:\s+(?<used>\d+)\s+layers", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ServerProcessManager(IRedactionService? redactor = null)
    {
        _redactor = redactor;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task StartAsync(ServerConfig cfg, CancellationToken ct = default)
    {
        if (Status is ServerStatus.Running or ServerStatus.Starting) return;

        ErrorMessage = string.Empty;
        ClearLog();
        SetStatus(ServerStatus.Starting);

        try
        {
            cfg = NormalizeConfig(cfg);
            _process = BuildProcess(cfg);
            _process.OutputDataReceived += (_, e) => { if (e.Data != null) Emit(e.Data); };
            _process.ErrorDataReceived  += (_, e) => { if (e.Data != null) Emit(e.Data); };
            _process.Exited += OnProcessExited;

            if (!_process.Start())
                throw new InvalidOperationException($"Failed to start '{cfg.ExecutablePath}'");

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Emit($"[aether] Launched PID {_process.Id} - waiting for /health on port {cfg.Port}...");
            Emit($"[aether] Model: {cfg.ModelPath}");

            _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await WaitForHealthAsync(cfg.Port, () => _process, _monitorCts.Token);

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
            ErrorMessage = BuildErrorMessage(ex);
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

    public static async Task<ServerTuneResult> AutoTuneAsync(
        ServerConfig cfg,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var probe = NormalizeConfig(new ServerConfig
        {
            Name           = cfg.Name,
            ExecutablePath = cfg.ExecutablePath,
            ModelPath      = cfg.ModelPath,
            Port           = cfg.Port,
            ContextSize    = cfg.ContextSize,
            GpuLayers      = 0,
            Threads        = cfg.Threads,
            EmbeddingsMode = cfg.EmbeddingsMode,
            AutoStart      = false,
            ExtraArgs      = cfg.ExtraArgs
        });

        progress?.Report("[aether] Auto-tune: probing llama.cpp auto-fit...");

        using var process = BuildProcess(probe);
        var lines = new ConcurrentQueue<string>();
        var completion = new TaskCompletionSource<ServerTuneResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void HandleLine(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;

            var line = raw.Trim();
            lines.Enqueue(line);
            while (lines.Count > 80)
                lines.TryDequeue(out _);

            progress?.Report(line);

            var offloaded = OffloadedLayersRegex.Match(line);
            if (offloaded.Success)
            {
                completion.TrySetResult(new ServerTuneResult(
                    int.Parse(offloaded.Groups["used"].Value),
                    int.Parse(offloaded.Groups["total"].Value),
                    string.Join('\n', lines)));
                return;
            }

            var fitted = FitLayersRegex.Match(line);
            if (fitted.Success)
            {
                completion.TrySetResult(new ServerTuneResult(
                    int.Parse(fitted.Groups["used"].Value),
                    null,
                    string.Join('\n', lines)));
            }
        }

        process.OutputDataReceived += (_, e) => HandleLine(e.Data);
        process.ErrorDataReceived += (_, e) => HandleLine(e.Data);
        process.Exited += (_, _) =>
        {
            if (!completion.Task.IsCompleted)
            {
                completion.TrySetException(new InvalidOperationException(
                    $"llama-server exited during auto-tune. Exit code: {process.ExitCode}.\n\nRecent log:\n{string.Join('\n', lines)}"));
            }
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Failed to start '{probe.ExecutablePath}' for auto-tune.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            using var registration = linked.Token.Register(() =>
                completion.TrySetException(new TimeoutException(
                    $"Auto-tune did not reach a GPU layer decision within 3 minutes.\n\nRecent log:\n{string.Join('\n', lines)}")));

            return await completion.Task;
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
        }
    }

    // ── Process build ─────────────────────────────────────────────────────────

    private static Process BuildProcess(ServerConfig cfg)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName               = cfg.ExecutablePath,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            WorkingDirectory       = GetWorkingDirectory(cfg.ExecutablePath)
        };

        foreach (var arg in BuildLaunchArguments(cfg))
            startInfo.ArgumentList.Add(arg);

        return new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
    }

    public static IReadOnlyList<string> BuildLaunchArguments(ServerConfig cfg)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(cfg.ModelPath))
        {
            parts.Add("-m");
            parts.Add(cfg.ModelPath);
        }

        parts.Add("--port");
        parts.Add(cfg.Port.ToString());
        parts.Add("--host");
        parts.Add("127.0.0.1");
        parts.Add("--ctx-size");
        parts.Add(cfg.ContextSize.ToString());

        if (cfg.Threads > 0)
        {
            parts.Add("--threads");
            parts.Add(cfg.Threads.ToString());
        }

        if (cfg.GpuLayers > 0)
        {
            parts.Add("--n-gpu-layers");
            parts.Add(cfg.GpuLayers.ToString());
        }

        if (cfg.EmbeddingsMode)
            parts.Add("--embeddings");

        if (!string.IsNullOrWhiteSpace(cfg.ExtraArgs))
            parts.AddRange(ExtraArgsParser.Split(cfg.ExtraArgs));

        return parts;
    }

    private static ServerConfig NormalizeConfig(ServerConfig cfg)
    {
        cfg.ExecutablePath = ResolveExecutable(cfg.ExecutablePath);
        cfg.ModelPath      = ResolveModel(cfg.ModelPath);
        return cfg;
    }

    private static string ResolveExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new InvalidOperationException("Set the llama-server executable path first.");

        var trimmed = executablePath.Trim();
        if (Directory.Exists(trimmed))
        {
            var direct = Path.Combine(trimmed, "llama-server");
            if (File.Exists(direct)) return direct;

            var matches = Directory.EnumerateFiles(trimmed, "llama-server", SearchOption.AllDirectories)
                .Take(2)
                .ToArray();

            return matches.Length switch
            {
                1 => matches[0],
                0 => throw new FileNotFoundException($"No llama-server executable was found inside '{trimmed}'."),
                _ => throw new InvalidOperationException($"More than one llama-server executable was found inside '{trimmed}'. Select the exact file.")
            };
        }

        if (File.Exists(trimmed)) return trimmed;

        if (!LooksLikePath(trimmed))
        {
            var resolved = FindOnPath(trimmed);
            if (resolved is not null) return resolved;
            throw new FileNotFoundException($"'{trimmed}' was not found on PATH. Select the full llama-server executable.");
        }

        throw new FileNotFoundException($"The llama-server executable does not exist: '{trimmed}'.");
    }

    private static string ResolveModel(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            throw new InvalidOperationException("Select a .gguf model file before starting the server.");

        var trimmed = modelPath.Trim();
        if (Directory.Exists(trimmed))
        {
            var models = Directory.EnumerateFiles(trimmed, "*.gguf", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToArray();

            return models.Length switch
            {
                1 => models[0],
                0 => throw new FileNotFoundException($"No .gguf model files were found inside '{trimmed}'."),
                _ => throw new InvalidOperationException(
                    $"More than one .gguf model was found inside '{trimmed}'. Select the exact model file, for example '{models[0]}'.")
            };
        }

        if (File.Exists(trimmed)) return trimmed;

        throw new FileNotFoundException($"The model file does not exist: '{trimmed}'.");
    }

    private static string GetWorkingDirectory(string executablePath)
    {
        if (Path.IsPathFullyQualified(executablePath) || LooksLikePath(executablePath))
            return Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory;

        return Environment.CurrentDirectory;
    }

    private static bool LooksLikePath(string value) =>
        value.Contains(Path.DirectorySeparatorChar) ||
        value.Contains(Path.AltDirectorySeparatorChar);

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

    // ── Health poll ───────────────────────────────────────────────────────────

    private static async Task WaitForHealthAsync(
        int port,
        Func<Process?> getProcess,
        CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var url      = $"http://127.0.0.1:{port}/health";
        var deadline = DateTime.UtcNow.AddMinutes(5);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var process = getProcess();
            if (process is { HasExited: true })
                throw new InvalidOperationException($"llama-server exited before it became ready. Exit code: {process.ExitCode}.");

            try
            {
                var r = await http.GetAsync(url, ct);
                if (r.IsSuccessStatusCode) return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }

            await Task.Delay(600, ct);
        }
        throw new TimeoutException($"llama-server on port {port} did not respond within 5 minutes");
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
        line = _redactor?.Redact(line) ?? line;
        _logRing.Enqueue($"[{DateTime.Now:HH:mm:ss}] {line}");
        while (_logRing.Count > MaxLogLines)
            _logRing.TryDequeue(out _);
        LogLine?.Invoke(line);
    }

    private string BuildErrorMessage(Exception ex)
    {
        var recent = _logRing.TakeLast(4).ToArray();
        if (recent.Length == 0) return ex.Message;

        return $"{ex.Message}\n\nRecent log:\n{string.Join('\n', recent)}";
    }
}

public sealed record ServerTuneResult(
    int GpuLayers,
    int? TotalLayers,
    string RecentLog);
