using Hermaeus.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hermaeus.Desktop.Views;

public partial class OrganizeModelsPreviewDialog : Window
{
    public OrganizeModelsPreviewDialog()
    {
        InitializeComponent();
    }

    public void SetPlan(ModelOrganizePlan plan)
    {
        SummaryText.Text = $"{plan.Moves.Count} file(s) to move, {plan.Skips.Count} skipped due to name collisions, "
            + $"{plan.ProvenanceCount} will record Hugging Face provenance for update checks.";

        var items = new List<Control>();
        foreach (var move in plan.Moves)
        {
            for (var i = 0; i < move.SourcePaths.Count; i++)
            {
                var label = move.HubRepoOrg is not null && i == 0
                    ? $"{move.SourcePaths[i]} -> {move.DestinationPaths[i]}  [{move.HubRepoOrg}/{move.HubRepoName}]"
                    : $"{move.SourcePaths[i]} -> {move.DestinationPaths[i]}";
                items.Add(new TextBlock { Text = label, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 0, 0, 4) });
            }
        }
        foreach (var skip in plan.Skips)
        {
            items.Add(new TextBlock
            {
                Text = $"Skipped: {skip.SourcePath} ({skip.Reason})",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Opacity = 0.6,
                Margin = new Avalonia.Thickness(0, 0, 0, 4)
            });
        }
        MovesList.ItemsSource = items;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);
}
