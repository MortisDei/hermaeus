using Xunit;

namespace Hermaeus.Tests;

public sealed class MainWindowAgentActionPromptTests
{
    [Fact]
    public void Main_window_keeps_a_pending_agent_action_visible_outside_the_agent_panel()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var view = File.ReadAllText(Path.Combine(root, "src", "Hermaeus.Desktop", "Views", "MainWindow.axaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "Hermaeus.ViewModels", "MainWindowViewModel.cs"));

        Assert.Contains("HasPendingAgentAction", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Agent.NextUserActionLabel", view, StringComparison.Ordinal);
        Assert.Contains("Open Agent", view, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"2\"", view, StringComparison.Ordinal);
        Assert.Contains("Agent.HasDecisionWaiting && !ShowAgent", viewModel, StringComparison.Ordinal);
    }
}
