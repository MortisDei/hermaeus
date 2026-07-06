using Aether.Core.Models;
using Aether.Services;
using Xunit;

namespace Aether.Tests;

public sealed class ProviderDescriptorTests
{
    [Fact]
    public void Registry_covers_all_provider_tags_exactly_once()
    {
        var tags = CompositeLlmService.Providers.Select(p => p.Tag).ToList();
        Assert.Equal(tags.Count, tags.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("llama.cpp", tags);
        Assert.Contains("ollama", tags);
        Assert.Contains("openai", tags);
    }

    [Fact]
    public void Only_the_openai_provider_is_remote()
    {
        var remote = CompositeLlmService.Providers.Where(p => p.IsRemote).ToList();
        Assert.Single(remote);
        Assert.Equal("openai", remote[0].Tag);
    }

    [Fact]
    public void Model_pull_and_delete_are_ollama_capabilities_only()
    {
        foreach (var p in CompositeLlmService.Providers)
        {
            var canManageModels = p.Supports(ProviderCapabilities.ModelPull)
                                  || p.Supports(ProviderCapabilities.ModelDelete);
            Assert.Equal(p.Tag == "ollama", canManageModels);
        }
    }

    [Theory]
    [InlineData(RuntimeKind.LlamaCpp, "llama.cpp")]
    [InlineData(RuntimeKind.Ollama, "ollama")]
    [InlineData(RuntimeKind.OpenAiCompatible, "openai")]
    public void DescriptorFor_maps_every_runtime_kind_to_its_provider_tag(RuntimeKind kind, string expectedTag)
    {
        Assert.Equal(expectedTag, CompositeLlmService.DescriptorFor(kind).Tag);
    }
}
