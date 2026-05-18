using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Aether.ViewModels;
using System.ComponentModel;

namespace Aether.Desktop;

public sealed class DesktopIntegrationService : IDisposable
{
    private readonly MainWindowViewModel _vm;
    private readonly GlobalHotkeyService _globalHotkeys = new();
    private Window? _window;
    private TrayIcon? _tray;
    private EventHandler<AvaloniaPropertyChangedEventArgs>? _windowPropertyChangedHandler;
    private bool _isQuitting;

    public DesktopIntegrationService(MainWindowViewModel vm)
    {
        _vm = vm;
    }

    public void Attach(Window window)
    {
        _window = window;
        _vm.Settings.PropertyChanged += OnSettingsPropertyChanged;
        window.KeyDown += OnKeyDown;
        _windowPropertyChangedHandler = (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty
                && window.WindowState == WindowState.Minimized
                && _vm.Settings.MinimizeToTray
                && _vm.Settings.EnableTrayIcon)
            {
                window.Hide();
            }
        };
        window.PropertyChanged += _windowPropertyChangedHandler;

        EnsureTray();
        ConfigureGlobalHotkeys();
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

        return _vm.Settings.EnableTrayIcon && _vm.Settings.MinimizeToTray;
    }

    public void Dispose()
    {
        if (_window is not null)
        {
            _window.KeyDown -= OnKeyDown;
            if (_windowPropertyChangedHandler is not null)
                _window.PropertyChanged -= _windowPropertyChangedHandler;
        }
        _vm.Settings.PropertyChanged -= OnSettingsPropertyChanged;
        _tray?.Dispose();
        _globalHotkeys.Dispose();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.EnableGlobalHotkeys))
            ConfigureGlobalHotkeys();
        else if (e.PropertyName == nameof(SettingsViewModel.EnableTrayIcon))
            SyncTray();
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
            Menu = BuildMenu()
        };
        try
        {
            using var iconStream = AssetLoader.Open(new Uri("avares://Aether.Desktop/Assets/aether-tray.png"));
            tray.Icon = new WindowIcon(iconStream);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Aether tray icon could not be loaded: {ex.Message}");
        }
        tray.Clicked += (_, _) => ShowAndActivate();
        _tray = tray;
    }

    private void SyncTray()
    {
        if (_vm.Settings.EnableTrayIcon)
        {
            EnsureTray();
            return;
        }

        _tray?.Dispose();
        _tray = null;
    }

    private void ConfigureGlobalHotkeys()
    {
        if (_window is null)
            return;

        if (!_vm.Settings.EnableGlobalHotkeys)
        {
            _globalHotkeys.Unregister();
            _vm.Settings.GlobalHotkeyStatus = GlobalHotkeyService.IsSupported
                ? "System-wide hotkeys are off."
                : "System-wide hotkeys are unavailable on this OS/compositor.";
            return;
        }

        var result = _globalHotkeys.Register(
            _window,
            () =>
            {
                ShowAndActivate();
                _vm.ToggleQuickChatSurface();
            },
            () =>
            {
                ShowAndActivate();
                _vm.OpenNewConversation();
            },
            () =>
            {
                ShowAndActivate();
                _vm.OpenServicesPanel();
            });

        _vm.Settings.GlobalHotkeyStatus = result.Message;
        if (!result.Active)
            _vm.Settings.EnableGlobalHotkeys = false;
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
