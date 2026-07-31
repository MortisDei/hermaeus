using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// The app asked a 4B model for valid JSON and hoped, then defended against
/// the answer with three parsers. A constraint makes the shape correct by
/// construction instead (r28 doc 01). These tests cover the contract and the
/// per-provider wire shapes; what they cannot cover is whether a real server
/// honours the field, which is why the field names were read off a running
/// b10195 before any of this was written (see LlmOutputConstraintWire).
/// </summary>
public sealed class OutputConstraintTests
{
    private const string SmallSchema = """{"type":"object","properties":{"waves":{"type":"integer"}},"required":["waves"]}""";

    // ── 1.1 the contract ──

    [Fact]
    public void A_schema_constraint_carries_the_schema_and_nothing_else()
    {
        var constraint = LlmOutputConstraint.FromJsonSchema(SmallSchema, "sea v1");

        Assert.Equal(SmallSchema, constraint.JsonSchema);
        Assert.Null(constraint.Grammar);
        Assert.Equal("sea v1", constraint.Description);
        Assert.True(constraint.IsJsonSchema);
        Assert.True(constraint.IsValid);
    }

    [Fact]
    public void A_grammar_constraint_carries_the_grammar_and_nothing_else()
    {
        var constraint = LlmOutputConstraint.FromGrammar("""root ::= "yes" """, "yes only");

        Assert.Null(constraint.JsonSchema);
        Assert.False(constraint.IsJsonSchema);
        Assert.True(constraint.IsValid);
    }

    [Fact]
    public void Neither_shape_set_is_not_a_usable_constraint()
    {
        Assert.False(new LlmOutputConstraint().IsValid);
        Assert.Throws<ArgumentException>(() => LlmOutputConstraint.FromJsonSchema("  "));
        Assert.Throws<ArgumentException>(() => LlmOutputConstraint.FromGrammar(string.Empty));
    }

    [Fact]
    public void Both_shapes_set_is_not_a_usable_constraint()
    {
        var both = new LlmOutputConstraint { JsonSchema = SmallSchema, Grammar = "root ::= \"x\"" };

        Assert.False(both.IsValid);
    }

    [Fact]
    public void A_constraint_round_trips_through_system_text_json()
    {
        // It goes into traces, so it has to survive the trip.
        var constraint = LlmOutputConstraint.FromJsonSchema(SmallSchema, "sea v1");

        var restored = JsonSerializer.Deserialize<LlmOutputConstraint>(JsonSerializer.Serialize(constraint));

        Assert.Equal(constraint, restored);
    }

    // ── 1.2 / 1.3 per-provider serialization ──

    private static string Serialize(object payload) => JsonSerializer.Serialize(payload);

    [Fact]
    public void The_llama_cpp_payload_carries_a_schema_as_response_format()
    {
        var json = Serialize(LlamaCppService.BuildChatPayload("m", [new ChatMessage("user", "hi")],
            new LlmChatOptions { OutputConstraint = LlmOutputConstraint.FromJsonSchema(SmallSchema, "sea v1") }, 64));

        using var doc = JsonDocument.Parse(json);
        var responseFormat = doc.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", responseFormat.GetProperty("type").GetString());
        Assert.True(responseFormat.GetProperty("json_schema").GetProperty("schema").TryGetProperty("properties", out _));
    }

    [Fact]
    public void The_llama_cpp_payload_carries_a_grammar_verbatim()
    {
        var json = Serialize(LlamaCppService.BuildChatPayload("m", [new ChatMessage("user", "hi")],
            new LlmChatOptions { OutputConstraint = LlmOutputConstraint.FromGrammar("""root ::= "SEAGRAMMAR" """) }, 64));

        using var doc = JsonDocument.Parse(json);
        Assert.Contains("SEAGRAMMAR", doc.RootElement.GetProperty("grammar").GetString());
        // A schema constraint and a grammar constraint are mutually exclusive,
        // so the other field is absent rather than null-and-present.
        Assert.False(doc.RootElement.TryGetProperty("response_format", out var rf) && rf.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public void An_unconstrained_llama_cpp_payload_is_unchanged()
    {
        var json = Serialize(LlamaCppService.BuildChatPayload("m", [new ChatMessage("user", "hi")], LlmChatOptions.Default, 64));

        using var doc = JsonDocument.Parse(json);
        Assert.True(!doc.RootElement.TryGetProperty("grammar", out var g) || g.ValueKind == JsonValueKind.Null);
        Assert.True(!doc.RootElement.TryGetProperty("response_format", out var rf) || rf.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public void The_openai_payload_carries_a_schema_as_response_format()
    {
        var json = Serialize(OpenAiService.BuildChatPayload("m", [new ChatMessage("user", "hi")],
            new LlmChatOptions { OutputConstraint = LlmOutputConstraint.FromJsonSchema(SmallSchema, "sea v1") }, 64));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("json_schema", doc.RootElement.GetProperty("response_format").GetProperty("type").GetString());
    }

    [Fact]
    public void The_ollama_format_field_is_the_schema_itself_unwrapped()
    {
        var format = LlmOutputConstraintWire.OllamaFormat(LlmOutputConstraint.FromJsonSchema(SmallSchema));

        Assert.NotNull(format);
        Assert.Equal("object", format!.Value.GetProperty("type").GetString());
    }

    // ── honest refusal ──

    [Fact]
    public void A_provider_that_cannot_constrain_refuses_by_name()
    {
        var refusal = LlmOutputConstraintWire.DescribeRefusal(
            LlmOutputConstraint.FromJsonSchema(SmallSchema, "sea v1"), LlmConstraintSupport.None, "OpenAI");

        Assert.NotNull(refusal);
        Assert.Contains("OpenAI", refusal!, StringComparison.Ordinal);
        Assert.Contains("sea v1", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void Ollama_refuses_a_grammar_rather_than_dropping_it()
    {
        var refusal = LlmOutputConstraintWire.DescribeRefusal(
            LlmOutputConstraint.FromGrammar("root ::= \"x\"", "grammar v1"), OllamaService.ConstraintSupport, "Ollama");

        Assert.NotNull(refusal);
        Assert.Contains("grammar", refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Llama_cpp_accepts_both_shapes()
    {
        Assert.Null(LlmOutputConstraintWire.DescribeRefusal(
            LlmOutputConstraint.FromJsonSchema(SmallSchema), LlamaCppService.ConstraintSupport, "llama.cpp"));
        Assert.Null(LlmOutputConstraintWire.DescribeRefusal(
            LlmOutputConstraint.FromGrammar("root ::= \"x\""), LlamaCppService.ConstraintSupport, "llama.cpp"));
    }

    [Fact]
    public void A_constraint_that_is_not_valid_json_is_refused_rather_than_sent()
    {
        var refusal = LlmOutputConstraintWire.DescribeRefusal(
            LlmOutputConstraint.FromJsonSchema("{not json", "broken v1"), LlamaCppService.ConstraintSupport, "llama.cpp");

        Assert.NotNull(refusal);
        Assert.Contains("broken v1", refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_constraint_is_refused_before_any_provider_sees_it()
    {
        var refusal = LlmOutputConstraintWire.DescribeRefusal(
            new LlmOutputConstraint { JsonSchema = SmallSchema, Grammar = "root ::= \"x\"" },
            LlamaCppService.ConstraintSupport, "llama.cpp");

        Assert.NotNull(refusal);
    }

    [Fact]
    public void No_constraint_is_never_a_refusal()
    {
        Assert.Null(LlmOutputConstraintWire.DescribeRefusal(null, LlmConstraintSupport.None, "OpenAI"));
    }

    // ── the OpenAI-compatible declaration ──

    private static OpenAiService Endpoint(TempDir temp, string baseUrl, bool declared)
    {
        var settings = NewSettings(temp);
        settings.Settings.Llm.OpenAiBaseUrl = baseUrl;
        settings.Settings.Llm.OpenAiSupportsStructuredOutputs = declared;
        return new OpenAiService(settings, new FakeSecretStore());
    }

    [Fact]
    public void Real_openai_can_always_constrain()
    {
        using var temp = new TempDir();

        Assert.Equal(LlmConstraintSupport.JsonSchema, Endpoint(temp, "https://api.openai.com", declared: false).ConstraintSupport);
    }

    [Fact]
    public void An_undeclared_compatible_endpoint_cannot_constrain()
    {
        using var temp = new TempDir();

        // The important half: silence is not a yes. Without a declaration the
        // request is refused rather than sent as a field the server may drop.
        Assert.Equal(LlmConstraintSupport.None, Endpoint(temp, "http://localhost:1234/v1", declared: false).ConstraintSupport);
    }

    [Fact]
    public void A_declared_compatible_endpoint_can_constrain()
    {
        using var temp = new TempDir();

        // LM Studio, vLLM and friends: the server supports the field and has
        // no way to say so, so the user says so instead.
        Assert.Equal(LlmConstraintSupport.JsonSchema, Endpoint(temp, "http://localhost:1234/v1", declared: true).ConstraintSupport);
    }

    [Fact]
    public void The_declaration_defaults_to_off()
    {
        Assert.False(new Hermaeus.Core.Models.LlmSettings().OpenAiSupportsStructuredOutputs);
    }
}
