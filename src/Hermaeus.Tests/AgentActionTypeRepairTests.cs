using System.Text.Json;
using Hermaeus.Agent.Services;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// A local model that names a tool in <c>next_action.type</c> instead of
/// saying "tool" produces a complete, well-formed response that the strict
/// enum rejected outright. The user saw "the model's response could not be
/// parsed as valid JSON" (it was valid) and the run stalled: four times in one
/// real task, every one of them this exact shape.
/// </summary>
public sealed class AgentActionTypeRepairTests
{
    /// <summary>
    /// Trimmed from a real stalled step: complete, fenced, well-formed, and
    /// rejected only for "type": "set_plan" with a null tool_name.
    /// </summary>
    private const string RealWorldSetPlanResponse = """
        {
          "thought_summary": "I have analyzed the entry point and five core generator components.",
          "current_step": "Analyze the existing code structure",
          "next_action": {
            "type": "set_plan",
            "tool_name": null,
            "arguments": {
              "steps": [
                { "description": "Read AudioWaveGenerator.cs", "status": "pending" }
              ]
            },
            "requires_approval": false,
            "risk_level": "none"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "I have updated the plan."
        }
        """;

    [Fact]
    public void A_tool_name_used_as_the_action_type_is_repaired_into_the_protocol_shape()
    {
        Assert.True(AgentService.TryRepairActionType(RealWorldSetPlanResponse, out var repaired));

        using var doc = JsonDocument.Parse(repaired);
        var action = doc.RootElement.GetProperty("next_action");
        Assert.Equal("tool", action.GetProperty("type").GetString());
        Assert.Equal("set_plan", action.GetProperty("tool_name").GetString());
    }

    [Fact]
    public void The_repaired_response_keeps_every_other_field_intact()
    {
        Assert.True(AgentService.TryRepairActionType(RealWorldSetPlanResponse, out var repaired));

        using var doc = JsonDocument.Parse(repaired);
        Assert.Equal("I have updated the plan.", doc.RootElement.GetProperty("user_message").GetString());
        Assert.Equal("Analyze the existing code structure", doc.RootElement.GetProperty("current_step").GetString());

        var steps = doc.RootElement.GetProperty("next_action").GetProperty("arguments").GetProperty("steps");
        Assert.Equal("Read AudioWaveGenerator.cs", steps[0].GetProperty("description").GetString());
        Assert.False(doc.RootElement.GetProperty("next_action").GetProperty("requires_approval").GetBoolean());
    }

    [Theory]
    [InlineData("tool")]
    [InlineData("ask_user")]
    [InlineData("final")]
    [InlineData("none")]
    public void A_response_whose_type_is_already_a_known_kind_is_left_alone(string kind)
    {
        var json = $$$"""{"next_action":{"type":"{{{kind}}}","tool_name":"read_file"}}""";

        Assert.False(AgentService.TryRepairActionType(json, out var repaired));
        Assert.Equal(json, repaired);
    }

    [Fact]
    public void A_response_that_already_names_a_different_tool_is_left_alone()
    {
        // Ambiguous: two different tools named in one action. Guessing which
        // one the model meant is guessing at what to execute.
        var json = """{"next_action":{"type":"set_plan","tool_name":"run_command"}}""";

        Assert.False(AgentService.TryRepairActionType(json, out _));
    }

    [Fact]
    public void A_response_naming_the_same_tool_in_both_places_is_repaired()
    {
        var json = """{"next_action":{"type":"read_file","tool_name":"read_file"}}""";

        Assert.True(AgentService.TryRepairActionType(json, out var repaired));
        using var doc = JsonDocument.Parse(repaired);
        Assert.Equal("tool", doc.RootElement.GetProperty("next_action").GetProperty("type").GetString());
        Assert.Equal("read_file", doc.RootElement.GetProperty("next_action").GetProperty("tool_name").GetString());
    }

    [Fact]
    public void Genuinely_malformed_json_is_not_repaired()
    {
        Assert.False(AgentService.TryRepairActionType("""{"next_action":{"type":"set_plan",""", out _));
        Assert.False(AgentService.TryRepairActionType("not json at all", out _));
    }

    [Fact]
    public void A_response_with_no_next_action_is_not_repaired()
    {
        Assert.False(AgentService.TryRepairActionType("""{"thought_summary":"hello"}""", out _));
    }

    [Fact]
    public void A_non_string_action_type_is_not_repaired()
    {
        Assert.False(AgentService.TryRepairActionType("""{"next_action":{"type":7}}""", out _));
        Assert.False(AgentService.TryRepairActionType("""{"next_action":{"type":{"nested":true}}}""", out _));
    }

    [Fact]
    public void The_repair_never_grants_approval_or_raises_risk()
    {
        // The repair corrects the shape of the request, never its authority:
        // whatever the model declared about approval and risk survives
        // unchanged, and the safety gate still classifies the tool itself.
        var json = """
            {"next_action":{"type":"run_command","tool_name":null,
             "arguments":{"command":"dotnet test"},"requires_approval":false,"risk_level":"none"}}
            """;

        Assert.True(AgentService.TryRepairActionType(json, out var repaired));

        using var doc = JsonDocument.Parse(repaired);
        var action = doc.RootElement.GetProperty("next_action");
        Assert.Equal("run_command", action.GetProperty("tool_name").GetString());
        Assert.False(action.GetProperty("requires_approval").GetBoolean());
        Assert.Equal("none", action.GetProperty("risk_level").GetString());
    }
}
