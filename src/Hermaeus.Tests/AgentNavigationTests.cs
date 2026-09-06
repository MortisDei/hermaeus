using Xunit;

namespace Hermaeus.Tests;

public sealed class AgentNavigationTests
{
    [Fact]
    public void Agent_workspace_exposes_one_tab_navigation_strip()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var view = File.ReadAllText(Path.Combine(repoRoot, "src", "Hermaeus.Desktop", "Views", "AgentView.axaml"));

        Assert.DoesNotContain("Agent workspace", view, StringComparison.Ordinal);
        Assert.Equal(1, view.Split("<TabControl", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Content=\"Run\" Command=\"{Binding ShowRunTabCommand}\"", view, StringComparison.Ordinal);
        Assert.Contains("Header=\"Run\"", view, StringComparison.Ordinal);
        Assert.Contains("Header=\"Changes\"", view, StringComparison.Ordinal);
        Assert.Contains("Header=\"Workspace\"", view, StringComparison.Ordinal);
        Assert.Contains("Header=\"History\"", view, StringComparison.Ordinal);
    }
}
