using System.Reflection;
using System.Text.Json;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// The planner protocol used to be a schema written in prose at the end of a
/// system prompt, asked for on every step and defended by an extractor, a
/// targeted repair and an error budget. r28 doc 05 5.1 writes it as a real
/// schema. This is the test that fails when the schema and the record drift,
/// which is the price of hand-writing it and is cheaper than a generator.
/// </summary>
public sealed class AgentPlannerSchemaTests
{
    private static JsonElement Schema()
    {
        using var doc = JsonDocument.Parse(AgentService.PlannerResponseSchema);
        return doc.RootElement.Clone();
    }

    private static string SnakeCase(string name) =>
        string.Concat(name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    private static IEnumerable<string> RecordProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => SnakeCase(p.Name)).Order();

    private static IEnumerable<string> SchemaProperties(JsonElement objectSchema) =>
        objectSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order();

    [Fact]
    public void The_schema_and_the_planner_response_agree_property_for_property()
    {
        var schema = Schema();

        Assert.Equal(RecordProperties(typeof(AgentPlannerResponse)), SchemaProperties(schema));
        Assert.Equal(
            RecordProperties(typeof(AgentNextAction)),
            SchemaProperties(schema.GetProperty("properties").GetProperty("next_action")));
        Assert.Equal(
            RecordProperties(typeof(AgentStateUpdate)),
            SchemaProperties(schema.GetProperty("properties").GetProperty("state_update")));
    }

    [Fact]
    public void The_action_kind_enum_matches_the_C_sharp_enum_by_name()
    {
        var schemaValues = Schema()
            .GetProperty("properties").GetProperty("next_action")
            .GetProperty("properties").GetProperty("type").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()!).Order();

        Assert.Equal(Enum.GetNames<AgentActionKind>().Select(SnakeCase).Order(), schemaValues);
    }

    [Fact]
    public void The_risk_level_enum_matches_the_C_sharp_enum_by_name()
    {
        var schemaValues = Schema()
            .GetProperty("properties").GetProperty("next_action")
            .GetProperty("properties").GetProperty("risk_level").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()!).Order();

        Assert.Equal(Enum.GetNames<AgentRiskLevel>().Select(SnakeCase).Order(), schemaValues);
    }

    [Fact]
    public void The_state_update_arrays_are_arrays_of_strings()
    {
        var stateUpdate = Schema().GetProperty("properties").GetProperty("state_update").GetProperty("properties");

        foreach (var property in stateUpdate.EnumerateObject())
        {
            Assert.Equal("array", property.Value.GetProperty("type").GetString());
            Assert.Equal("string", property.Value.GetProperty("items").GetProperty("type").GetString());
        }
    }

    [Fact]
    public void A_truncated_object_fails_the_schema_rather_than_the_deserializer()
    {
        // Not a validator run, which would need a package. What is asserted is
        // that the schema declares the top-level properties required, which is
        // what makes a truncated object fail at the sampler.
        var required = Schema().GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.Contains("thought_summary", required);
        Assert.Contains("next_action", required);
        Assert.Contains("state_update", required);
        Assert.Contains("user_message", required);
    }

    [Fact]
    public void A_document_valid_against_the_schema_deserializes_without_repair()
    {
        var document = """
            {
              "thought_summary": "Reading the workspace.",
              "current_step": "List the files.",
              "next_action": {
                "type": "tool",
                "tool_name": "list_files",
                "arguments": {},
                "requires_approval": false,
                "risk_level": "low"
              },
              "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
              "user_message": "Listing files.",
              "reservations": []
            }
            """;

        Assert.False(AgentService.TryRepairActionType(document, out _));

        var parsed = AgentService.ParseResponse(document);
        Assert.Equal(AgentActionKind.Tool, parsed.NextAction.Type);
        Assert.Equal("list_files", parsed.NextAction.ToolName);
        Assert.Equal(AgentRiskLevel.Low, parsed.NextAction.RiskLevel);
    }

    // ── 5.4 the parse-failure message ──

    [Fact]
    public void An_unconstrained_prose_reply_says_the_provider_could_not_enforce_a_shape()
    {
        var message = AgentService.DescribeParseFailure("Sure! Here is what I would do next.", constraintApplied: false);

        Assert.Contains("cannot enforce", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The one case where the fix is a checkbox rather than a bigger model:
    /// an OpenAI-compatible endpoint that supports response_format and has not
    /// been declared as supporting it.
    /// </summary>
    [Fact]
    public void An_undeclared_compatible_endpoint_is_told_about_the_setting()
    {
        var message = AgentService.DescribeParseFailure(
            "Sure! Here is what I would do next.", constraintApplied: false, constraintAvailableButUndeclared: true);

        Assert.Contains("response_format", message, StringComparison.Ordinal);
        Assert.Contains("Settings", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_constrained_reply_is_never_told_about_the_setting()
    {
        // Applied wins: the shape was enforced, so a checkbox is not the answer.
        var message = AgentService.DescribeParseFailure(
            "Sure! Here is what I would do next.", constraintApplied: true, constraintAvailableButUndeclared: true);

        Assert.DoesNotContain("response_format", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_constrained_prose_reply_stops_blaming_the_format_request()
    {
        var message = AgentService.DescribeParseFailure("Sure! Here is what I would do next.", constraintApplied: true);

        Assert.Contains("enforced", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cannot enforce", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_truncated_reply_is_diagnosed_the_same_way_either_side_of_the_constraint()
    {
        const string truncated = """{"thought_summary": "working", "next_action": { "type": "tool" """;

        Assert.Equal(
            AgentService.DescribeParseFailure(truncated, constraintApplied: false),
            AgentService.DescribeParseFailure(truncated, constraintApplied: true));
    }

    [Fact]
    public void An_empty_reply_is_diagnosed_the_same_way_either_side_of_the_constraint()
    {
        Assert.Equal(
            AgentService.DescribeParseFailure(string.Empty, constraintApplied: false),
            AgentService.DescribeParseFailure(string.Empty, constraintApplied: true));
    }
}
