using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hermaeus.Desktop.Views;

public partial class PackageIntegrationWindow : Window
{
    private PackageIntegrationLaunch? _launch;

    public PackageIntegrationWindow()
    {
        InitializeComponent();
    }

    internal PackageIntegrationWindow(PackageIntegrationLaunch launch) : this()
    {
        _launch = launch;

        var installing = launch.Action == PackageIntegrationActionKind.Install;
        HeadingText.Text = installing ? "Install Hermaeus" : "Uninstall Hermaeus";
        MessageText.Text = installing
            ? "Add Hermaeus to your application menu and install the Moss icon for this user?"
            : "Remove the installed application-menu entry and package for this user? This extracted copy will remain available.";
        ActionButton.Content = installing ? "Install" : "Uninstall";

        if (!launch.CanRun)
        {
            StatusText.Text = "Close the running Hermaeus window before changing its desktop installation.";
            ActionButton.IsEnabled = false;
            CancelButton.Content = "Close";
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnActionClick(object? sender, RoutedEventArgs e)
    {
        if (_launch is null)
            return;

        ActionButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        StatusText.Text = _launch.Action == PackageIntegrationActionKind.Install
            ? "Installing..."
            : "Uninstalling...";

        var result = await PackageIntegrationAction.RunAsync(_launch);
        var installing = _launch.Action == PackageIntegrationActionKind.Install;
        StatusText.Text = result.Success
            ? installing
                ? "Installed. Launch Hermaeus from your application menu."
                : "Uninstalled. This extracted copy can still be launched directly."
            : $"The operation failed: {result.Detail}";

        CancelButton.Content = "Close";
        CancelButton.IsEnabled = true;
        if (!result.Success)
            ActionButton.IsEnabled = true;
    }
}
