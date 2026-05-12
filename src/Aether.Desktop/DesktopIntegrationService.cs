using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Aether.ViewModels;

namespace Aether.Desktop;

public sealed class DesktopIntegrationService : IDisposable
{
    private readonly MainWindowViewModel _vm;
    private Window? _window;
    private TrayIcon? _tray;
    private bool _isQuitting;

    public DesktopIntegrationService(MainWindowViewModel vm)
    {
        _vm = vm;
    }

    public void Attach(Window window)
    {
        _window = window;
        window.KeyDown += OnKeyDown;
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty
                && window.WindowState == WindowState.Minimized
                && _vm.Settings.MinimizeToTray
                && _vm.Settings.EnableTrayIcon)
            {
                window.Hide();
            }
        };

        EnsureTray();
    }

    public void Quit()
    {
        _isQuitting = true;
        _vm.Shutdown();
        _window?.Close();
    }

    public bool ShouldCancelCloseForTray()
    {
        if (_isQuitting)
            return false;

        // Closing the window means exiting Aether. Minimize-to-tray only applies
        // to an explicit minimize action so managed local servers still stop on X.
        return false;
    }

    public void Dispose()
    {
        if (_window is not null)
            _window.KeyDown -= OnKeyDown;
        _tray?.Dispose();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_vm.Settings.EnableLocalHotkeys)
            return;

        var modifiers = e.KeyModifiers;
        if (modifiers == KeyModifiers.Control && e.Key == Key.Space)
        {
            ShowAndActivate();
            _vm.ToggleQuickChatSurface();
            e.Handled = true;
            return;
        }

        if (modifiers == KeyModifiers.Control && e.Key == Key.N)
        {
            ShowAndActivate();
            _vm.OpenNewConversation();
            e.Handled = true;
            return;
        }

        if (modifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.S)
        {
            ShowAndActivate();
            _vm.OpenServicesPanel();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _vm.ShowQuickChat)
        {
            _vm.HideQuickChat();
            e.Handled = true;
        }
    }

    private void EnsureTray()
    {
        if (_tray is not null || !_vm.Settings.EnableTrayIcon)
            return;

        var tray = new TrayIcon
        {
            ToolTipText = "Aether",
            IsVisible = true,
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Aether.Desktop/Assets/aether.ico"))),
            Menu = BuildMenu()
        };
        tray.Clicked += (_, _) => ShowAndActivate();
        _tray = tray;
    }

    private NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();
        menu.Items.Add(Item("Show Aether", ShowAndActivate));
        menu.Items.Add(Item("Quick Chat", () =>
        {
            ShowAndActivate();
            _vm.ToggleQuickChatSurface();
        }));
        menu.Items.Add(Item("New Chat", () =>
        {
            ShowAndActivate();
            _vm.OpenNewConversation();
        }));
        menu.Items.Add(Item("Services", () =>
        {
            ShowAndActivate();
            _vm.OpenServicesPanel();
        }));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item("Stop Services", _vm.Shutdown));
        menu.Items.Add(Item("Quit Aether", Quit));
        return menu;
    }

    private static NativeMenuItem Item(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    private void ShowAndActivate()
    {
        if (_window is null)
            return;

        if (!_window.IsVisible)
            _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
}
