using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hermaeus.Desktop.Views;

public partial class RemoveMissingSourcesDialog : Window
{
    public RemoveMissingSourcesDialog()
        : this(string.Empty, [])
    {
    }

    public RemoveMissingSourcesDialog(string datasetName, IReadOnlyList<string> paths)
    {
        InitializeComponent();
        DataContext = new RemoveMissingSourcesDialogViewModel(datasetName, paths);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnRemoveClick(object? sender, RoutedEventArgs e) => Close(true);
}

public class RemoveMissingSourcesDialogViewModel
{
    public string Header { get; }
    public string Message { get; }
    public IReadOnlyList<string> Paths { get; }

    public RemoveMissingSourcesDialogViewModel(string datasetName, IReadOnlyList<string> paths)
    {
        Header = $"Remove missing sources from '{datasetName}'?";
        Message = $"{paths.Count} source file(s) no longer exist on disk:";
        Paths = paths;
    }
}
