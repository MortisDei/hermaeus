using System.Diagnostics;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services.ProcessManagement;

/// <summary>
/// Starts and stops the optional <c>Hermaeus.LocalApi</c> host as a child
/// process. This is what makes <see cref="LocalApiSettings.Enabled"/> real:
/// previously nothing launched the host, so the Settings checkbox did
/// nothing (docs/review/01-code-audit.md P1-1). Follows the same
/// start/health-poll/kill pattern as <see cref="ServerProcessManager"/> and
/// <see cref="KokoroProcessManager"/>.
/// </summary>
public sealed class LocalApiProcessManager : IDisposable
{
    private Process? _process;
    private LocalApiRuntimeConfiguration? _activeConfiguration;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly IProcessJobObject _jobObject;
    private readonly IRuntimeLogService? _runtimeLogs;
    private readonly Func<(string? FileName, IReadOnlyList<string> Args)> _resolveLaunchTarget;
    private readonly Func<string?> _settingsPathResolver;
    private bool _stopRequested;

    private sealed record LocalApiRuntimeConfiguration(int Port, IReadOnlyList<(string Id, string SecretRef)> Tokens);

    public bool IsRunning => _process is { HasExited: false };
    public string StatusLabel { get; private set; } = "Stopped";
    public event Action? StatusChanged;

    public LocalApiProcessManager(
        IProcessJobObject? jobObject = null,
        IRuntimeLogService? runtimeLogs = null,
        Func<(string? FileName, IReadOnlyList<string> Args)>? launchTargetResolver = null,
        Func<string?>? settingsPathResolver = null)
    {
        _jobObject = jobObject ?? ProcessJobObject.Default;
        _runtimeLogs = runtimeLogs;
        _resolveLaunchTarget = launchTargetResolver ?? (() => ResolveLaunchTarget());
        _settingsPathResolver = settingsPathResolver ?? (() => null);
    }

    public async Task StartAsync(AppSettings settings, CancellationToken ct = default)
    {
        await _operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StartCoreAsync(settings, ct).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task EnsureRunningStateAsync(AppSettings settings, CancellationToken ct = default)
    {
        await _operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!settings.LocalApi.Enabled)
            {
                await StopCoreAsync().ConfigureAwait(false);
                return;
            }

            var requested = GetRuntimeConfiguration(settings);
            if (IsRunning && RuntimeConfigurationEquals(_activeConfiguration, requested))
                return;

            if (IsRunning)
                await StopCoreAsync().ConfigureAwait(false);
            await StartCoreAsync(settings, ct).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task StartCoreAsync(AppSettings settings, CancellationToken ct)
    {
        if (IsRunning) return;
        if (_process is not null)
        {
            _process.Dispose();
            _process = null;
            _activeConfiguration = null;
        }
        Volatile.Write(ref _stopRequested, false);
        if (!settings.LocalApi.Enabled) return;

        if (settings.LocalApi.Tokens.Count == 0)
        {
            SetStatus("Stopped (no token configured; generate one in Settings)");
            return;
        }

        var (fileName, args) = _resolveLaunchTarget();
        if (fileName is null)
        {
            SetStatus("Stopped (Hermaeus.LocalApi executable not found)");
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
        var settingsPath = _settingsPathResolver();
        if (!string.IsNullOrWhiteSpace(settingsPath))
        {
            psi.ArgumentList.Add("--settings-path");
            psi.ArgumentList.Add(Path.GetFullPath(settingsPath));
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process = process;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.Exited += OnProcessExited;

        try
        {
            if (!_process.Start())
                throw new InvalidOperationException("Failed to start Hermaeus.LocalApi.");

            // r11 4.1: unlike ServerProcessManager/KokoroProcessManager/XttsProcessManager,
            // this manager never joined the app's job object, so an app crash
            // orphaned the LocalApi process holding its port and per-app tokens
            // alive in memory (the r9 2.1 orphan class, missed for this manager).
            if (OperatingSystem.IsWindows() && !_jobObject.TryAssign(_process))
                _runtimeLogs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Service,
                    "Could not attach Hermaeus.LocalApi process to the app's job object; it may survive an abnormal app exit."));

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            var port = settings.LocalApi.Port is > 0 and <= 65535 ? settings.LocalApi.Port : 39300;
            await WaitForHealthAsync(process, port, ct).ConfigureAwait(false);
            _activeConfiguration = GetRuntimeConfiguration(settings);
            SetStatus("Running");
        }
        catch (Exception ex)
        {
            try
            {
                await StopCoreAsync().ConfigureAwait(false);
            }
            catch (Exception stopException)
            {
                throw new AggregateException(ex, stopException);
            }
            throw;
        }
    }

    public void Stop()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch { }
    }

    private async Task StopCoreAsync()
    {
        Volatile.Write(ref _stopRequested, true);
        var process = _process;
        if (process is null)
        {
            _activeConfiguration = null;
            SetStatus("Stopped");
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            _process = null;
            _activeConfiguration = null;
            process.Dispose();
            SetStatus("Stopped");
        }
        catch
        {
            SetStatus("Stop failed");
            throw;
        }
    }

    private static LocalApiRuntimeConfiguration GetRuntimeConfiguration(AppSettings settings) =>
        new(settings.LocalApi.Port is > 0 and <= 65535 ? settings.LocalApi.Port : 39300,
            settings.LocalApi.Tokens
                .Select(token => (token.Id, token.SecretRef))
                .OrderBy(token => token.Id, StringComparer.Ordinal)
                .ThenBy(token => token.SecretRef, StringComparer.Ordinal)
                .ToArray());

    private static bool RuntimeConfigurationEquals(
        LocalApiRuntimeConfiguration? active,
        LocalApiRuntimeConfiguration requested) =>
        active is not null
        && active.Port == requested.Port
        && active.Tokens.SequenceEqual(requested.Tokens);

    private void SetStatus(string label)
    {
        StatusLabel = label;
        StatusChanged?.Invoke();
    }

    private static async Task WaitForHealthAsync(Process process, int port, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var url = $"http://127.0.0.1:{port}/health";
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new InvalidOperationException($"Hermaeus.LocalApi exited before it became healthy. Exit code: {TryGetExitCode(process)}.");
            try
            {
                using var response = await http.GetAsync(url, ct);
                if (response.IsSuccessStatusCode) return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }

            await Task.Delay(300, ct);
        }

        throw new TimeoutException("Hermaeus.LocalApi did not become healthy within 30 seconds.");
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process exited || !ReferenceEquals(_process, exited))
            return;

        _activeConfiguration = null;
        SetStatus(GetProcessExitStatus(Volatile.Read(ref _stopRequested), TryGetExitCode(exited)));
    }

    internal static string GetProcessExitStatus(bool stopRequested, int code) =>
        stopRequested || code == 0 ? "Stopped" : $"Error (Local API exited with code {code})";

    private static int TryGetExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch (ObjectDisposedException) { return -1; }
        catch (InvalidOperationException) { return -1; }
    }

    /// <summary>
    /// Prefers a packaged sibling install (<c>LocalApi/Hermaeus.LocalApi(.exe)</c>
    /// next to the running Desktop executable, as laid out by build.ps1/build.sh).
    /// Falls back to the sibling project's own build output for `dotnet run`/F5
    /// development, located by walking up from the running executable to find
    /// Hermaeus.sln and mirroring Hermaeus.Desktop's bin path onto Hermaeus.LocalApi.
    /// </summary>
    public static (string? FileName, IReadOnlyList<string> Args) ResolveLaunchTarget(string? baseDirOverride = null)
    {
        var baseDir = baseDirOverride ?? AppContext.BaseDirectory;
        var packaged = Path.Combine(baseDir, "LocalApi", OperatingSystem.IsWindows() ? "Hermaeus.LocalApi.exe" : "Hermaeus.LocalApi");
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
            if (!File.Exists(Path.Combine(current.FullName, "Hermaeus.sln")))
                continue;

            var desktopBinRoot = Path.Combine(current.FullName, "src", "Hermaeus.Desktop", "bin");
            var relative = Path.GetRelativePath(desktopBinRoot, baseDir);
            var dll = Path.Combine(current.FullName, "src", "Hermaeus.LocalApi", "bin", relative, "Hermaeus.LocalApi.dll");
            return File.Exists(dll) ? dll : null;
        }

        return null;
    }

    public void Dispose() => Stop();
}
