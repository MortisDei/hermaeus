using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services.ProcessManagement;

namespace Hermaeus.Services;

public sealed class IsolatedLabRuntimeHost : ILabRuntimeHost
{
    private readonly ISettingsService _settings;
    private readonly RedactionService _redaction;
    private readonly SemaphoreSlim _manifestGate = new(1, 1);
    private readonly IRuntimeLogService? _runtimeLogs;

    public IsolatedLabRuntimeHost(ISettingsService settings, RedactionService redaction, IRuntimeLogService? runtimeLogs = null)
    {
        _settings = settings;
        _redaction = redaction;
        _runtimeLogs = runtimeLogs;
    }

    private string ManifestPath => Path.Combine(SettingsService.ResolveDataRoot(_settings.Settings), "lab", "runtime-ownership.json");

    public async Task<ILabRuntimeSession> StartAsync(string runId, ServerConfig source,
        LabConfiguration configuration, CancellationToken ct = default)
    {
        LabDefinitionValidator.ValidateConfiguration(configuration, source.ExtraArgs);
        LabDefinitionValidator.ValidateIsolationArguments(source.ExtraArgs);
        var port = ReserveLoopbackPort();
        var manager = new ServerProcessManager(_redaction);
        var isolated = LabConfigurationMapper.Apply(source, configuration, port);
        try
        {
            await manager.StartAsync(isolated, ct);
            if (manager.Status != ServerStatus.Running || manager.CurrentProcessIdentity is not { } process)
            {
                var error = string.IsNullOrWhiteSpace(manager.ErrorMessage)
                    ? "The isolated runtime did not reach Running state." : manager.ErrorMessage;
                throw new InvalidOperationException(error);
            }

            var ownershipId = Guid.NewGuid().ToString("N");
            var runtimeIdentity = await RuntimeIdentityFactory.CreateRuntimeIdentityAsync(source.ExecutablePath, null, ct);
            var owner = new RuntimeOwner(ownershipId, runId, process.ProcessId, process.StartedAtUtc,
                runtimeIdentity.ExecutableSha256, port, DateTime.UtcNow);
            await AddOwnerAsync(owner, ct);
            return new Session(this, owner, manager.Stop, manager.Dispose,
                () => manager.Status, () => manager.CurrentProcessIdentity,
                stopManagerAsync: manager.StopAsync);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                RecordLaunchFailure(manager, ex);

            try { await manager.StopAsync(); }
            catch (Exception stopException)
            {
                try
                {
                    RecordLaunchFailure(manager, stopException);
                }
                catch
                {
                    // Preserve the original launch failure even if cleanup logging fails.
                }
            }
            finally
            {
                manager.Dispose();
            }
            throw;
        }
    }

    private void RecordLaunchFailure(ServerProcessManager manager, Exception exception)
    {
        if (_runtimeLogs is null)
            return;

        var detail = string.Join(" ", new[] { exception.Message, manager.ErrorMessage, manager.GetLog() }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        detail = _redaction.Redact(detail);
        if (detail.Length > 2048)
            detail = detail[..2048] + "...";
        _runtimeLogs.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            RuntimeLogLevel.Error,
            RuntimeLogCategory.ModelLoad,
            $"Lab isolated runtime launch failed: {detail}"));
    }

    public async Task<IReadOnlyList<string>> RecoverOwnedProcessesAsync(CancellationToken ct = default)
    {
        await _manifestGate.WaitAsync(ct);
        try
        {
            var store = CreateOwnershipStore();
            var read = await store.ReadAsync(ct);
            if (read.State == RuntimeOwnershipState.Unknown)
            {
                return ["Lab runtime ownership evidence could not be read; recovery was skipped and the existing manifest was preserved."];
            }

            var owners = read.Owners.ToList();
            var unresolved = new List<RuntimeOwner>();
            var results = new List<string>();
            foreach (var owner in owners)
            {
                ct.ThrowIfCancellationRequested();
                var process = TryGetProcess(owner.ProcessId);
                if (process is null)
                {
                    results.Add($"Recovered stale Lab ownership {owner.OwnershipId}; process was already stopped.");
                    continue;
                }
                using (process)
                {
                    if (!MatchesStart(process, owner.StartedAtUtc) || !await MatchesExecutableAsync(process, owner.ExecutableSha256, ct))
                    {
                        unresolved.Add(owner);
                        results.Add($"Lab ownership {owner.OwnershipId} remains Unknown because exact process identity could not be verified.");
                        continue;
                    }
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(ct);
                        results.Add($"Stopped recovered Lab runtime ownership {owner.OwnershipId}.");
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        unresolved.Add(owner);
                        results.Add($"Could not stop recovered Lab ownership {owner.OwnershipId}: {_redaction.Redact(ex.Message)}");
                    }
                }
            }
            await store.WriteAsync(unresolved, ct);
            return results;
        }
        finally
        {
            _manifestGate.Release();
        }
    }

    private async Task AddOwnerAsync(RuntimeOwner owner, CancellationToken ct)
    {
        await _manifestGate.WaitAsync(ct);
        try
        {
            await CreateOwnershipStore().AddAsync(owner, ct);
        }
        finally
        {
            _manifestGate.Release();
        }
    }

    private async Task RemoveOwnerAsync(string ownershipId, CancellationToken ct)
    {
        await _manifestGate.WaitAsync(ct);
        try
        {
            await CreateOwnershipStore().RemoveAsync(ownershipId, ct);
        }
        finally
        {
            _manifestGate.Release();
        }
    }

    private RuntimeOwnershipManifestStore CreateOwnershipStore() =>
        new(ManifestPath, () => _runtimeLogs?.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            RuntimeLogLevel.Warning,
            RuntimeLogCategory.Service,
            "Lab runtime ownership evidence is unreadable; ownership mutations are blocked until it can be read.")));

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static Process? TryGetProcess(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (process.HasExited) { process.Dispose(); return null; }
            return process;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static bool MatchesStart(Process process, DateTime expected)
    {
        try { return Math.Abs((process.StartTime.ToUniversalTime() - expected.ToUniversalTime()).TotalSeconds) < 1; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    private static async Task<bool> MatchesExecutableAsync(Process process, string expectedSha256, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256)) return false;
        try
        {
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
            return string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    internal sealed class Session(
        IsolatedLabRuntimeHost owner,
        RuntimeOwner record,
        Action stopManager,
        Action disposeManager,
        Func<ServerStatus> getStatus,
        Func<ManagedRuntimeProcessIdentity?> getProcess,
        Func<CancellationToken, Task>? removeOwnership = null,
        Func<Task>? stopManagerAsync = null) : ILabRuntimeSession
    {
        private int _stopped;
        private int _disposed;
        private readonly Func<CancellationToken, Task> _removeOwnership =
            removeOwnership ?? (ct => owner.RemoveOwnerAsync(record.OwnershipId, ct));
        private readonly Func<Task>? _stopManagerAsync = stopManagerAsync;
        public string OwnershipId => record.OwnershipId;
        public int Port => record.Port;
        public bool IsRunning => getStatus() == ServerStatus.Running;
        public ManagedProcessReference? Process => getProcess() is { } process
            ? new ManagedProcessReference(process.ProcessId, process.StartedAtUtc) : null;

        public async Task StopAsync(CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
            try
            {
                await _removeOwnership(ct);
            }
            catch (RuntimeOwnershipUnknownException)
            {
                // Recovery must remain fail-closed, but this session directly
                // owns the manager it created. Stop that known child while
                // preserving the unreadable manifest for later reconciliation.
            }
            finally
            {
                if (_stopManagerAsync is not null)
                    await _stopManagerAsync();
                else
                    stopManager();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try
            {
                await StopAsync();
            }
            finally
            {
                disposeManager();
            }
        }
    }

}
