using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hermaeus.Desktop.Views;

public partial class LinkHuggingFaceRepoDialog : Window
{
    public LinkHuggingFaceRepoDialog()
    {
        InitializeComponent();
    }

    public void SetModelName(string name) =>
        MessageText.Text = $"Enter the Hugging Face repo (org/repo) that {name} came from.";

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(RepoIdBox.Text);
}
