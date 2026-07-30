using System.Text.RegularExpressions;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// r25 doc 05 5.1: documentation drift, caught mechanically.
///
/// The failure this exists to prevent is specific and it already happened: r24
/// shipped Projects, Recall, the command palette, watched sources, the Activity
/// feed and speech input, and README's feature narrative mentioned none of them,
/// while the documentation index in the same file was updated correctly. The
/// index is an obvious checklist item; the narrative was not, because nothing
/// checked it. Anyone opening the repository read a description of 0.30.0.
///
/// This repository already prefers a guard test to a process reminder:
/// <c>HarnessRegistrationGuardTests</c> for unregistered tests,
/// <c>NamingConsistencyTests</c> for rename drift, an axaml scan for missing
/// tooltips. Documentation gets the same treatment.
///
/// Deliberately dumb: one regex over one file, then a case-insensitive substring
/// check. A guard that needs maintenance is a guard that gets deleted the first
/// time it is inconvenient.
/// </summary>
public sealed class DocsCoverageGuardTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    /// <summary>
    /// The navigation panels are the app's own answer to "what can this do", so
    /// they are the right thing to hold the docs to.
    /// </summary>
    internal static IReadOnlyList<string> NavigationPanels()
    {
        var axaml = File.ReadAllText(
            Path.Combine(RepoRoot, "src", "Hermaeus.Desktop", "Views", "MainWindow.axaml"));

        var panels = Regex.Matches(axaml, @"Show(\w+?)PanelCommand")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(panels.Count >= 10,
            $"expected to find the app's navigation panels in MainWindow.axaml, found {panels.Count}");
        return panels;
    }

    [Fact]
    public void Every_navigation_panel_is_named_in_the_readme()
    {
        var readme = File.ReadAllText(Path.Combine(RepoRoot, "README.md"));

        var missing = NavigationPanels()
            .Where(p => !readme.Contains(p, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(missing.Count == 0,
            "README.md does not mention these navigation panels: " + string.Join(", ", missing) +
            ". A shipped capability the front page never names is a capability nobody finds.");
    }

    [Fact]
    public void Every_navigation_panel_is_named_in_features_doc()
    {
        var features = File.ReadAllText(Path.Combine(RepoRoot, "docs", "features.md"));

        var missing = NavigationPanels()
            .Where(p => !features.Contains(p, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(missing.Count == 0,
            "docs/features.md does not mention these navigation panels: " + string.Join(", ", missing));
    }

    /// <summary>
    /// A guard that cannot fail is worse than none, so the detection itself is
    /// tested rather than trusted.
    /// </summary>
    [Fact]
    public void The_guard_detects_a_missing_panel()
    {
        var panels = NavigationPanels();
        const string pretendReadme = "Hermaeus has a Chat panel and nothing else worth naming.";

        var missing = panels
            .Where(p => !pretendReadme.Contains(p, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(missing);
        Assert.DoesNotContain("Chat", missing);
    }

    /// <summary>r25 doc 05 5.2: the deferred-item ledger has to exist and be reachable,
    /// or it is not a ledger, it is a file.</summary>
    [Fact]
    public void The_deferred_item_ledger_exists_and_is_linked_from_the_readme()
    {
        var ledger = Path.Combine(RepoRoot, "docs", "review", "deferred.md");
        Assert.True(File.Exists(ledger), "docs/review/deferred.md must exist (r25 doc 05 5.2)");

        var content = File.ReadAllText(ledger);
        Assert.True(content.Length > 500, "the ledger should carry the actual audit, not a placeholder");

        var readme = File.ReadAllText(Path.Combine(RepoRoot, "README.md"));
        Assert.Contains("docs/review/deferred.md", readme, StringComparison.Ordinal);
    }
}
