using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class ProviderCapabilityDiscoveryTests
{
    [Fact]
    public void LlamaCpp_props_top_level_n_ctx_is_read()
    {
        var result = LlamaCppService.ParsePropsContextLength("""{"n_ctx": 8192}""");
        Assert.Equal(8192, result);
    }

    [Fact]
    public void LlamaCpp_props_falls_back_to_default_generation_settings()
    {
        var result = LlamaCppService.ParsePropsContextLength(
            """{"default_generation_settings": {"n_ctx": 4096}}""");
        Assert.Equal(4096, result);
    }

    [Fact]
    public void LlamaCpp_props_missing_context_length_returns_null()
    {
        Assert.Null(LlamaCppService.ParsePropsContextLength("""{"total_slots": 1}"""));
    }

    [Fact]
    public void LlamaCpp_props_malformed_json_returns_null()
    {
        Assert.Null(LlamaCppService.ParsePropsContextLength("not json"));
    }

    [Fact]
    public void Ollama_show_reads_architecture_keyed_context_length()
    {
        var json = """
        {
            "model_info": {
                "general.architecture": "qwen2",
                "qwen2.context_length": 32768
            }
        }
        """;
        Assert.Equal(32768, OllamaService.ParseShowContextLength(json));
    }

    [Fact]
    public void Ollama_show_missing_architecture_returns_null()
    {
        var json = """{"model_info": {"qwen2.context_length": 32768}}""";
        Assert.Null(OllamaService.ParseShowContextLength(json));
    }

    [Fact]
    public void Ollama_show_missing_model_info_returns_null()
    {
        Assert.Null(OllamaService.ParseShowContextLength("""{"details": {}}"""));
    }

    [Fact]
    public void ApplyProfiles_uses_probed_context_length_when_no_user_override()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var profiles = new ModelProfileService(settings);
        var models = new List<LlmModel>
        {
            new() { Id = "m1", Name = "m1", ProbedContextLength = 32768 }
        };

        profiles.ApplyProfiles(models);

        Assert.Equal(32768, models[0].DefaultContextSize);
    }

    [Fact]
    public async Task ApplyProfiles_explicit_user_override_wins_over_probe()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var profiles = new ModelProfileService(settings);
        await profiles.SaveAsync(new ModelProfile { ModelId = "m1", DefaultContextSize = 4096 });

        var models = new List<LlmModel>
        {
            new() { Id = "m1", Name = "m1", ProbedContextLength = 32768 }
        };

        profiles.ApplyProfiles(models);

        Assert.Equal(4096, models[0].DefaultContextSize);
    }
}
