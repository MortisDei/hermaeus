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

    /// <summary>
    /// r27 05-small-open-items.md 5.2: the README's version sat at 0.24.0-alpha
    /// while Directory.Build.props said 0.33.0. The gap opened at r24 and no
    /// close-out since closed it, on the front page of a repository being
    /// prepared to go public.
    /// This class existed the whole time and passed the whole time, because it
    /// asserted that navigation panel NAMES appear in the README and never
    /// looked at the version. The lesson is the general one: a guard covers what
    /// it asserts and nothing else.
    /// </summary>
    [Fact]
    public void The_readme_states_the_version_this_build_ships()
    {
        var props = File.ReadAllText(Path.Combine(RepoRoot, "Directory.Build.props"));
        var match = System.Text.RegularExpressions.Regex.Match(props, @"<VersionPrefix>\s*([^<\s]+)\s*</VersionPrefix>");
        Assert.True(match.Success, "Directory.Build.props should declare a <VersionPrefix>.");

        var version = match.Groups[1].Value;
        var readme = File.ReadAllText(Path.Combine(RepoRoot, "README.md"));

        Assert.True(readme.Contains(version, StringComparison.Ordinal),
            $"README.md does not mention version {version}. Directory.Build.props says <VersionPrefix>{version}</VersionPrefix>; " +
            $"update the version line under \"Current Status\" in README.md to **{version}-alpha** as part of this version bump.");
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

    // ── r28 doc 06 6.2: facts that are enumerated in code and in prose ──
    //
    // Same style as everything above: assert that a name appears in a file.
    // Not prose analysis, not schema comparison, and explicitly not
    // generating documentation from code, which r25 rejected.

    /// <summary>
    /// Every tool the safety gate classifies by name appears in the risk
    /// table's section of docs/agent.md. Landed after 6.1, so it pins a
    /// resolved answer rather than freezing a discrepancy: the table's Safe
    /// row had been missing three read-only tools, and run_command's real
    /// route was undocumented.
    /// </summary>
    [Fact]
    public void Every_tool_the_gate_classifies_is_named_in_the_agent_risk_table()
    {
        var gate = File.ReadAllText(Path.Combine(RepoRoot, "src", "Hermaeus.Agent", "Services", "AgentSafetyGate.cs"));
        var section = RiskLevelsSection();

        var tools = Regex.Matches(gate, @"^\s*""([a-z_]+)"",?\s*$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(tools.Count >= 15,
            $"expected to find the gate's tool sets in AgentSafetyGate.cs, found {tools.Count}");

        var missing = tools.Where(t => !section.Contains(t, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(missing.Count == 0,
            "docs/agent.md's \"Action Risk Levels\" section does not name these tools the gate classifies: "
            + string.Join(", ", missing));
    }

    /// <summary>The risk table's own section, so a tool named elsewhere in the doc does not count.</summary>
    private static string RiskLevelsSection()
    {
        var agent = File.ReadAllText(Path.Combine(RepoRoot, "docs", "agent.md"));
        var start = agent.IndexOf("## Action Risk Levels", StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/agent.md should have an \"Action Risk Levels\" section");

        var end = agent.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        return end < 0 ? agent[start..] : agent[start..end];
    }

    [Fact]
    public void The_agent_risk_guard_detects_a_tool_the_table_forgot()
    {
        // A guard that cannot fail is worse than none.
        const string pretendSection = "## Action Risk Levels\n| Safe | read_file | execute directly |\n";
        Assert.DoesNotContain("delete_file", pretendSection, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("read_file", pretendSection, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// docs/benchmarks.md claims a specific set of run metadata is recorded.
    /// Assert every property on <c>BenchmarkMetadata</c> is named there, so a
    /// field added to the record cannot quietly go undocumented.
    /// </summary>
    [Fact]
    public void Every_recorded_benchmark_metadata_field_is_named_in_the_benchmarks_doc()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "src", "Hermaeus.Core", "Models", "BenchmarkMetadata.cs"));
        var benchmarks = File.ReadAllText(Path.Combine(RepoRoot, "docs", "benchmarks.md"));

        var fields = Regex.Matches(source, @"public\s+[\w?<>\.]+\s+(\w+)\s*\{\s*get;\s*set;\s*\}")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(fields.Count >= 20,
            $"expected to find BenchmarkMetadata's properties, found {fields.Count}");

        var missing = fields.Where(f => !benchmarks.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(missing.Count == 0,
            "docs/benchmarks.md does not name these recorded metadata fields: " + string.Join(", ", missing));
    }

    /// <summary>
    /// CLAUDE.md enumerates the settings domain sections. Assert the list and
    /// <c>AppSettings</c> agree in both directions: a section CLAUDE.md does
    /// not name is as much a drift as a name with no section behind it.
    /// </summary>
    [Fact]
    public void The_settings_section_list_and_AppSettings_agree_in_both_directions()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "src", "Hermaeus.Core", "Models", "AppSettings.cs"));
        var claude = File.ReadAllText(Path.Combine(RepoRoot, "CLAUDE.md"));

        var sections = Regex.Matches(source, @"public\s+(\w+Settings)\s+(\w+)\s*\{\s*get;\s*set;\s*\}\s*=\s*new\(\);")
            .Select(m => m.Groups[2].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(sections.Count >= 9,
            $"expected to find AppSettings' domain sections, found {sections.Count}");

        var listed = Regex.Match(claude, @"domain sections on `AppSettings` \(([^)]+)\)");
        Assert.True(listed.Success, "CLAUDE.md should enumerate the AppSettings domain sections.");
        var named = listed.Groups[1].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        var undocumented = sections.Where(s => !named.Contains(s, StringComparer.Ordinal)).ToList();
        Assert.True(undocumented.Count == 0,
            "CLAUDE.md's settings-section list does not name these sections on AppSettings: " + string.Join(", ", undocumented));

        var phantom = named
            .Where(n => Regex.IsMatch(n, @"^\w+$") && !sections.Contains(n, StringComparer.Ordinal))
            .ToList();
        Assert.True(phantom.Count == 0,
            "CLAUDE.md names these settings sections, which are not properties on AppSettings: " + string.Join(", ", phantom));
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
