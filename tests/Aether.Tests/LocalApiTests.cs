using System.Net;
using System.Net.Http.Json;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.LocalApi;
using Aether.Rag;
using Aether.Rag.Retrieval;
using Aether.Rag.Storage;
using Aether.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

internal static class LocalApiTests
{
    private const string TestToken = "test-local-api-token-0123456789";

    private sealed class FakeSecretStore : ISecretStore
    {
        public bool IsReference(string value) => false;
        public Task<string> StoreAsync(string name, string secret, CancellationToken ct = default) => Task.FromResult(secret);
        public Task<string> ResolveAsync(string valueOrReference, CancellationToken ct = default) => Task.FromResult(valueOrReference);
        public Task<string> BackendLabelAsync(CancellationToken ct = default) => Task.FromResult("Fake");
    }

    private sealed class FakeMemoryStore : IMemoryStore
    {
        private readonly List<Memory> _items;
        public FakeMemoryStore(List<Memory> items) => _items = items;

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Memory>> GetAllAsync(bool includeArchived = false, CancellationToken ct = default) => Task.FromResult(_items.ToList());
        public Task<Memory?> GetByIdAsync(string id, CancellationToken ct = default) => Task.FromResult(_items.FirstOrDefault(m => m.Id == id));
        public Task<List<Memory>> GetByCategoryAsync(string category, CancellationToken ct = default) => Task.FromResult(_items.Where(m => m.Category == category).ToList());
        public Task<List<Memory>> GetByScopeAsync(MemoryScope scope, string? scopeId = null, bool includeArchived = false, CancellationToken ct = default) => Task.FromResult(_items.ToList());
        public Task SaveAsync(Memory memory, CancellationToken ct = default) { _items.Add(memory); return Task.CompletedTask; }
        public Task DeleteAsync(string id, CancellationToken ct = default) { _items.RemoveAll(m => m.Id == id); return Task.CompletedTask; }
        public Task<List<Memory>> SearchAsync(string query, CancellationToken ct = default) =>
            Task.FromResult(_items.Where(m => m.Content.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList());
        public Task<List<Memory>> GetByImportanceAsync(double minScore, CancellationToken ct = default) => Task.FromResult(_items.Where(m => m.ImportanceScore >= minScore).ToList());
        public Task<List<Memory>> GetRecentAsync(int limit = 10, CancellationToken ct = default) => Task.FromResult(_items.Take(limit).ToList());
        public Task<List<Memory>> GetRecentByConversationAsync(string conversationId, int limit = 10, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task<int> GetCountByConversationAsync(string conversationId, bool includeArchived = false, CancellationToken ct = default) => Task.FromResult(0);
        public Task<Dictionary<string, int>> GetCountsByConversationAsync(IEnumerable<string> conversationIds, bool includeArchived = false, CancellationToken ct = default) =>
            Task.FromResult(conversationIds.ToDictionary(id => id, _ => 0));
    }

    private static async Task<(IHost Host, HttpClient Client)> StartTestHostAsync(TempDir temp, bool configureToken = true)
    {
        var settingsService = NewSettings(temp);
        await settingsService.LoadAsync();
        settingsService.Settings.LocalApi.ApiToken = configureToken ? TestToken : string.Empty;

        var memories = new FakeMemoryStore([
            new Memory { Id = "m1", Category = "facts", Content = "The user's favorite language is C#.", ImportanceScore = 0.8 }
        ]);

        var ragStore = new SqliteRagStore(settingsService);
        await ragStore.InitializeAsync();
        var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settingsService, new NoOpReranker());

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<ISettingsService>(settingsService);
                    services.AddSingleton<ISecretStore, FakeSecretStore>();
                    services.AddSingleton<ILlmService>(new FakeLlm());
                    services.AddSingleton<IMemoryStore>(memories);
                    services.AddSingleton(rag);
                    services.AddSingleton<ITraceStore>(new SqliteTraceStore(settingsService));
                    services.AddSingleton<ModelProfileService>(new ModelProfileService(settingsService));
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseLocalApiTokenAuth();
                    app.UseEndpoints(endpoints => endpoints.MapLocalApiEndpoints());
                });
            });

        var host = await hostBuilder.StartAsync();
        var client = host.GetTestClient();
        return (host, client);
    }

    public static async Task ChatCompletionEndpointReturnsAggregatedContent()
    {
        using var temp = new TempDir();
        var (host, client) = await StartTestHostAsync(temp);
        using (host)
        {
            client.DefaultRequestHeaders.Add(LocalApiTokenAuth.TokenHeaderName, TestToken);
            var response = await client.PostAsJsonAsync("/v1/chat/completions",
                new ChatCompletionRequest("fake", [new ChatMessageDto("user", "hi")], null, null));

            True(response.IsSuccessStatusCode, "Chat completion request should succeed with a valid token.");
            var body = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>();
            True(body is not null && body.Content == "local ready alpha beta 42",
                $"Expected aggregated fake LLM output, got '{body?.Content}'.");
        }
    }

    public static async Task RequestsWithoutTokenAreRejected()
    {
        using var temp = new TempDir();
        var (host, client) = await StartTestHostAsync(temp);
        using (host)
        {
            var response = await client.PostAsJsonAsync("/v1/chat/completions",
                new ChatCompletionRequest("fake", [new ChatMessageDto("user", "hi")], null, null));
            Equal(HttpStatusCode.Unauthorized, response.StatusCode, "A request with no token header must be rejected.");
        }
    }

    public static async Task RequestsAreRejectedWhenNoTokenIsConfigured()
    {
        using var temp = new TempDir();
        var (host, client) = await StartTestHostAsync(temp, configureToken: false);
        using (host)
        {
            client.DefaultRequestHeaders.Add(LocalApiTokenAuth.TokenHeaderName, "anything");
            var response = await client.PostAsJsonAsync("/v1/chat/completions",
                new ChatCompletionRequest("fake", [new ChatMessageDto("user", "hi")], null, null));
            Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode,
                "The local API must fail closed when no token has been configured, never allow unauthenticated access.");
        }
    }

    public static async Task MemoryQueryEndpointReturnsMatchingMemories()
    {
        using var temp = new TempDir();
        var (host, client) = await StartTestHostAsync(temp);
        using (host)
        {
            client.DefaultRequestHeaders.Add(LocalApiTokenAuth.TokenHeaderName, TestToken);
            var response = await client.GetAsync("/v1/memory/query?q=C%23");
            True(response.IsSuccessStatusCode, "Memory query should succeed with a valid token.");
            var body = await response.Content.ReadFromJsonAsync<MemoryQueryResponse>();
            True(body is not null && body.Memories.Count == 1 && body.Memories[0].Id == "m1",
                "Memory query should return the matching seeded memory.");
        }
    }

    public static async Task RagQueryEndpointRefusesWhenDatasetHasNoContext()
    {
        using var temp = new TempDir();
        var (host, client) = await StartTestHostAsync(temp);
        using (host)
        {
            client.DefaultRequestHeaders.Add(LocalApiTokenAuth.TokenHeaderName, TestToken);
            var response = await client.PostAsJsonAsync("/v1/rag/query",
                new RagQueryRequest("nonexistent-dataset", "What is this?", null));
            True(response.IsSuccessStatusCode, "RAG query should succeed (with a grounded refusal) even for an empty dataset.");
            var body = await response.Content.ReadFromJsonAsync<RagQueryResponse>();
            True(body is not null && body.Answer.Contains("not have enough grounded context", StringComparison.OrdinalIgnoreCase),
                $"Expected a grounding refusal for an empty dataset, got '{body?.Answer}'.");
        }
    }

    public static async Task ChatCompletionRejectsMissingFields()
    {
        using var temp = new TempDir();
        var (host, client) = await StartTestHostAsync(temp);
        using (host)
        {
            client.DefaultRequestHeaders.Add(LocalApiTokenAuth.TokenHeaderName, TestToken);
            var response = await client.PostAsJsonAsync("/v1/chat/completions",
                new ChatCompletionRequest("", [], null, null));
            Equal(HttpStatusCode.BadRequest, response.StatusCode, "modelId/messages are required.");
        }
    }

    public static async Task ModelsEndpointReturnsVisibleModels()
    {
        using var temp = new TempDir();
        var (host, client) = await StartTestHostAsync(temp);
        using (host)
        {
            client.DefaultRequestHeaders.Add(LocalApiTokenAuth.TokenHeaderName, TestToken);
            var response = await client.GetAsync("/v1/models");
            True(response.IsSuccessStatusCode, "Models list should succeed with a valid token.");
            var body = await response.Content.ReadFromJsonAsync<ModelsResponse>();
            True(body is not null && body.Models.Any(m => m.Id == "fake"),
                "Models list should include the fake provider's model.");
        }
    }

    public static async Task CallsAreLoggedToTraceStoreWithCallerName()
    {
        using var temp = new TempDir();
        var settingsService = NewSettings(temp);
        await settingsService.LoadAsync();
        settingsService.Settings.LocalApi.ApiToken = TestToken;
        settingsService.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var traces = new SqliteTraceStore(settingsService);

        var memories = new FakeMemoryStore([]);
        var ragStore = new SqliteRagStore(settingsService);
        await ragStore.InitializeAsync();
        var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settingsService, new NoOpReranker());

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<ISettingsService>(settingsService);
                    services.AddSingleton<ISecretStore, FakeSecretStore>();
                    services.AddSingleton<ILlmService>(new FakeLlm());
                    services.AddSingleton<IMemoryStore>(memories);
                    services.AddSingleton(rag);
                    services.AddSingleton<ITraceStore>(traces);
                    services.AddSingleton<ModelProfileService>(new ModelProfileService(settingsService));
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseLocalApiTokenAuth();
                    app.UseEndpoints(endpoints => endpoints.MapLocalApiEndpoints());
                });
            });

        using var host = await hostBuilder.StartAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(LocalApiTokenAuth.TokenHeaderName, TestToken);
        client.DefaultRequestHeaders.Add(LocalApiEndpoints.ClientHeaderName, "my-test-app");

        var response = await client.PostAsJsonAsync("/v1/chat/completions",
            new ChatCompletionRequest("fake", [new ChatMessageDto("user", "hi")], null, null));
        True(response.IsSuccessStatusCode, "Chat completion should succeed.");

        var recent = await traces.GetRecentAsync(TraceKind.LocalApi, 10);
        True(recent.Count == 1, "Exactly one local API call should have been traced.");
        Equal("my-test-app", recent[0].SourceId, "the trace should record the caller's self-reported client name");
        Equal("chat.completions", recent[0].Operation, "the trace should record which endpoint was called");
    }
}
