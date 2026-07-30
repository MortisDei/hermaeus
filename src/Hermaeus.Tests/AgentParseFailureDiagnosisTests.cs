using Hermaeus.Agent.Services;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// What the agent tells the user when a model's response cannot be parsed as the
/// JSON action format.
///
/// "The model's response could not be parsed as valid JSON" is true and useless:
/// the underlying causes (empty response, prose instead of JSON, output cut off
/// mid-structure) each need a different response from the user, and the raw text
/// used to be discarded so none of them could be told apart after the fact.
/// </summary>
public sealed class AgentParseFailureDiagnosisTests
{

    /// <summary>
    /// "The model's response could not be parsed as valid JSON" is true and
    /// useless: the three underlying causes need three different responses from
    /// the user. The owner hit this repeatedly with no way to tell which it was.
    /// </summary>
    [Fact]
    public void A_truncated_json_response_is_reported_as_truncation()
    {
        var cutOff = """{"thought_summary":"working on it","next_action":{"type":"tool","tool_name":"read_file""";

        var message = AgentService.DescribeParseFailure(cutOff);

        Assert.Contains("cut off", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("token limit", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_prose_only_response_is_reported_as_prose()
    {
        var message = AgentService.DescribeParseFailure(
            "Sure! I will now read the documents in the docs folder and report back.");

        Assert.Contains("prose", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cut off", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_empty_response_is_reported_as_empty()
    {
        var message = AgentService.DescribeParseFailure("   ");

        Assert.Contains("empty", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Balanced_but_invalid_json_falls_back_to_the_generic_message()
    {
        var message = AgentService.DescribeParseFailure("{ this is not json but the braces match }");

        Assert.Contains("could not be parsed", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The traced excerpt is bounded, so a huge malformed response cannot bloat
    /// the per-task trace file.</summary>
    [Fact]
    public void Parse_failure_excerpts_are_bounded_and_cover_both_ends()
    {
        var head = new string('a', 700);
        var tail = new string('z', 700);
        var raw = head + tail;

        var start = AgentService.Excerpt(raw, fromStart: true);
        var end = AgentService.Excerpt(raw, fromStart: false);

        Assert.Equal(600, start.Length);
        Assert.Equal(600, end.Length);
        Assert.StartsWith("aaa", start, StringComparison.Ordinal);
        Assert.EndsWith("zzz", end, StringComparison.Ordinal);

        // A short response needs no tail excerpt: the head already is the whole thing.
        Assert.Equal("short", AgentService.Excerpt("short", fromStart: true));
        Assert.Equal(string.Empty, AgentService.Excerpt("short", fromStart: false));
    }
}
