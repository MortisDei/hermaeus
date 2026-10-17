using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.LocalApi;
using Hermaeus.Rag;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class LocalApiRouteBoundaryTests
{
    private const string Token = "synthetic-route-boundary-token";

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong")]
    [InlineData("valid")]
    [InlineData("revoked")]
    [InlineData("unconfigured")]
    public async Task Actual_routes_keep_health_liveness_separate_from_token_and_method_checks(string authentication)
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        await settings.LoadAsync();
        var token = new LocalApiTokenEntry { Name = "verified-client", SecretRef = Token };
        if (authentication != "unconfigured") settings.Settings.LocalApi.Tokens.Add(token);
        var memories = new MemoryStore(settings);
        await memories.InitializeAsync();
        var store = new SqliteRagStore(settings);
        await store.InitializeAsync();
        var traces = new SqliteTraceStore(settings);
        var rag = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
        using var host = await new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ISettingsService>(settings);
                services.AddSingleton<ISecretStore, PassthroughSecrets>();
                services.AddSingleton<ILlmService>(new FakeLlm());
                services.AddSingleton<IMemoryStore>(memories);
                services.AddSingleton(rag);
                services.AddSingleton<ITraceStore>(traces);
                services.AddSingleton(new ModelProfileService(settings));
                services.AddSingleton<IEmbeddingService, FakeEmbeddingService>();
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseLocalApiTokenAuth();
                app.UseEndpoints(endpoints => endpoints.MapLocalApiEndpoints());
            });
        }).StartAsync();
        using var client = host.GetTestClient();
        if (authentication == "revoked")
        {
            settings.Settings.LocalApi.Tokens.Remove(token);
            settings.Settings.LocalApi.Tokens.Add(new LocalApiTokenEntry { Name = "retained-client", SecretRef = "synthetic-other-token" });
        }
        if (authentication != "missing") client.DefaultRequestHeaders.Add(LocalApiTokenAuth.TokenHeaderName,
            authentication == "wrong" ? "synthetic-wrong-token" : Token);
        client.DefaultRequestHeaders.Add(LocalApiEndpoints.ClientHeaderName, "spoofed-owner");

        foreach (var path in new[] { "/health", "/HEALTH" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("ok", await response.Content.ReadFromJsonAsync<string>());
        }
        // The middleware recognises health path segments, but there are no
        // descendant routes. Prefix exemption alone must not expose API data.
        foreach (var path in new[] { "/health/v1/models", "/health/agent/tasks", "/HEALTH/settings" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using (var response = await client.PostAsync("/health", null))
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        var refused = authentication == "unconfigured" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Unauthorized;
        foreach (var (method, path, authenticatedStatus) in new[]
        {
            ("GET", "/healthx", HttpStatusCode.NotFound),
            ("GET", "/v1/health", HttpStatusCode.NotFound),
            ("GET", "/v1/agent/tasks", HttpStatusCode.NotFound),
            ("POST", "/v1/agent/tasks", HttpStatusCode.NotFound),
            ("POST", "/v1/agent/tasks/task-a/start", HttpStatusCode.NotFound),
            ("DELETE", "/v1/agent/tasks/task-a", HttpStatusCode.NotFound),
            ("POST", "/v1/models", HttpStatusCode.MethodNotAllowed),
            ("DELETE", "/v1/models", HttpStatusCode.MethodNotAllowed),
            ("OPTIONS", "/v1/models", HttpStatusCode.MethodNotAllowed),
            ("GET", "/v1/chat/completions", HttpStatusCode.MethodNotAllowed)
        })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            using var response = await client.SendAsync(request);
            Assert.True(response.StatusCode == (authentication == "valid" ? authenticatedStatus : refused),
                $"{authentication} {method} {path}: received {response.StatusCode}");
        }

        // Positive control proves these requests reached a working production
        // endpoint composition rather than an empty router that always refuses.
        using (var response = await client.GetAsync("/v1/models"))
            Assert.Equal(authentication == "valid" ? HttpStatusCode.OK : refused, response.StatusCode);
        var persisted = await traces.GetRecentAsync(TraceKind.LocalApi);
        if (authentication == "valid")
        {
            var trace = Assert.Single(persisted);
            Assert.Equal("verified-client", trace.SourceId);
            Assert.Equal("models.list", trace.Operation);
            using var detail = JsonDocument.Parse(trace.DetailJson);
            Assert.Equal("verified-client", detail.RootElement.GetProperty("client").GetString());
            Assert.Equal("spoofed-owner", detail.RootElement.GetProperty("selfReportedClient").GetString());
            Assert.DoesNotContain(Token, trace.DetailJson);
        }
        else Assert.Empty(persisted);
    }

    private sealed class PassthroughSecrets : ISecretStore
    {
        public bool IsReference(string value) => false;
        public Task<string> StoreAsync(string name, string secret, CancellationToken ct = default) => Task.FromResult(secret);
        public Task<string> ResolveAsync(string valueOrReference, CancellationToken ct = default) => Task.FromResult(valueOrReference);
        public Task<string> BackendLabelAsync(CancellationToken ct = default) => Task.FromResult("Synthetic");
    }
}
