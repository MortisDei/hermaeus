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
    public void Model_configuration_subview_has_work_area_constraints_without_a_fixed_width()
    {
        var path = Path.Combine(RepoRoot, "src", "Hermaeus.Desktop", "Views", "ModelManagementView.axaml");
        var doc = XDocument.Load(path);
        var editor = doc.Descendants().Single(element => element.Name.LocalName == "Border"
            && (string?)element.Attribute("IsVisible") == "{Binding HasSelectedProfile}");
        var scrollViewer = editor.Descendants().Single(element => element.Name.LocalName == "ScrollViewer");

        Assert.Null((string?)scrollViewer.Attribute("Width"));
        Assert.Equal("620", (string?)scrollViewer.Attribute("MaxHeight"));
        Assert.Equal("Auto", (string?)scrollViewer.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)scrollViewer.Attribute("HorizontalScrollBarVisibility"));
        Assert.Contains(editor.Descendants(), element => element.Name.LocalName == "Button"
            && (string?)element.Attribute("Content") == "Save model profile");

        var modelList = doc.Descendants().Single(element => element.Name.LocalName == "ScrollViewer"
            && element.Attributes().Any(attribute => attribute.Name.LocalName == "Name"
                && (string?)attribute == "ModelListScrollViewer"));
        Assert.Equal("Disabled", (string?)modelList.Attribute("HorizontalScrollBarVisibility"));
    }

    [Fact]
    public void Model_cards_use_equal_grid_tracks_without_clipping_variable_content()
    {
        var path = Path.Combine(RepoRoot, "src", "Hermaeus.Desktop", "Views", "ModelManagementView.axaml");
        var doc = XDocument.Load(path);
        var cards = doc.Descendants().Single(element => element.Name.LocalName == "ItemsControl"
            && (string?)element.Attribute("ItemsSource") == "{Binding Models}");
        var panel = cards.Descendants().Single(element => element.Name.LocalName == "ItemsPanelTemplate");
        var uniformGrid = panel.Elements().Single(element => element.Name.LocalName == "UniformGrid");

        Assert.Equal("3", (string?)uniformGrid.Attribute("Columns"));
        var card = cards.Descendants().Single(element => element.Name.LocalName == "Border"
            && (string?)element.Attribute("MinHeight") == "320");
        Assert.Null((string?)card.Attribute("MaxHeight"));
        Assert.Null((string?)card.Attribute("Width"));
    }

    [Fact]
    public void Model_cards_wrap_primary_metadata_inside_the_card_and_keep_tune_metadata_separate()
    {
        var path = Path.Combine(RepoRoot, "src", "Hermaeus.Desktop", "Views", "ModelManagementView.axaml");
        var doc = XDocument.Load(path);
        var cards = doc.Descendants().Single(element => element.Name.LocalName == "ItemsControl"
            && (string?)element.Attribute("ItemsSource") == "{Binding Models}");
        var card = cards.Descendants().Single(element => element.Name.LocalName == "Border"
            && (string?)element.Attribute("MinHeight") == "320");
        var primaryRow = card.Descendants().Single(element => element.Name.LocalName == "WrapPanel"
            && (string?)element.Attribute("IsVisible") == "{Binding RoleLabel, Converter={x:Static views:NotEmptyConverter.Instance}}");

        Assert.Equal("6", (string?)primaryRow.Attribute("ItemSpacing"));
        Assert.Equal("4", (string?)primaryRow.Attribute("LineSpacing"));
        Assert.Null((string?)primaryRow.Attribute("Width"));
        Assert.Null((string?)primaryRow.Attribute("MinWidth"));
        Assert.Null((string?)primaryRow.Attribute("MaxWidth"));
        Assert.Null((string?)card.Attribute("Width"));
        Assert.Null((string?)card.Attribute("MinWidth"));

        var primaryBindings = primaryRow
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => (string?)element.Attribute("Text"))
            .ToList();
        Assert.Contains("{Binding RoleLabel}", primaryBindings);
        Assert.Contains("{Binding CapabilityBadges}", primaryBindings);
        Assert.Contains("{Binding SizeDisplay}", primaryBindings);
        Assert.Contains("{Binding FitLabel, Converter={x:Static views:NotEmptyConverter.Instance}}", primaryRow.Descendants().Select(element => (string?)element.Attribute("IsVisible")));
        Assert.Contains("{Binding UpdateLabel, Converter={x:Static views:NotEmptyConverter.Instance}}", primaryRow.Descendants().Select(element => (string?)element.Attribute("IsVisible")));

        var statusBadges = primaryRow.Descendants().Where(element => element.Name.LocalName == "Border").ToList();
        Assert.Contains(statusBadges, border => border.Descendants().Any(element => (string?)element.Attribute("Text") == "{Binding FitLabel}"));
        Assert.Contains(statusBadges, border => border.Descendants().Any(element => (string?)element.Attribute("Text") == "{Binding UpdateLabel}"));

        var tunePanel = doc.Descendants().Single(element => element.Name.LocalName == "Border"
            && (string?)element.Attribute("IsVisible") == "{Binding TuneSummary, Converter={x:Static views:NotEmptyConverter.Instance}}");
        Assert.NotSame(tunePanel, primaryRow);
        Assert.DoesNotContain(primaryBindings, binding => binding?.Contains("Tuned", StringComparison.Ordinal) == true);
        Assert.Contains(tunePanel.Descendants(), element =>
            ((string?)element.Attribute("Text"))?.Contains("Tuned", StringComparison.Ordinal) == true);
    }
}
