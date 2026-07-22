using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hermaeus.Desktop.Views;

public partial class DeleteDatasetDialog : Window
{
    public DeleteDatasetDialog()
        : this(string.Empty, 0)
    {
    }

    public DeleteDatasetDialog(string datasetName, int chunkCount)
    {
        InitializeComponent();
        DataContext = new DeleteDatasetDialogViewModel(datasetName, chunkCount);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnDeleteClick(object? sender, RoutedEventArgs e) => Close(true);
}

public class DeleteDatasetDialogViewModel
{
    public string Header { get; }
    public string Message { get; }

    public DeleteDatasetDialogViewModel(string datasetName, int chunkCount)
    {
        Header = $"Delete '{datasetName}'?";
        Message = $"This will permanently delete the dataset and all {chunkCount:N0} chunk(s) associated with it.";
    }
}
