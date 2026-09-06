using Hermaeus.Desktop.Views;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ConfirmActionDialogTests
{
    [Fact]
    public void Confirmation_dialog_uses_the_framework_owner_placement()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/Hermaeus.Desktop/Views/ConfirmActionDialog.axaml");
        var markup = File.ReadAllText(path);
        Assert.Contains("WindowStartupLocation=\"CenterOwner\"", markup, StringComparison.Ordinal);

        var code = File.ReadAllText(Path.ChangeExtension(path, ".axaml.cs"));
        Assert.DoesNotContain("WindowStartupLocation.Manual", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Position =", code, StringComparison.Ordinal);
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
