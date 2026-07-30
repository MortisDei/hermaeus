using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// The workbench set one status message when a run started and did not touch
/// it again until a step finished, so a long model call left every label
/// frozen and the panel read as hung. This is the rotating line that fixes it,
/// kept pure so it tests without a live run or a real timer.
/// </summary>
public sealed class AgentActivityPhaseTests
{
    [Fact]
    public void An_idle_agent_shows_nothing()
    {
        Assert.Equal(string.Empty, AgentActivityPhase.Describe(60_000, isRunning: false, step: 3));
    }

    [Fact]
    public void A_step_faster_than_the_grace_window_never_flickers_a_placeholder()
    {
        Assert.Equal(string.Empty, AgentActivityPhase.Describe(AgentActivityPhase.GraceMs - 1, isRunning: true));
        Assert.NotEqual(string.Empty, AgentActivityPhase.Describe(AgentActivityPhase.GraceMs, isRunning: true));
    }

    [Fact]
    public void The_line_names_the_step_and_the_elapsed_seconds()
    {
        var text = AgentActivityPhase.Describe(12_400, isRunning: true, step: 3, wordIndex: 0);

        Assert.StartsWith("Step 3: ", text, StringComparison.Ordinal);
        Assert.EndsWith("... 12s", text, StringComparison.Ordinal);
        Assert.Contains(AgentActivityPhase.WhimsyWords[0], text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unnumbered_step_omits_the_prefix_rather_than_saying_step_zero()
    {
        var text = AgentActivityPhase.Describe(5_000, isRunning: true, step: 0);

        Assert.DoesNotContain("Step", text, StringComparison.Ordinal);
        Assert.EndsWith("... 5s", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_word_rotates_with_the_index_and_wraps_in_both_directions()
    {
        var count = AgentActivityPhase.WhimsyWords.Count;
        var first = AgentActivityPhase.Describe(5_000, true, 1, wordIndex: 0);
        var second = AgentActivityPhase.Describe(5_000, true, 1, wordIndex: 1);
        var wrapped = AgentActivityPhase.Describe(5_000, true, 1, wordIndex: count);
        var negative = AgentActivityPhase.Describe(5_000, true, 1, wordIndex: -1);

        Assert.NotEqual(first, second);
        Assert.Equal(first, wrapped);
        Assert.Equal(AgentActivityPhase.Describe(5_000, true, 1, wordIndex: count - 1), negative);
    }

    [Fact]
    public void The_same_elapsed_time_always_produces_the_same_line()
    {
        Assert.Equal(
            AgentActivityPhase.Describe(30_000, true, 7, 4),
            AgentActivityPhase.Describe(30_000, true, 7, 4));
    }

    [Fact]
    public void Every_word_in_the_pool_is_reachable_and_non_empty()
    {
        Assert.NotEmpty(AgentActivityPhase.WhimsyWords);
        Assert.All(AgentActivityPhase.WhimsyWords, word => Assert.False(string.IsNullOrWhiteSpace(word)));

        var seen = Enumerable.Range(0, AgentActivityPhase.WhimsyWords.Count)
            .Select(i => AgentActivityPhase.Describe(5_000, true, 1, i))
            .Distinct()
            .Count();
        Assert.Equal(AgentActivityPhase.WhimsyWords.Count, seen);
    }
}
