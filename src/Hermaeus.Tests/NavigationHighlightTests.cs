using System.Text.RegularExpressions;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// The nav rail had no indication of which panel you were on. The highlight
/// binds to the per-panel bools the view model already exposed, so the failure
/// mode is not a missing style but a bool that never raises PropertyChanged:
/// ShowActivity was absent from OnActivePanelChanged's notification list from
/// the day it was added, so anything bound to it stayed stale forever.
/// </summary>
public sealed class NavigationHighlightTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    private static string ViewModelSource =>
        File.ReadAllText(Path.Combine(RepoRoot, "src", "Hermaeus.ViewModels", "MainWindowViewModel.cs"));

    [Fact]
    public void Every_panel_bool_is_notified_when_the_active_panel_changes()
    {
        var source = ViewModelSource;

        var declared = Regex.Matches(source, @"public bool (Show\w+)\s*=> ActivePanel ==")
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.True(declared.Count >= 12, $"expected the per-panel bools, found {declared.Count}");

        var handler = source[source.IndexOf("partial void OnActivePanelChanged", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("\n    }", StringComparison.Ordinal)];

        var unnotified = declared
            .Where(name => !handler.Contains($"OnPropertyChanged(nameof({name}))", StringComparison.Ordinal))
            .ToList();

        Assert.True(unnotified.Count == 0,
            "OnActivePanelChanged does not raise PropertyChanged for these panel bools, so anything bound to "
            + "them (the nav highlight, panel visibility) never updates: " + string.Join(", ", unnotified));
    }

    [Fact]
    public void Every_nav_button_marks_itself_active()
    {
        var window = File.ReadAllText(Path.Combine(RepoRoot, "src", "Hermaeus.Desktop", "Views", "MainWindow.axaml"));

        var navCommands = Regex.Matches(window, @"<Button[^>]*Classes=""icon-btn""[^>]*Command=""\{Binding (Show(\w+)PanelCommand)\}""[^>]*")
            .Select(m => (Element: m.Value, Panel: m.Groups[2].Value))
            .ToList();
        Assert.True(navCommands.Count >= 12, $"expected the nav buttons, found {navCommands.Count}");

        var missing = navCommands
            .Where(n => !n.Element.Contains("Classes.nav-active", StringComparison.Ordinal))
            .Select(n => n.Panel)
            .ToList();

        Assert.True(missing.Count == 0,
            "These nav buttons do not mark themselves active, so the rail cannot show where you are: "
            + string.Join(", ", missing));
    }
}
