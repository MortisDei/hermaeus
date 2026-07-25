using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r19 2.2: the llama.cpp update flow must stop running llama-server(s)
/// before updating and restart exactly that set afterward (or on failure,
/// restart them unchanged) rather than leaving them on the old build. Doctor
/// has no server-process knowledge of its own, so this is exercised through
/// its two bridging hooks rather than a live ServicesViewModel.
/// </summary>
public sealed class DoctorViewModelUpdateStopRestartTests
{
    private sealed class ScriptedDetailedUpdateDoctorService(bool succeeds) : IDoctorService
    {
        public Task<DoctorReport> ScanAsync(CancellationToken ct = default) =>
            Task.FromResult(new DoctorReport([], DateTime.UtcNow, "ok"));
        public Task<bool> InstallRerankerAssetsAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallRerankerAssetsAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallEmbeddingModelAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallEmbeddingModelAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallLlamaServerUpdateAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallLlamaServerUpdateAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallNativeKokoroAssetsAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallNativeKokoroAssetsAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);

        public Task<LlamaUpdateOutcome> InstallLlamaServerUpdateDetailedAsync(IProgress<string>? progress, CancellationToken ct = default) =>
            Task.FromResult(succeeds
                ? LlamaUpdateOutcome.Ok("/new/llama-server", "/new", [])
                : LlamaUpdateOutcome.Failed("boom"));
    }

    private static readonly DoctorCheck UpdateCheck = new(
        "llama-server-update", "llama.cpp update available", DoctorCheckStatus.Info,
        "An update is available", "detail", "Update", true, string.Empty, "Runtime");

    [Fact]
    public async Task Successful_update_stops_the_running_server_once_and_restarts_only_it()
    {
        using var temp = new TempDir();
        var vm = new DoctorViewModel(new ScriptedDetailedUpdateDoctorService(succeeds: true), new FakeToasts(), NewSettings(temp));

        var stopCalls = 0;
        var restarted = new List<IReadOnlyList<string>>();
        vm.RequestStopRunningLlamaServersForUpdate = () => { stopCalls++; return ["server-a"]; };
        vm.RequestRestartServers = ids => { restarted.Add(ids); return Task.CompletedTask; };

        await vm.RunFixCommand.ExecuteAsync(UpdateCheck);

        Assert.Equal(1, stopCalls);
        var restart = Assert.Single(restarted);
        Assert.Equal(["server-a"], restart);
    }

    [Fact]
    public async Task Failed_update_still_restarts_the_stopped_server()
    {
        using var temp = new TempDir();
        var vm = new DoctorViewModel(new ScriptedDetailedUpdateDoctorService(succeeds: false), new FakeToasts(), NewSettings(temp));

        var restarted = new List<IReadOnlyList<string>>();
        vm.RequestStopRunningLlamaServersForUpdate = () => ["server-a"];
        vm.RequestRestartServers = ids => { restarted.Add(ids); return Task.CompletedTask; };

        await vm.RunFixCommand.ExecuteAsync(UpdateCheck);

        var restart = Assert.Single(restarted);
        Assert.Equal(["server-a"], restart);
    }

    [Fact]
    public async Task Update_with_no_running_servers_never_calls_restart()
    {
        using var temp = new TempDir();
        var vm = new DoctorViewModel(new ScriptedDetailedUpdateDoctorService(succeeds: true), new FakeToasts(), NewSettings(temp));

        var restartCalled = false;
        vm.RequestStopRunningLlamaServersForUpdate = () => [];
        vm.RequestRestartServers = ids => { restartCalled = true; return Task.CompletedTask; };

        await vm.RunFixCommand.ExecuteAsync(UpdateCheck);

        Assert.False(restartCalled, "Restart must not be invoked when nothing was stopped.");
    }

    /// <summary>
    /// A real bug report: updating llama.cpp while every managed server was
    /// already stopped left the Services page showing the pre-update path
    /// until the app was restarted. InstallLlamaServerUpdateDetailedAsync
    /// rewrites every server's ExecutablePath unconditionally, but the old
    /// flow only ever re-synced servers named in the (here, empty) stopped
    /// list, so the Services rows never learned about the change.
    /// </summary>
    [Fact]
    public async Task Successful_update_syncs_server_paths_even_when_nothing_was_running()
    {
        using var temp = new TempDir();
        var vm = new DoctorViewModel(new ScriptedDetailedUpdateDoctorService(succeeds: true), new FakeToasts(), NewSettings(temp));

        var syncCalls = 0;
        vm.RequestStopRunningLlamaServersForUpdate = () => [];
        vm.RequestRestartServers = _ => Task.CompletedTask;
        vm.RequestSyncServerExecutablePaths = () => syncCalls++;

        await vm.RunFixCommand.ExecuteAsync(UpdateCheck);

        Assert.Equal(1, syncCalls);
    }

    [Fact]
    public async Task Failed_update_does_not_sync_server_paths()
    {
        using var temp = new TempDir();
        var vm = new DoctorViewModel(new ScriptedDetailedUpdateDoctorService(succeeds: false), new FakeToasts(), NewSettings(temp));

        var syncCalls = 0;
        vm.RequestStopRunningLlamaServersForUpdate = () => ["server-a"];
        vm.RequestRestartServers = _ => Task.CompletedTask;
        vm.RequestSyncServerExecutablePaths = () => syncCalls++;

        await vm.RunFixCommand.ExecuteAsync(UpdateCheck);

        Assert.Equal(0, syncCalls);
    }
}
