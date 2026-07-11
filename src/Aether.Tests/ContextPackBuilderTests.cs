using Aether.Core.Services;
using Xunit;

namespace Aether.Tests;

public sealed class ContextPackBuilderTests
{
    private static ContextPart Part(string title, int tokens, string? group = null) =>
        new("test", title, new string('x', tokens * 4), GroupKey: group, Tokens: tokens);

    [Fact]
    public void Packs_in_priority_order_until_budget_is_exhausted()
    {
        var packed = ContextPackBuilder.Pack(
            [Part("a", 100), Part("b", 100), Part("c", 100)],
            tokenBudget: 200);

        Assert.Equal(["a", "b"], packed.Parts.Select(p => p.Title));
        Assert.Equal(200, packed.TokensUsed);
        Assert.Contains("exhausted", packed.Summary);
    }

    [Fact]
    public void Part_limit_stops_selection_with_a_note()
    {
        var packed = ContextPackBuilder.Pack(
            [Part("a", 10), Part("b", 10), Part("c", 10)],
            tokenBudget: 10_000,
            maxParts: 2);

        Assert.Equal(2, packed.Parts.Count);
        Assert.Contains("part limit 2 reached", packed.Summary);
    }

    [Fact]
    public void Per_group_cap_skips_extra_parts_from_the_same_source()
    {
        var packed = ContextPackBuilder.Pack(
            [Part("a1", 10, "a"), Part("a2", 10, "a"), Part("b1", 10, "b")],
            tokenBudget: 10_000,
            maxPerGroup: 1);

        Assert.Equal(["a1", "b1"], packed.Parts.Select(p => p.Title));
    }

    [Fact]
    public void Last_part_is_truncated_to_fit_and_flagged()
    {
        var packed = ContextPackBuilder.Pack(
            [Part("a", 100), Part("big", 500)],
            tokenBudget: 200);

        Assert.Equal(2, packed.Parts.Count);
        Assert.True(packed.Parts[1].Truncated);
        Assert.True(packed.Parts[1].Content.Length < 500 * 4);
        Assert.Contains("truncated big", packed.Summary);
    }

    [Fact]
    public void Oversized_single_part_is_truncated_rather_than_dropped()
    {
        var packed = ContextPackBuilder.Pack(
            [Part("huge", 100_000)],
            tokenBudget: 128);

        var part = Assert.Single(packed.Parts);
        Assert.True(part.Truncated);
        Assert.Contains("truncated huge", packed.Summary);
    }

    [Fact]
    public void Fallback_keeps_the_first_candidate_when_selection_ends_empty()
    {
        // Whitespace content truncates to nothing, so normal selection skips it.
        var whitespace = new ContextPart("test", "blank", new string(' ', 100_000), Tokens: 100_000);
        var packed = ContextPackBuilder.Pack([whitespace], tokenBudget: 128);

        var part = Assert.Single(packed.Parts);
        Assert.True(part.Truncated);
        Assert.Contains("fallback", packed.Summary);
    }

    [Fact]
    public void Estimates_tokens_at_roughly_four_chars_each_when_not_supplied()
    {
        Assert.Equal(0, ContextPackBuilder.EstimateTokens(""));
        Assert.Equal(1, ContextPackBuilder.EstimateTokens("ab"));
        Assert.Equal(25, ContextPackBuilder.EstimateTokens(new string('x', 100)));
        Assert.Equal(25, new ContextPart("k", "t", new string('x', 100)).EffectiveTokens);
    }
}
