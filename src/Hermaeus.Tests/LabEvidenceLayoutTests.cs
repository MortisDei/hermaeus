using System.Xml.Linq;
using Xunit;

namespace Hermaeus.Tests;

public sealed class LabEvidenceLayoutTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public void Evidence_detail_uses_structured_result_data_and_keeps_raw_drilldown()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "src", "Hermaeus.Desktop", "Views", "LabView.axaml"));

        Assert.Contains("SelectedExperience.ResultDetails.ExperimentLabel", source, StringComparison.Ordinal);
        Assert.Contains("SelectedExperience.ResultDetails.Comparisons", source, StringComparison.Ordinal);
        Assert.Contains("Execution evidence details", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding SelectedExperience.ResultSummary}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Evidence_refresh_clears_selection_before_rebuilding_the_bound_rows()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "src", "Hermaeus.ViewModels", "LabViewModel.cs"));
        var clearSelection = source.IndexOf("SelectedExperience = null;", StringComparison.Ordinal);
        var clearRows = source.IndexOf("Experiences.Clear();", clearSelection, StringComparison.Ordinal);

        Assert.True(clearSelection >= 0, "Evidence refresh must explicitly clear the prior selected row.");
        Assert.True(clearRows > clearSelection, "The stale selection must be cleared before the ItemsSource is rebuilt.");
    }

    [Fact]
    public void Model_configuration_flyout_has_work_area_constraints_without_a_fixed_width()
    {
        var path = Path.Combine(RepoRoot, "src", "Hermaeus.Desktop", "Views", "ModelManagementView.axaml");
        var doc = XDocument.Load(path);
        var flyout = doc.Descendants().Single(element => element.Name.LocalName == "Flyout"
            && (string?)element.Attribute("Placement") == "BottomEdgeAlignedRight");
        var scrollViewer = flyout.Descendants().Single(element => element.Name.LocalName == "ScrollViewer");

        Assert.Equal("SlideX,FlipY,ResizeX,ResizeY", (string?)flyout.Attribute("PlacementConstraintAdjustment"));
        Assert.Null((string?)scrollViewer.Attribute("Width"));
        Assert.Equal("720", (string?)scrollViewer.Attribute("MaxWidth"));
        Assert.Equal("520", (string?)scrollViewer.Attribute("MaxHeight"));
        Assert.Equal("Auto", (string?)scrollViewer.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)scrollViewer.Attribute("HorizontalScrollBarVisibility"));

        var modelList = doc.Descendants().Single(element => element.Name.LocalName == "ScrollViewer"
            && element.Attributes().Any(attribute => attribute.Name.LocalName == "Name"
                && (string?)attribute == "ModelListScrollViewer"));
        Assert.Equal("Disabled", (string?)modelList.Attribute("HorizontalScrollBarVisibility"));
    }
}
