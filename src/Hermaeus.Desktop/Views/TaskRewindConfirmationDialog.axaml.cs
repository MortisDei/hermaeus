using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

/// <summary>
/// The mandatory pre-Rewind confirmation (r23 1.4): lists exactly which
/// files will be restored and which will be deleted before anything runs.
/// There is no "do not ask again" - this is a destructive-adjacent action.
/// </summary>
public partial class TaskRewindConfirmationDialog : Window
{
    public TaskRewindConfirmationDialog()
    {
        InitializeComponent();
    }

    public void SetPlan(AgentTaskRewindConfirmation plan)
    {
        SummaryText.Text = $"{plan.FilesToRestore.Count} file(s) to restore, {plan.FilesToDelete.Count} file(s) to delete.";
        RestorePanel.IsVisible = plan.FilesToRestore.Count > 0;
        DeletePanel.IsVisible = plan.FilesToDelete.Count > 0;
        RestoreList.ItemsSource = ToTextBlocks(plan.FilesToRestore);
        DeleteList.ItemsSource = ToTextBlocks(plan.FilesToDelete);
    }

    private static List<Control> ToTextBlocks(IReadOnlyList<string> paths) =>
        paths.Select(path => (Control)new TextBlock
        {
            Text = path,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 2)
        }).ToList();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);
}
