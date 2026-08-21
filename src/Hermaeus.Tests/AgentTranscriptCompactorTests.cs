using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class AgentTranscriptCompactorTests
{
    [Fact]
    public void Compact_collapses_only_consecutive_identical_successful_outcomes()
    {
        var timestamp = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var first = SuccessfulEntry(1, "read_file", "README contents", new Dictionary<string, object?>
        {
            ["path"] = "README.md",
            ["line_limit"] = 20
        }, timestamp);
        var equivalentArgumentsInDifferentOrder = SuccessfulEntry(2, "read_file", "README contents", new Dictionary<string, object?>
        {
            ["line_limit"] = 20,
            ["path"] = "README.md"
        }, timestamp.AddSeconds(1));
        var third = SuccessfulEntry(3, "read_file", "README contents", new Dictionary<string, object?>
        {
            ["path"] = "README.md",
            ["line_limit"] = 20
        }, timestamp.AddSeconds(2));
        var differentArguments = SuccessfulEntry(4, "read_file", "README contents", new Dictionary<string, object?>
        {
            ["path"] = "CHANGELOG.md",
            ["line_limit"] = 20
        }, timestamp.AddSeconds(3));
        var denied = first with { Step = 5, Timestamp = timestamp.AddSeconds(4), ReplaySafe = false };
        var legacy = new AgentTranscriptEntry(6, "tool", "read_file", "README contents", timestamp.AddSeconds(5));

        var compacted = AgentTranscriptCompactor.Compact([first, equivalentArgumentsInDifferentOrder, third, differentArguments, denied, legacy]);

        Assert.Equal(4, compacted.Entries.Count);
        var repeated = compacted.Entries[0];
        Assert.Equal(3, repeated.RepeatCount);
        Assert.Contains("README contents", repeated.Entry.Content, StringComparison.Ordinal);
        Assert.Contains("steps 1-3", repeated.Entry.Content, StringComparison.Ordinal);
        var diagnostic = Assert.Single(compacted.Diagnostics);
        Assert.Equal("read_file", diagnostic.ToolName);
        Assert.Equal(3, diagnostic.Count);
        Assert.Equal(4, compacted.Entries[1].Entry.Step);
        Assert.Equal(5, compacted.Entries[2].Entry.Step);
        Assert.Equal(6, compacted.Entries[3].Entry.Step);
    }

    [Fact]
    public void Compact_preserves_nonconsecutive_and_differing_results()
    {
        var timestamp = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var first = SuccessfulEntry(1, "search_files", "one hit", new Dictionary<string, object?> { ["query"] = "TODO" }, timestamp);
        var assistant = new AgentTranscriptEntry(1, "assistant", null, "Checking another possibility.", timestamp.AddSeconds(1));
        var laterSame = first with { Step = 2, Timestamp = timestamp.AddSeconds(2) };
        var changedResult = first with { Step = 3, Content = "two hits", Timestamp = timestamp.AddSeconds(3) };

        var compacted = AgentTranscriptCompactor.Compact([first, assistant, laterSame, changedResult]);

        Assert.Equal(4, compacted.Entries.Count);
        Assert.Empty(compacted.Diagnostics);
    }

    [Fact]
    public void Compact_preserves_partial_outcomes()
    {
        var timestamp = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var partial = SuccessfulEntry(1, "read_file", "{\"truncated\":true}", new Dictionary<string, object?>
        {
            ["path"] = "README.md"
        }, timestamp);

        var compacted = AgentTranscriptCompactor.Compact([partial, partial with { Step = 2, Timestamp = timestamp.AddSeconds(1) }]);

        Assert.Collection(compacted.Entries, _ => { }, _ => { });
        Assert.All(compacted.Entries, entry => Assert.Equal(1, entry.RepeatCount));
        Assert.Empty(compacted.Diagnostics);
    }

    private static AgentTranscriptEntry SuccessfulEntry(
        int step,
        string tool,
        string result,
        Dictionary<string, object?> arguments,
        DateTime timestamp) =>
        AgentTranscriptCompactor.FromToolResult(step, new AgentToolResult
        {
            Tool = tool,
            Arguments = arguments,
            ResultSummary = result,
            Source = new Hermaeus.Core.Models.SourceReference(Hermaeus.Core.Models.ProvenanceKind.AgentTool, tool)
        }, timestamp);
}
