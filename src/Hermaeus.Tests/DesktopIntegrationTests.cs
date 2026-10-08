using Avalonia;
using Avalonia.Controls;
using Hermaeus.Desktop;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

[Collection(AvaloniaTestHost.CollectionName)]
public sealed class DesktopIntegrationTests(AvaloniaTestHost avalonia)
{
    [Fact]
    public Task Tray_is_attached_removed_and_recreated_without_stranding_a_hidden_window() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var harness = await MainWindowViewModelStartupTests.NewHarnessAsync(temp, initializeRagStore: true);
        var vm = harness.Main;
        vm.Settings.EnableGlobalHotkeys = false;
        vm.Settings.EnableTrayIcon = true;
        vm.Settings.CloseToTray = true;
        vm.Settings.MinimizeToTray = true;
        var state = new TrayIntegrationState();
        using var integration = new DesktopIntegrationService(vm, state);
        var window = new Window { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            integration.Attach(window);
            var tray = Assert.IsType<TrayIcon>(integration.Tray);
            Assert.Contains(tray, TrayIcon.GetIcons(Application.Current!)!);
            if (OperatingSystem.IsLinux())
                Assert.False(integration.ShouldCancelCloseForTray());

            state.Confirm();
            Assert.Equal(tray.NativeMenuExporter is not null, integration.ShouldCancelCloseForTray());
            tray.IsVisible = false;
            Assert.False(integration.ShouldCancelCloseForTray());
            tray.IsVisible = true;

            window.Show();
            window.Hide();
            vm.Settings.EnableTrayIcon = false;
            Assert.True(window.IsVisible);
            Assert.Null(integration.Tray);
            Assert.False(state.IsConfirmed);
            Assert.DoesNotContain(tray, TrayIcon.GetIcons(Application.Current!)!);
            Assert.False(integration.ShouldCancelCloseForTray());

            vm.Settings.EnableTrayIcon = true;
            var replacement = Assert.IsType<TrayIcon>(integration.Tray);
            Assert.NotSame(tray, replacement);
            Assert.Contains(replacement, TrayIcon.GetIcons(Application.Current!)!);
            if (OperatingSystem.IsLinux())
                Assert.False(integration.ShouldCancelCloseForTray());
            integration.Dispose();
            Assert.DoesNotContain(replacement, TrayIcon.GetIcons(Application.Current!)!);
        }
        finally
        {
            window.Close();
            await vm.ShutdownAsync();
        }
    });

    [Fact]
    public Task Minimize_without_confirmed_linux_tray_retains_the_window() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var harness = await MainWindowViewModelStartupTests.NewHarnessAsync(temp, initializeRagStore: true);
        var vm = harness.Main;
        vm.Settings.EnableGlobalHotkeys = false;
        vm.Settings.MinimizeToTray = true;
        // An unavailable/disabled icon must never cause either path to hide.
        vm.Settings.EnableTrayIcon = false;
        using var integration = new DesktopIntegrationService(vm, new TrayIntegrationState());
        var window = new Window { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            integration.Attach(window);
            window.Show();
            window.WindowState = WindowState.Minimized;
            Assert.True(window.IsVisible);
            Assert.False(integration.ShouldCancelCloseForTray());
        }
        finally
        {
            window.Close();
            await vm.ShutdownAsync();
        }
    });
}
