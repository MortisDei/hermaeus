using System.Net;
using System.Net.Sockets;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class LocalApiProcessManagerBoundaryTests
{
    [Theory]
    [InlineData(true, 137, "Stopped")]
    [InlineData(false, 137, "Error (Local API exited with code 137)")]
    [InlineData(false, 0, "Stopped")]
    public void Process_exit_status_distinguishes_intentional_stop_from_crash(
        bool stopRequested, int code, string expected) =>
        Assert.Equal(expected, LocalApiProcessManager.GetProcessExitStatus(stopRequested, code));

    private sealed class BoundarySecretStore : ISecretStore
    {
        public bool IsReference(string value) => false;
        public Task<string> StoreAsync(string name, string secret, CancellationToken ct = default) => Task.FromResult(secret);
        public Task<string> ResolveAsync(string valueOrReference, CancellationToken ct = default) => Task.FromResult(valueOrReference);
        public Task<string> BackendLabelAsync(CancellationToken ct = default) => Task.FromResult("test");
    }

    [Fact]
    public async Task Token_changes_and_port_changes_restart_the_real_local_api_child()
    {
        using var temp = new TempDir();
        var settingsPath = temp.PathFor("settings/settings.json");
        var dataRoot = temp.PathFor("data");
        var oldToken = "old-local-api-token-0123456789";
        var newToken = "new-local-api-token-0123456789";
        var firstPort = ReservePort();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = dataRoot;
        settings.Settings.LocalApi.Enabled = true;
        settings.Settings.LocalApi.Port = firstPort;
        var oldEntry = new LocalApiTokenEntry { Name = "old", SecretRef = oldToken };
        settings.Settings.LocalApi.Tokens.Add(oldEntry);
        await settings.SaveAsync();

        var apiDll = FindLocalApiDll();
        var launchCount = 0;

        (string? FileName, IReadOnlyList<string> Args) ResolveTarget()
        {
            launchCount++;
            return ("dotnet", [apiDll]);
        }

        using var manager = new LocalApiProcessManager(
            launchTargetResolver: ResolveTarget,
            settingsPathResolver: () => settingsPath);
        await manager.EnsureRunningStateAsync(settings.Settings);

        using var client = new HttpClient();
        Assert.Equal(HttpStatusCode.OK, await GetModelsAsync(client, firstPort, oldToken));

        await manager.EnsureRunningStateAsync(settings.Settings);
        Assert.Equal(1, launchCount);

        var tokenVm = new LocalApiSettingsViewModel(new BoundarySecretStore(), settings,
            () => manager.EnsureRunningStateAsync(settings.Settings));
        tokenVm.ReloadFrom(settings.Settings);
        tokenVm.NewTokenName = "new";
        tokenVm.NewTokenValue = newToken;
        await tokenVm.AddTokenCommand.ExecuteAsync(null);

        Assert.Equal(2, launchCount);
        Assert.Equal(HttpStatusCode.OK, await GetModelsAsync(client, firstPort, newToken));

        tokenVm.ReloadFrom(settings.Settings);
        await tokenVm.RevokeTokenCommand.ExecuteAsync(tokenVm.Tokens.Single(row => row.Id == oldEntry.Id));

        Assert.Equal(3, launchCount);
        Assert.Equal(HttpStatusCode.Unauthorized, await GetModelsAsync(client, firstPort, oldToken));
        Assert.Equal(HttpStatusCode.OK, await GetModelsAsync(client, firstPort, newToken));

        var secondPort = ReservePort();
        settings.Settings.LocalApi.Port = secondPort;
        await settings.SaveAsync();
        await manager.EnsureRunningStateAsync(settings.Settings);

        Assert.Equal(4, launchCount);
        Assert.Equal(HttpStatusCode.OK, await GetModelsAsync(client, secondPort, newToken));
        await Assert.ThrowsAsync<HttpRequestException>(() => GetModelsAsync(client, firstPort, newToken));
    }

    private static async Task<HttpStatusCode> GetModelsAsync(HttpClient client, int port, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/v1/models");
        request.Headers.Add("X-Hermaeus-Token", token);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindLocalApiDll()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "Hermaeus.LocalApi.dll");
        Assert.True(File.Exists(dll), $"Local API test child was not built: {dll}");
        return dll;
    }
}
