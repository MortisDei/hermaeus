using System.Net;
using System.Net.Sockets;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

public sealed class LocalApiProcessManagerBoundaryTests
{
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
        var settings = new SettingsService(settingsPath);
        settings.Settings.DataManagement.DataRootDirectory = dataRoot;
        settings.Settings.LocalApi.Enabled = true;
        settings.Settings.LocalApi.Port = firstPort;
        var oldEntry = new LocalApiTokenEntry { Name = "old", SecretRef = oldToken };
        settings.Settings.LocalApi.Tokens.Add(oldEntry);
        await settings.SaveAsync();

        var apiDll = FindLocalApiDll();
        using var manager = new LocalApiProcessManager(
            launchTargetResolver: () => ("dotnet", [apiDll]),
            settingsPathResolver: () => settingsPath);
        await manager.EnsureRunningStateAsync(settings.Settings);

        using var client = new HttpClient();
        Assert.Equal(HttpStatusCode.OK, await GetModelsAsync(client, firstPort, oldToken));

        var tokenVm = new LocalApiSettingsViewModel(new BoundarySecretStore(), settings,
            () => manager.EnsureRunningStateAsync(settings.Settings));
        tokenVm.ReloadFrom(settings.Settings);
        tokenVm.NewTokenName = "new";
        tokenVm.NewTokenValue = newToken;
        await tokenVm.AddTokenCommand.ExecuteAsync(null);

        Assert.Equal(HttpStatusCode.OK, await GetModelsAsync(client, firstPort, newToken));

        tokenVm.ReloadFrom(settings.Settings);
        await tokenVm.RevokeTokenCommand.ExecuteAsync(tokenVm.Tokens.Single(row => row.Id == oldEntry.Id));

        Assert.Equal(HttpStatusCode.Unauthorized, await GetModelsAsync(client, firstPort, oldToken));
        Assert.Equal(HttpStatusCode.OK, await GetModelsAsync(client, firstPort, newToken));

        var secondPort = ReservePort();
        settings.Settings.LocalApi.Port = secondPort;
        await settings.SaveAsync();
        await manager.EnsureRunningStateAsync(settings.Settings);

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
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Hermaeus.sln")))
            current = current.Parent;
        Assert.NotNull(current);
        var dll = Path.Combine(current!.FullName, "src", "Hermaeus.LocalApi", "bin", "Debug", "net10.0", "Hermaeus.LocalApi.dll");
        Assert.True(File.Exists(dll), $"Local API test child was not built: {dll}");
        return dll;
    }
}
