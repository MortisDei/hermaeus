using Avalonia;
using Avalonia.Controls;
using Hermaeus.Desktop.Views;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ConfirmActionDialogTests
{
    [Fact]
    public void Confirmation_dialog_position_is_centered_inside_its_owner_bounds()
    {
        var position = ConfirmActionDialog.CenterOnOwner(
            new PixelPoint(2400, 180), new Size(1800, 1200), new Size(460, 200));

        Assert.Equal(new PixelPoint(3070, 680), position);
    }

    [Fact]
    public void Modal_view_call_sites_pass_an_explicit_owner_window()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", "src", "Hermaeus.Desktop", "Views"));
        var calls = Directory.EnumerateFiles(root, "*.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(path => File.ReadLines(path).Select(line => (path, line)))
            .Where(item => item.line.Contains("ShowDialog", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(calls);
        Assert.All(calls, call => Assert.DoesNotContain("ShowDialog()", call.line, StringComparison.Ordinal));
    }
}
