using System.Net;
using System.Text;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class OpenAiServiceTests
{
    /// <summary>r11 2.2: auth must be a per-request header, never a write to the shared static client's DefaultRequestHeaders (which races under concurrent chat + model refresh, and would leak between requests once runtime profiles carry their own key).</summary>
    [Fact]
    public async Task GetModelsAsync_sends_a_per_request_authorization_header_and_never_touches_default_headers()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.OpenAiApiKey = "secret:openai-key";
        settings.Settings.Llm.OpenAiBaseUrl = "https://api.example.test";

        var handler = new CapturingHandler("""{"data":[{"id":"llama-3.3-70b"}]}""");
        using var http = new HttpClient(handler);
        using var service = new OpenAiService(settings, new ResolvingSecretStore("resolved-key-1"), http);

        await service.GetModelsAsync();

        Assert.Equal("Bearer resolved-key-1", handler.LastCapturedAuthorization);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    /// <summary>Two calls resolving different secrets must never bleed into each other, which is exactly what mutating a shared DefaultRequestHeaders would risk under concurrency.</summary>
    [Fact]
    public async Task Sequential_calls_with_different_resolved_keys_each_send_their_own_header()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.OpenAiBaseUrl = "https://api.example.test";

        var handler = new CapturingHandler("""{"data":[]}""");
        using var http = new HttpClient(handler);

        settings.Settings.Llm.OpenAiApiKey = "secret:key-a";
        using (var serviceA = new OpenAiService(settings, new ResolvingSecretStore("key-a-resolved"), http))
            await serviceA.GetModelsAsync();
        Assert.Equal("Bearer key-a-resolved", handler.LastCapturedAuthorization);

        settings.Settings.Llm.OpenAiApiKey = "secret:key-b";
        using (var serviceB = new OpenAiService(settings, new ResolvingSecretStore("key-b-resolved"), http))
            await serviceB.GetModelsAsync();
        Assert.Equal("Bearer key-b-resolved", handler.LastCapturedAuthorization);
    }

    /// <summary>r11 2.3: pointing OpenAiBaseUrl at a non-OpenAI compatible endpoint must surface every chat-usable id, not just gpt/o1/o3/o4-prefixed ones.</summary>
    [Fact]
    public async Task GetModelsAsync_surfaces_non_gpt_ids_for_a_compatible_endpoint()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.OpenAiApiKey = "plain-key";
        settings.Settings.Llm.OpenAiBaseUrl = "https://openrouter.example.test/api/v1";

        var body = """{"data":[{"id":"llama-3.3-70b"},{"id":"mistral-large"},{"id":"claude-3.7-sonnet"}]}""";
        using var http = new HttpClient(new CapturingHandler(body));
        using var service = new OpenAiService(settings, new ResolvingSecretStore("unused"), http);

        var models = await service.GetModelsAsync();

        Assert.Equal(3, models.Count);
        Assert.Contains(models, m => m.Id == "llama-3.3-70b");
        Assert.Contains(models, m => m.Id == "mistral-large");
        Assert.Contains(models, m => m.Id == "claude-3.7-sonnet");
    }

    /// <summary>On the real OpenAI endpoint, known non-chat ids (embeddings/tts/whisper/dall-e) are still filtered out.</summary>
    [Fact]
    public async Task GetModelsAsync_filters_known_non_chat_ids_only_against_the_real_openai_endpoint()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.OpenAiApiKey = "plain-key";
        settings.Settings.Llm.OpenAiBaseUrl = "https://api.openai.com/v1";

        var body = """{"data":[{"id":"gpt-4o"},{"id":"text-embedding-3-large"},{"id":"whisper-1"},{"id":"tts-1"},{"id":"dall-e-3"}]}""";
        using var http = new HttpClient(new CapturingHandler(body));
        using var service = new OpenAiService(settings, new ResolvingSecretStore("unused"), http);

        var models = await service.GetModelsAsync();

        var ids = models.Select(m => m.Id).ToList();
        Assert.Single(ids);
        Assert.Contains("gpt-4o", ids);
    }

    private sealed class ResolvingSecretStore(string resolved) : ISecretStore
    {
        public bool IsReference(string value) => value.StartsWith("secret:", StringComparison.OrdinalIgnoreCase);
        public Task<string> StoreAsync(string name, string secret, CancellationToken ct = default) => Task.FromResult(secret);
        public Task<string> ResolveAsync(string valueOrReference, CancellationToken ct = default) =>
            Task.FromResult(IsReference(valueOrReference) ? resolved : valueOrReference);
        public Task<string> BackendLabelAsync(CancellationToken ct = default) => Task.FromResult("Resolving fake");
    }

    private sealed class CapturingHandler(string jsonBody) : HttpMessageHandler
    {
        public string? LastCapturedAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastCapturedAuthorization = request.Headers.Authorization?.ToString();
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
