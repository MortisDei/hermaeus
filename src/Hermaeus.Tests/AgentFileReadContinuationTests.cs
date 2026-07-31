using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// A truncated read is not a dead end, and the result has to say so. A real
/// run concluded "the tool cannot return the entire file content in one go"
/// and abandoned the file, then went hunting for the symbol elsewhere: the
/// result said only "truncated": true, and nothing pointed at line_offset.
/// </summary>
public sealed class AgentFileReadContinuationTests
{
    [Fact]
    public void A_complete_read_offers_no_continuation()
    {
        var result = new AgentFileReadResult("a.cs", "all of it", Truncated: false);

        Assert.Equal(string.Empty, result.ContinuationHint);
    }

    [Fact]
    public void A_truncated_line_range_names_the_exact_next_offset()
    {
        var result = new AgentFileReadResult("a.cs", "...", Truncated: true,
            TotalLines: 900, LineOffset: 0, LineCount: 400);

        Assert.Contains("lines 1 to 400 of 900", result.ContinuationHint, StringComparison.Ordinal);
        Assert.Contains("line_offset=400", result.ContinuationHint, StringComparison.Ordinal);
    }

    [Fact]
    public void A_later_slice_continues_from_where_it_stopped()
    {
        var result = new AgentFileReadResult("a.cs", "...", Truncated: true,
            TotalLines: 900, LineOffset: 400, LineCount: 400);

        Assert.Contains("lines 401 to 800 of 900", result.ContinuationHint, StringComparison.Ordinal);
        Assert.Contains("line_offset=800", result.ContinuationHint, StringComparison.Ordinal);
    }

    [Fact]
    public void A_truncated_read_with_no_line_information_still_says_how_to_continue()
    {
        var result = new AgentFileReadResult("a.cs", "...", Truncated: true);

        Assert.Contains("line_offset", result.ContinuationHint, StringComparison.Ordinal);
        Assert.Contains("line_limit", result.ContinuationHint, StringComparison.Ordinal);
    }

    [Fact]
    public void Reading_a_slice_reports_the_totals_needed_to_ask_for_the_next_one()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        File.WriteAllLines(Path.Combine(workspace, "big.cs"),
            Enumerable.Range(1, 500).Select(i => $"// line {i}"));

        var tools = new AgentWorkspaceTools();
        var options = new AgentWorkspaceOptions(workspace);

        var first = tools.ReadFile(options, "big.cs", lineOffset: 0, lineLimit: 200);

        Assert.True(first.Truncated);
        Assert.Equal(500, first.TotalLines);
        Assert.Equal(0, first.LineOffset);
        Assert.Equal(200, first.LineCount);
        Assert.Contains("line_offset=200", first.ContinuationHint, StringComparison.Ordinal);
    }

    [Fact]
    public void Following_the_hint_reaches_the_end_of_the_file()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        File.WriteAllLines(Path.Combine(workspace, "big.cs"),
            Enumerable.Range(1, 500).Select(i => $"// line {i}"));

        var tools = new AgentWorkspaceTools();
        var options = new AgentWorkspaceOptions(workspace);

        // Exactly what the hint tells the model to do, three times over.
        var offset = 0;
        AgentFileReadResult slice;
        var collected = new List<string>();
        do
        {
            slice = tools.ReadFile(options, "big.cs", lineOffset: offset, lineLimit: 200);
            collected.AddRange(slice.Content.Split('\n'));
            offset += slice.LineCount ?? 0;
        }
        while (slice.Truncated && offset < 5000);

        Assert.False(slice.Truncated);
        Assert.Equal(500, collected.Count);
        Assert.Equal("// line 1", collected[0]);
        Assert.Equal("// line 500", collected[^1]);
    }

    [Fact]
    public void A_whole_small_file_is_not_reported_as_truncated()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "small.cs"), "one\ntwo\nthree");

        var result = new AgentWorkspaceTools().ReadFile(new AgentWorkspaceOptions(workspace), "small.cs");

        Assert.False(result.Truncated);
        Assert.Equal(string.Empty, result.ContinuationHint);
        Assert.Contains("three", result.Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file over the whole-file byte cap could not be read at all: the size
    /// gate ran before the ranged path, so line_offset could not rescue it and
    /// the caller was told only that the file was "too large". A bounded
    /// window is exactly what a ranged read is for.
    /// </summary>
    [Fact]
    public void A_file_over_the_whole_file_cap_can_still_be_read_in_slices()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var line = "// a padded source line that makes this file comfortably large for the test";
        File.WriteAllLines(Path.Combine(workspace, "huge.cs"), Enumerable.Repeat(line, 4000));

        var tools = new AgentWorkspaceTools();
        // Smaller than the file, which is the condition that used to refuse it.
        var options = new AgentWorkspaceOptions(workspace) { MaxFileBytes = 8 * 1024 };

        var slice = tools.ReadFile(options, "huge.cs", lineOffset: 0, lineLimit: 100);

        Assert.Equal(100, slice.LineCount);
        Assert.Equal(4000, slice.TotalLines);
        Assert.True(slice.Truncated);
        Assert.Contains("line_offset=100", slice.ContinuationHint, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unranged_read_of_an_oversized_file_says_to_read_it_in_slices()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        File.WriteAllLines(Path.Combine(workspace, "huge.cs"),
            Enumerable.Repeat("// a padded source line for the test", 4000));

        var tools = new AgentWorkspaceTools();
        var options = new AgentWorkspaceOptions(workspace) { MaxFileBytes = 8 * 1024 };

        var error = Assert.Throws<InvalidOperationException>(() => tools.ReadFile(options, "huge.cs"));

        Assert.Contains("line_offset", error.Message, StringComparison.Ordinal);
        Assert.Contains("slices", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
