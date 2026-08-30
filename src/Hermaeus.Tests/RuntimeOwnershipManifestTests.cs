using System.Diagnostics;
using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class RuntimeOwnershipManifestTests
{
    [Fact]
    public async Task Missing_manifest_is_known_empty_state()
    {
        using var temp = new TempDir();
        var result = await new RuntimeOwnershipManifestStore(temp.PathFor("runtime-ownership.json")).ReadAsync();

        Assert.Equal(RuntimeOwnershipState.Missing, result.State);
        Assert.Empty(result.Owners);
    }

    [Fact]
    public async Task Valid_empty_manifest_is_known_empty_state()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("runtime-ownership.json");
        await File.WriteAllTextAsync(path, "[]");

        var result = await new RuntimeOwnershipManifestStore(path).ReadAsync();

        Assert.Equal(RuntimeOwnershipState.Known, result.State);
        Assert.Empty(result.Owners);
    }

    [Fact]
    public async Task Valid_populated_manifest_is_known_with_owners()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("runtime-ownership.json");
        var owner = Owner();
        await WriteJsonAsync(path, new[] { owner });

        var result = await new RuntimeOwnershipManifestStore(path).ReadAsync();

        Assert.Equal(RuntimeOwnershipState.Known, result.State);
        Assert.Equal(owner, Assert.Single(result.Owners));
    }

    [Theory]
    [InlineData("{\"ownershipId\":")]
    [InlineData("not-json")]
    public async Task Malformed_or_truncated_manifest_is_unknown(string contents)
    {
        using var temp = new TempDir();
        var path = temp.PathFor("runtime-ownership.json");
        await File.WriteAllTextAsync(path, contents);

        var result = await new RuntimeOwnershipManifestStore(path).ReadAsync();

        Assert.Equal(RuntimeOwnershipState.Unknown, result.State);
        Assert.Empty(result.Owners);
    }

    [Fact]
    public async Task Read_failure_is_unknown_without_deleting_existing_path()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("runtime-ownership.json");
        Directory.CreateDirectory(path);

        var result = await new RuntimeOwnershipManifestStore(path).ReadAsync();

        Assert.Equal(RuntimeOwnershipState.Unknown, result.State);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public async Task Unknown_manifest_refuses_add_and_remove_and_preserves_bytes()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("runtime-ownership.json");
        const string original = "{\"ownershipId\":";
        await File.WriteAllTextAsync(path, original);
        var store = new RuntimeOwnershipManifestStore(path);

        await Assert.ThrowsAsync<RuntimeOwnershipUnknownException>(() => store.AddAsync(Owner()));
        await Assert.ThrowsAsync<RuntimeOwnershipUnknownException>(() => store.RemoveAsync("owner-1"));

        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Recovery_preserves_unknown_manifest_and_does_not_terminate_processes()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var path = Path.Combine(settings.Settings.DataManagement.DataRootDirectory, "lab", "runtime-ownership.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string original = "{\"ownershipId\":";
        await File.WriteAllTextAsync(path, original);
        using var process = Process.GetCurrentProcess();
        var host = new IsolatedLabRuntimeHost(settings, new RedactionService());

        var results = await host.RecoverOwnedProcessesAsync();

        Assert.Contains(results, message => message.Contains("preserved", StringComparison.Ordinal));
        Assert.False(process.HasExited);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Recovery_keeps_normal_stale_owner_cleanup_for_known_evidence()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var path = Path.Combine(settings.Settings.DataManagement.DataRootDirectory, "lab", "runtime-ownership.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await WriteJsonAsync(path, new[] { Owner(processId: int.MaxValue) });
        var host = new IsolatedLabRuntimeHost(settings, new RedactionService());

        var results = await host.RecoverOwnedProcessesAsync();

        Assert.Contains(results, message => message.Contains("already stopped", StringComparison.Ordinal));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Direct_session_stops_owned_manager_when_manifest_becomes_unknown()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var path = Path.Combine(settings.Settings.DataManagement.DataRootDirectory, "lab", "runtime-ownership.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var host = new IsolatedLabRuntimeHost(settings, new RedactionService());
        var owner = Owner(processId: Environment.ProcessId);
        await WriteJsonAsync(path, new[] { owner });
        var stopped = false;
        var disposed = false;
        var session = new IsolatedLabRuntimeHost.Session(
            host,
            owner,
            () => stopped = true,
            () => disposed = true,
            () => ServerStatus.Running,
            () => null);

        const string original = "{\"ownershipId\":";
        await File.WriteAllTextAsync(path, original);
        await session.DisposeAsync();

        Assert.True(stopped);
        Assert.True(disposed);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Direct_session_removes_known_ownership_after_stopping_manager()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var path = Path.Combine(settings.Settings.DataManagement.DataRootDirectory, "lab", "runtime-ownership.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var owner = Owner(processId: Environment.ProcessId);
        await WriteJsonAsync(path, new[] { owner });

        var host = new IsolatedLabRuntimeHost(settings, new RedactionService());
        var stopped = false;
        var session = new IsolatedLabRuntimeHost.Session(
            host,
            owner,
            () => stopped = true,
            () => { },
            () => ServerStatus.Running,
            () => null);

        await session.StopAsync();

        Assert.True(stopped);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Direct_session_awaits_the_async_manager_stop_boundary()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var host = new IsolatedLabRuntimeHost(settings, new RedactionService());
        var owner = Owner(processId: Environment.ProcessId);
        var asyncStopCompleted = false;
        var synchronousStopCalled = false;
        var session = new IsolatedLabRuntimeHost.Session(
            host,
            owner,
            () => synchronousStopCalled = true,
            () => { },
            () => ServerStatus.Running,
            () => null,
            _ => Task.CompletedTask,
            async () =>
            {
                await Task.Yield();
                asyncStopCompleted = true;
            });

        await session.StopAsync();

        Assert.True(asyncStopCompleted);
        Assert.False(synchronousStopCalled);
    }

    [Fact]
    public async Task Direct_session_stops_manager_when_known_cleanup_fails()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var host = new IsolatedLabRuntimeHost(settings, new RedactionService());
        var owner = Owner(processId: Environment.ProcessId);
        var stopped = false;
        var session = new IsolatedLabRuntimeHost.Session(
            host,
            owner,
            () => stopped = true,
            () => { },
            () => ServerStatus.Running,
            () => null,
            _ => Task.FromException(new IOException("manifest write failed")));

        await Assert.ThrowsAsync<IOException>(() => session.StopAsync());

        Assert.True(stopped);
    }

    [Fact]
    public async Task Direct_session_stops_manager_when_cleanup_is_cancelled()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var host = new IsolatedLabRuntimeHost(settings, new RedactionService());
        var owner = Owner(processId: Environment.ProcessId);
        var stopped = false;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var session = new IsolatedLabRuntimeHost.Session(
            host,
            owner,
            () => stopped = true,
            () => { },
            () => ServerStatus.Running,
            () => null,
            ct => Task.FromCanceled(ct));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.StopAsync(cts.Token));

        Assert.True(stopped);
    }

    [Fact]
    public async Task Dispose_disposes_manager_when_ownership_cleanup_fails()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var host = new IsolatedLabRuntimeHost(settings, new RedactionService());
        var owner = Owner(processId: Environment.ProcessId);
        var stopped = false;
        var disposed = false;
        var session = new IsolatedLabRuntimeHost.Session(
            host,
            owner,
            () => stopped = true,
            () => disposed = true,
            () => ServerStatus.Running,
            () => null,
            _ => Task.FromException(new IOException("manifest write failed")));

        await Assert.ThrowsAsync<IOException>(() => session.DisposeAsync().AsTask());

        Assert.True(stopped);
        Assert.True(disposed);
    }

    [Fact]
    public async Task Repeated_stop_and_dispose_are_idempotent()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var host = new IsolatedLabRuntimeHost(settings, new RedactionService());
        var owner = Owner(processId: Environment.ProcessId);
        var stopCount = 0;
        var disposeCount = 0;
        var session = new IsolatedLabRuntimeHost.Session(
            host,
            owner,
            () => stopCount++,
            () => disposeCount++,
            () => ServerStatus.Running,
            () => null,
            _ => Task.CompletedTask);

        await session.StopAsync();
        await session.StopAsync();
        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(1, stopCount);
        Assert.Equal(1, disposeCount);
    }

    private static RuntimeOwner Owner(int processId = int.MaxValue) => new(
        "owner-1", "run-1", processId, DateTime.UtcNow.AddMinutes(-1),
        new string('a', 64), 39201, DateTime.UtcNow);

    private static Task WriteJsonAsync(string path, IReadOnlyCollection<RuntimeOwner> owners) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(owners, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}
