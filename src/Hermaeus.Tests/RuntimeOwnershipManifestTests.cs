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

    private static RuntimeOwner Owner(int processId = int.MaxValue) => new(
        "owner-1", "run-1", processId, DateTime.UtcNow.AddMinutes(-1),
        new string('a', 64), 39201, DateTime.UtcNow);

    private static Task WriteJsonAsync(string path, IReadOnlyCollection<RuntimeOwner> owners) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(owners, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}
