using System.Net;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class RuntimeProfileHealthCheckTests
{
    /// <summary>r11 2.8: when the stored ApiKey is a secret reference, the health check must resolve it before sending, not authenticate with the literal "secret:&lt;name&gt;" string.</summary>
    [Fact]
    public async Task CheckHealthAsync_resolves_a_secret_reference_before_sending_the_bearer_token()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var handler = new CapturingHandler();
        var service = new RuntimeProfileService(settings, new ResolvingSecretStore("resolved-api-key"), new HttpClient(handler));

        var profile = new RuntimeProfile { Kind = RuntimeKind.OpenAiCompatible, BaseUrl = "https://api.example.test", ApiKey = "secret:my-key" };
        var health = await service.CheckHealthAsync(profile);

        Assert.True(health.IsHealthy);
        Assert.Equal("Bearer resolved-api-key", handler.LastAuthorization);
    }

    /// <summary>A plain (non-reference) API key value must still pass through unchanged.</summary>
    [Fact]
    public async Task CheckHealthAsync_passes_a_plain_key_through_unchanged()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var handler = new CapturingHandler();
        var service = new RuntimeProfileService(settings, new ResolvingSecretStore("resolved-api-key"), new HttpClient(handler));

        var profile = new RuntimeProfile { Kind = RuntimeKind.OpenAiCompatible, BaseUrl = "https://api.example.test", ApiKey = "plain-value" };
        await service.CheckHealthAsync(profile);

        Assert.Equal("Bearer plain-value", handler.LastAuthorization);
    }

    private sealed class ResolvingSecretStore(string resolved) : ISecretStore
    {
        public bool IsReference(string value) => value.StartsWith("secret:", StringComparison.OrdinalIgnoreCase);
        public Task<string> StoreAsync(string name, string secret, CancellationToken ct = default) => Task.FromResult(secret);
        public Task<string> ResolveAsync(string valueOrReference, CancellationToken ct = default) =>
            Task.FromResult(IsReference(valueOrReference) ? resolved : valueOrReference);
        public Task<string> BackendLabelAsync(CancellationToken ct = default) => Task.FromResult("resolving fake");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastAuthorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
