using System.Net;
using System.Net.Http.Json;
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
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

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
        public Task MarkRecalledAsync(IEnumerable<string> ids, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> ArchiveStaleMemoriesAsync(double importanceFloor = 0.05, int unrecalledForDays = 180, CancellationToken ct = default) => Task.FromResult(0);
        public Task RunEmbeddingBackfillAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> GetEmbeddingMismatchCountAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> ClearMismatchedEmbeddingsAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private static async Task<(IHost Host, HttpClient Client)> StartTestHostAsync(TempDir temp, bool configureToken = true, IReadOnlyList<(string Name, string Token)>? tokens = null)
    {
        var settingsService = NewSettings(temp);
        await settingsService.LoadAsync();
        if (tokens is not null)
        {
            foreach (var (name, token) in tokens)
                settingsService.Settings.LocalApi.Tokens.Add(new LocalApiTokenEntry { Name = name, SecretRef = token });
        }
        else if (configureToken)
        {
            settingsService.Settings.LocalApi.Tokens.Add(new LocalApiTokenEntry { Name = "test", SecretRef = TestToken });
        }

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
                    services.AddSingleton<IEmbeddingService, FakeEmbeddingService>();
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

    public static async Task ChatCompletionStreamsServerSentEventsWhenRequested()
    {
        using var temp = new TempDir();
        var (host, client) = await StartTestHostAsync(temp);
        using (host)
        {
            client.DefaultRequestHeaders.Add(LocalApiTokenAuth.TokenHeaderName, TestToken);
            var response = await client.PostAsJsonAsync("/v1/chat/completions",
                new ChatCompletionRequest("fake", [new ChatMessageDto("user", "hi")], null, null, Stream: true));

            True(response.IsSuccessStatusCode, "streaming chat completion request should succeed with a valid token.");
            Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType ?? string.Empty,
                "streaming responses should use the text/event-stream content type.");

            var body = await response.Content.ReadAsStringAsync();
            True(body.Contains("\"object\":\"chat.completion.chunk\"", StringComparison.Ordinal),
                "each SSE event should use the OpenAI chat.completion.chunk shape");
            True(body.Contains("\"content\":\"local "), "the first content delta should appear in an SSE data line");
            True(body.TrimEnd().EndsWith("data: [DONE]", StringComparison.Ordinal),
                "the SSE stream should terminate with a [DONE] sentinel");
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

    public static async Task DistinctNamedTokensAuthenticateIndependentlyAndIdentifyTheirCaller()
    {
        using var temp = new TempDir();
        var (host, client) = await StartTestHostAsync(temp, tokens: [("alice-app", "token-alice-0000000000"), ("bob-app", "token-bob-11111111111")]);
        using (host)
        {
            client.DefaultRequestHeaders.Add(LocalApiTokenAuth.TokenHeaderName, "token-alice-0000000000");
            var aliceResponse = await client.GetAsync("/v1/models");
            True(aliceResponse.IsSuccessStatusCode, "alice-app's own token should authenticate.");

            client.DefaultRequestHeaders.Remove(LocalApiTokenAuth.TokenHeaderName);
            client.DefaultRequestHeaders.Add(LocalApiTokenAuth.TokenHeaderName, "token-bob-11111111111");
            var bobResponse = await client.GetAsync("/v1/models");
            True(bobResponse.IsSuccessStatusCode, "bob-app's own distinct token should also authenticate.");

            client.DefaultRequestHeaders.Remove(LocalApiTokenAuth.TokenHeaderName);
            client.DefaultRequestHeaders.Add(LocalApiTokenAuth.TokenHeaderName, "not-a-real-token");
            var badResponse = await client.GetAsync("/v1/models");
            Equal(HttpStatusCode.Unauthorized, badResponse.StatusCode, "a token matching neither entry should still be rejected.");
        }
    }

    public static async Task RevokedTokenStopsAuthenticatingWhileOthersStillWork()
    {
        using var temp = new TempDir();
        var settingsService = NewSettings(temp);
        await settingsService.LoadAsync();
        var keepEntry = new LocalApiTokenEntry { Name = "keep", SecretRef = "token-keep-0000000000" };
        var revokedEntry = new LocalApiTokenEntry { Name = "revoked", SecretRef = "token-revoked-1111111" };
        settingsService.Settings.LocalApi.Tokens.Add(keepEntry);
        settingsService.Settings.LocalApi.Tokens.Add(revokedEntry);

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
                    services.AddSingleton<ITraceStore>(new SqliteTraceStore(settingsService));
                    services.AddSingleton<ModelProfileService>(new ModelProfileService(settingsService));
                    services.AddSingleton<IEmbeddingService, FakeEmbeddingService>();
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

        client.DefaultRequestHeaders.Add(LocalApiTokenAuth.TokenHeaderName, "token-revoked-1111111");
        var beforeRevoke = await client.GetAsync("/v1/models");
        True(beforeRevoke.IsSuccessStatusCode, "the token should authenticate before it is revoked.");

        settingsService.Settings.LocalApi.Tokens.RemoveAll(t => t.Id == revokedEntry.Id);

        var afterRevoke = await client.GetAsync("/v1/models");
        Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode, "a revoked token should stop authenticating immediately.");

        client.DefaultRequestHeaders.Remove(LocalApiTokenAuth.TokenHeaderName);
        client.DefaultRequestHeaders.Add(LocalApiTokenAuth.TokenHeaderName, "token-keep-0000000000");
        var otherToken = await client.GetAsync("/v1/models");
        True(otherToken.IsSuccessStatusCode, "revoking one token should not affect a different, still-configured token.");
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

    public static async Task EmbeddingsEndpointReturnsVectorsForEachInput()
    {
        using var temp = new TempDir();
        var (host, client) = await StartTestHostAsync(temp);
        using (host)
        {
            client.DefaultRequestHeaders.Add(LocalApiTokenAuth.TokenHeaderName, TestToken);
            var response = await client.PostAsJsonAsync("/v1/embeddings", new EmbeddingsRequest(["hello", "a longer phrase"]));

            True(response.IsSuccessStatusCode, "embeddings request should succeed with a valid token.");
            var body = await response.Content.ReadFromJsonAsync<EmbeddingsResponse>();
            True(body is not null && body.Data.Count == 2, "embeddings response should include one vector per input string");
            Equal(4, body!.Dimensions, "embeddings response should report the provider's vector dimensionality");
            Equal(0, body.Data[0].Index, "vectors should be returned in input order with their index");
            Equal(1, body.Data[1].Index, "vectors should be returned in input order with their index");
            Equal(4, body.Data[0].Embedding.Length, "each returned vector should have the reported dimensionality");
        }
    }

    public static async Task EmbeddingsEndpointRejectsEmptyInput()
    {
        using var temp = new TempDir();
        var (host, client) = await StartTestHostAsync(temp);
        using (host)
        {
            client.DefaultRequestHeaders.Add(LocalApiTokenAuth.TokenHeaderName, TestToken);
            var response = await client.PostAsJsonAsync("/v1/embeddings", new EmbeddingsRequest([]));
            Equal(HttpStatusCode.BadRequest, response.StatusCode, "an empty input array should be rejected.");
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
        settingsService.Settings.LocalApi.Tokens.Add(new LocalApiTokenEntry { Name = "test", SecretRef = TestToken });
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
                    services.AddSingleton<IEmbeddingService, FakeEmbeddingService>();
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
        Equal("test", recent[0].SourceId, "the trace should record the verified per-app token name, not the self-reported header");
        True(recent[0].DetailJson.Contains("\"selfReportedClient\":\"my-test-app\"", StringComparison.Ordinal),
            "the self-reported X-Hermaeus-Client header should still be recorded as an unverified hint in the trace detail");
        Equal("chat.completions", recent[0].Operation, "the trace should record which endpoint was called");
    }

    public static async Task ChatCompletionAppliesModelProfileSamplingDefaultsAndHonorsExplicitOverrides()
    {
        using var temp = new TempDir();
        var settingsService = NewSettings(temp);
        await settingsService.LoadAsync();
        settingsService.Settings.LocalApi.Tokens.Add(new LocalApiTokenEntry { Name = "test", SecretRef = TestToken });
        settingsService.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settingsService.Settings.Llm.TopP = 0.5;

        var profiles = new ModelProfileService(settingsService);
        await profiles.SaveAsync(new ModelProfile { ModelId = "capture", DefaultTopP = 0.9, DefaultTopK = 40 });

        var capturing = new CapturingLlm();
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
                    services.AddSingleton<ILlmService>(capturing);
                    services.AddSingleton<IMemoryStore>(memories);
                    services.AddSingleton(rag);
                    services.AddSingleton<ITraceStore>(new SqliteTraceStore(settingsService));
                    services.AddSingleton(profiles);
                    services.AddSingleton<IEmbeddingService, FakeEmbeddingService>();
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

        // No sampling params supplied: the model's saved profile default
        // should win over the global setting.
        var response = await client.PostAsJsonAsync("/v1/chat/completions",
            new ChatCompletionRequest("capture", [new ChatMessageDto("user", "hi")], null, null));
        True(response.IsSuccessStatusCode, "chat completion should succeed");
        Equal(0.9, capturing.LastOptions?.TopP, "with no explicit TopP, the model profile default should be used over the global setting");
        Equal(40, capturing.LastOptions?.TopK, "with no explicit TopK, the model profile default should be used");

        // Explicit param supplied: it should win over both the profile and the global default.
        response = await client.PostAsJsonAsync("/v1/chat/completions",
            new ChatCompletionRequest("capture", [new ChatMessageDto("user", "hi")], null, null, TopP: 0.2));
        True(response.IsSuccessStatusCode, "chat completion should succeed");
        Equal(0.2, capturing.LastOptions?.TopP, "an explicit TopP in the request should override the model profile default");

        // A model with no profile falls back to the global setting.
        response = await client.PostAsJsonAsync("/v1/chat/completions",
            new ChatCompletionRequest("no-profile-model", [new ChatMessageDto("user", "hi")], null, null));
        True(response.IsSuccessStatusCode, "chat completion should succeed");
        Equal(0.5, capturing.LastOptions?.TopP, "with no profile and no explicit value, the global LLM setting should be used");
    }
}
