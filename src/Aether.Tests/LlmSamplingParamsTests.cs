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

    private static readonly JsonElement EmptySchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;

    [Fact]
    public void Payload_omits_tools_when_none_declared()
    {
        var payload = OpenAiService.BuildChatPayload("model-a", [new ChatMessage("user", "hi")], LlmChatOptions.Default, 512);
        var json = JsonSerializer.Serialize(payload, JsonOpts);

        Assert.DoesNotContain("\"tools\"", json);
        Assert.DoesNotContain("\"tool_choice\"", json);
    }

    [Fact]
    public void Payload_includes_tools_and_tool_choice_when_declared()
    {
        var options = new LlmChatOptions { Tools = [new LlmToolDefinition("read_file", "Read a file.", EmptySchema)] };

        var openAiJson = JsonSerializer.Serialize(OpenAiService.BuildChatPayload("model-a", [new ChatMessage("user", "hi")], options, 512), JsonOpts);
        Assert.Contains("\"name\":\"read_file\"", openAiJson);
        Assert.Contains("\"tool_choice\":\"auto\"", openAiJson);

        var llamaCppJson = JsonSerializer.Serialize(LlamaCppService.BuildChatPayload("model-a", [new ChatMessage("user", "hi")], options, 512), JsonOpts);
        Assert.Contains("\"name\":\"read_file\"", llamaCppJson);
    }

    [Fact]
    public void ToolCallAccumulator_merges_fragmented_streaming_deltas()
    {
        var accumulator = new OpenAiCompatibleToolWire.ToolCallAccumulator();
        OpenAiCompatibleToolWire.AccumulateFromChunk(
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"read_file","arguments":""}}]}}]}""",
            accumulator);
        OpenAiCompatibleToolWire.AccumulateFromChunk(
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"relative_path\":"}}]}}]}""",
            accumulator);
        OpenAiCompatibleToolWire.AccumulateFromChunk(
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"README.md\"}"}}]}}]}""",
            accumulator);

        Assert.True(accumulator.HasCalls);
        var calls = accumulator.Complete();
        Assert.Single(calls);
        Assert.Equal("call_1", calls[0].Id);
        Assert.Equal("read_file", calls[0].Name);
        Assert.Equal("""{"relative_path":"README.md"}""", calls[0].ArgumentsJson);
    }

    [Fact]
    public void ToolCallAccumulator_ignores_chunks_without_tool_calls()
    {
        var accumulator = new OpenAiCompatibleToolWire.ToolCallAccumulator();
        OpenAiCompatibleToolWire.AccumulateFromChunk("""{"choices":[{"delta":{"content":"hello"}}]}""", accumulator);
        Assert.False(accumulator.HasCalls);
    }

    [Fact]
    public void Ollama_parses_whole_tool_calls_from_the_terminal_chunk()
    {
        var json = """
            {"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"list_files","arguments":{"subdirectory":"src"}}}]},"done":true}
            """;

        var calls = OllamaService.ParseToolCallsForTest(json);
        Assert.NotNull(calls);
        Assert.Single(calls!);
        Assert.Equal("list_files", calls![0].Name);
        Assert.Contains("subdirectory", calls[0].ArgumentsJson);
    }
}
