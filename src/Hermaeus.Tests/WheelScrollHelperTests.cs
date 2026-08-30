using Avalonia;
using Hermaeus.Desktop.Views;
using Xunit;

namespace Hermaeus.Tests;

public sealed class WheelScrollHelperTests
{
    [Fact]
    public void Vertical_wheel_clamps_to_content_and_reports_movement()
    {
        var moved = WheelScrollHelper.TryCalculateOffset(
            new Vector(0, 100), new Size(500, 1000), new Size(500, 400), new Vector(0, -2), out var next);

        Assert.True(moved);
        Assert.Equal(212, next.Y);
        Assert.Equal(0, next.X);
    }

    [Fact]
    public void Horizontal_wheel_is_preserved_and_edges_report_no_movement()
    {
        var moved = WheelScrollHelper.TryCalculateOffset(
            new Vector(100, 0), new Size(900, 400), new Size(400, 400), new Vector(2, 0), out var next);
        var atEdge = WheelScrollHelper.TryCalculateOffset(
            new Vector(500, 0), new Size(900, 400), new Size(400, 400), new Vector(-2, 0), out var unchanged);

        Assert.True(moved);
        Assert.Equal(0, next.X);
        Assert.False(atEdge);
        Assert.Equal(500, unchanged.X);
    }
}
