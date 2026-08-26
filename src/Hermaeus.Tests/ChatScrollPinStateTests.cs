using Hermaeus.Desktop;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ChatScrollPinStateTests
{
    [Fact]
    public void Pinned_extent_growth_requests_bottom_snap()
    {
        var result = ChatScrollPinState.Apply(true, 1200, 400, 800, 20);

        Assert.True(result.IsPinned);
        Assert.True(result.ShouldSnap);
    }

    [Fact]
    public void User_scroll_up_unpins_and_extent_growth_preserves_position()
    {
        var scrolled = ChatScrollPinState.Apply(true, 1200, 400, 700, 0);
        var grown = ChatScrollPinState.Apply(scrolled.IsPinned, 1220, 400, 700, 20);

        Assert.False(scrolled.IsPinned);
        Assert.False(grown.IsPinned);
        Assert.False(grown.ShouldSnap);
    }

    [Fact]
    public void Returning_within_threshold_repins_without_forcing_a_snap()
    {
        var result = ChatScrollPinState.Apply(false, 1200, 400, 765, 0);

        Assert.True(result.IsPinned);
        Assert.False(result.ShouldSnap);
    }

    [Fact]
    public void Completion_growth_while_unpinned_does_not_snap()
    {
        var result = ChatScrollPinState.Apply(false, 1200, 400, 600, 80);

        Assert.False(result.IsPinned);
        Assert.False(result.ShouldSnap);
    }

    [Fact]
    public void Extent_growth_after_scroll_up_does_not_repin_the_view()
    {
        var result = ChatScrollPinState.Apply(true, 1220, 400, 700, 20);

        Assert.False(result.IsPinned);
        Assert.False(result.ShouldSnap);
    }
}
