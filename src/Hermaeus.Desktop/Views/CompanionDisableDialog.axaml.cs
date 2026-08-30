using Avalonia.Controls;
using Avalonia.Interactivity;
using Hermaeus.Services;

namespace Hermaeus.Desktop.Views;

public partial class CompanionDisableDialog : Window
{
    public CompanionDisableDialog()
    {
        InitializeComponent();
    }

    public CompanionDisableDialog(ModelDeletionPlan plan) : this()
    {
        MessageText.Text = $"Automatic companion handling is being disabled for this model.\n\n{plan.Description}";
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(CompanionDisableChoice.Cancel);
    private void OnKeepClick(object? sender, RoutedEventArgs e) => Close(CompanionDisableChoice.KeepFiles);
    private void OnRemoveClick(object? sender, RoutedEventArgs e) => Close(CompanionDisableChoice.RemoveFiles);
}
