using System.Diagnostics;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services.ProcessManagement;

/// <summary>
/// Starts and stops the optional <c>Aether.LocalApi</c> host as a child
/// process. This is what makes <see cref="LocalApiSettings.Enabled"/> real:
/// previously nothing launched the host, so the Settings checkbox did
/// nothing (docs/review/01-code-audit.md P1-1). Follows the same
/// start/health-poll/kill pattern as <see cref="ServerProcessManager"/> and
/// <see cref="KokoroProcessManager"/>.
/// </summary>
public sealed class LocalApiProcessManager : IDisposable
{
    private Process? _process;
    private readonly IProcessJobObject _jobObject;
    private readonly IRuntimeLogService? _runtimeLogs;
    private readonly Func<(string? FileName, IReadOnlyList<string> Args)> _resolveLaunchTarget;

    public bool IsRunning => _process is { HasExited: false };
    public string StatusLabel { get; private set; } = "Stopped";
    public event Action? StatusChanged;

    public LocalApiProcessManager(
        IProcessJobObject? jobObject = null,
        IRuntimeLogService? runtimeLogs = null,
        Func<(string? FileName, IReadOnlyList<string> Args)>? launchTargetResolver = null)
    {
        _jobObject = jobObject ?? ProcessJobObject.Default;
        _runtimeLogs = runtimeLogs;
        _resolveLaunchTarget = launchTargetResolver ?? (() => ResolveLaunchTarget());
    }

    public async Task StartAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (IsRunning) return;
        if (!settings.LocalApi.Enabled) return;

        if (settings.LocalApi.Tokens.Count == 0)
        {
            SetStatus("Stopped (no token configured; generate one in Settings)");
            return;
        }

        var (fileName, args) = _resolveLaunchTarget();
        if (fileName is null)
        {
            SetStatus("Stopped (Aether.LocalApi executable not found)");
            return;
        }

        SetStatus("Starting");

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, _) => { };
        _process.ErrorDataReceived += (_, _) => { };
        _process.Exited += (_, _) => SetStatus("Stopped");

        try
        {
            if (!_process.Start())
                throw new InvalidOperationException("Failed to start Aether.LocalApi.");

            // r11 4.1: unlike ServerProcessManager/KokoroProcessManager/XttsProcessManager,
            // this manager never joined the app's job object, so an app crash
            // orphaned the LocalApi process holding its port and per-app tokens
            // alive in memory (the r9 2.1 orphan class, missed for this manager).
            if (OperatingSystem.IsWindows() && !_jobObject.TryAssign(_process))
                _runtimeLogs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Service,
                    "Could not attach Aether.LocalApi process to the app's job object; it may survive an abnormal app exit."));

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            var port = settings.LocalApi.Port is > 0 and <= 65535 ? settings.LocalApi.Port : 39300;
            await WaitForHealthAsync(port, ct);
            SetStatus("Running");
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch { }

        _process?.Dispose();
        _process = null;
        SetStatus("Stopped");
    }

    private void SetStatus(string label)
    {
        StatusLabel = label;
        StatusChanged?.Invoke();
    }

    private static async Task WaitForHealthAsync(int port, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var url = $"http://127.0.0.1:{port}/health";
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var response = await http.GetAsync(url, ct);
                if (response.IsSuccessStatusCode) return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }

            await Task.Delay(300, ct);
        }

        throw new TimeoutException("Aether.LocalApi did not become healthy within 30 seconds.");
    }

    /// <summary>
    /// Prefers a packaged sibling install (<c>LocalApi/Aether.LocalApi(.exe)</c>
    /// next to the running Desktop executable, as laid out by build.ps1/build.sh).
    /// Falls back to the sibling project's own build output for `dotnet run`/F5
    /// development, located by walking up from the running executable to find
    /// Aether.sln and mirroring Aether.Desktop's bin path onto Aether.LocalApi.
    /// </summary>
    public static (string? FileName, IReadOnlyList<string> Args) ResolveLaunchTarget(string? baseDirOverride = null)
    {
        var baseDir = baseDirOverride ?? AppContext.BaseDirectory;
        var packaged = Path.Combine(baseDir, "LocalApi", OperatingSystem.IsWindows() ? "Aether.LocalApi.exe" : "Aether.LocalApi");
        if (File.Exists(packaged))
            return (packaged, []);

        var devDll = ResolveDevBuildDll(baseDir);
        return devDll is null ? (null, []) : ("dotnet", [devDll]);
    }

    public static string? ResolveDevBuildDll(string baseDir)
    {
        var current = new DirectoryInfo(baseDir);
        for (var i = 0; i < 10 && current is not null; i++, current = current.Parent)
        {
            if (!File.Exists(Path.Combine(current.FullName, "Aether.sln")))
                continue;

            var desktopBinRoot = Path.Combine(current.FullName, "src", "Aether.Desktop", "bin");
            var relative = Path.GetRelativePath(desktopBinRoot, baseDir);
            var dll = Path.Combine(current.FullName, "src", "Aether.LocalApi", "bin", relative, "Aether.LocalApi.dll");
            return File.Exists(dll) ? dll : null;
        }

        return null;
    }

    public void Dispose() => Stop();
}
