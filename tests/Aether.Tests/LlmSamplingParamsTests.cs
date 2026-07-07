using System.Text.Json;
using System.Text.Json.Serialization;
using Aether.Core.Services;
using Aether.Services;
using Xunit;

namespace Aether.Tests;

public sealed class LlmSamplingParamsTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void LlamaCpp_payload_includes_all_sampling_params_when_set()
    {
        var options = new LlmChatOptions
        {
            Temperature = 0.8,
            TopP = 0.9,
            TopK = 40,
            MinP = 0.05,
            RepeatPenalty = 1.1,
            FrequencyPenalty = 0.2,
            PresencePenalty = 0.3
        };

        var payload = LlamaCppService.BuildChatPayload("model-a", [new ChatMessage("user", "hi")], options, 512);
        var json = JsonSerializer.Serialize(payload, JsonOpts);

        Assert.Contains("\"top_p\":0.9", json);
        Assert.Contains("\"top_k\":40", json);
        Assert.Contains("\"min_p\":0.05", json);
        Assert.Contains("\"repeat_penalty\":1.1", json);
        Assert.Contains("\"frequency_penalty\":0.2", json);
        Assert.Contains("\"presence_penalty\":0.3", json);
    }

    [Fact]
    public void LlamaCpp_payload_omits_unset_sampling_params()
    {
        var options = new LlmChatOptions { Temperature = 0.8 };

        var payload = LlamaCppService.BuildChatPayload("model-a", [new ChatMessage("user", "hi")], options, 512);
        var json = JsonSerializer.Serialize(payload, JsonOpts);

        Assert.DoesNotContain("top_p", json);
        Assert.DoesNotContain("top_k", json);
        Assert.DoesNotContain("min_p", json);
        Assert.DoesNotContain("repeat_penalty", json);
        Assert.DoesNotContain("frequency_penalty", json);
        Assert.DoesNotContain("presence_penalty", json);
    }

    [Fact]
    public void OpenAi_payload_only_forwards_standard_sampling_params()
    {
        var options = new LlmChatOptions
        {
            Temperature = 0.8,
            TopP = 0.9,
            TopK = 40,
            MinP = 0.05,
            RepeatPenalty = 1.1,
            FrequencyPenalty = 0.2,
            PresencePenalty = 0.3
        };

        var payload = OpenAiService.BuildChatPayload("model-a", [new ChatMessage("user", "hi")], options, 512);
        var json = JsonSerializer.Serialize(payload, JsonOpts);

        Assert.Contains("\"top_p\":0.9", json);
        Assert.Contains("\"frequency_penalty\":0.2", json);
        Assert.Contains("\"presence_penalty\":0.3", json);
        Assert.DoesNotContain("top_k", json);
        Assert.DoesNotContain("min_p", json);
        Assert.DoesNotContain("repeat_penalty", json);
    }
}
