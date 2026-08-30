using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Hermaeus.Core.Services;
using Hermaeus.ViewModels;
using System.ComponentModel;

namespace Hermaeus.Desktop;

public sealed class DesktopIntegrationService : IDisposable
{
    private readonly MainWindowViewModel _vm;
    private readonly ITrayIntegrationState _trayIntegration;
    private readonly GlobalHotkeyService _globalHotkeys = new();
    private Window? _window;
    private TrayIcon? _tray;
    private NativeMenuItem? _quitItem;
    private EventHandler<AvaloniaPropertyChangedEventArgs>? _windowPropertyChangedHandler;
    private bool _isQuitting;

    public DesktopIntegrationService(MainWindowViewModel vm, ITrayIntegrationState trayIntegration)
    {
        _vm = vm;
        _trayIntegration = trayIntegration;
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
        if (_isQuitting)
            return;
        _isQuitting = true;
        if (_quitItem is not null)
        {
            _quitItem.Header = "Quitting Hermaeus...";
            _quitItem.IsEnabled = false;
        }
        _window?.Close();
    }

    public bool ShouldCancelCloseForTray()
    {
        if (_isQuitting)
            return false;

        // Closing and minimizing are separate choices now. Sharing one flag
        // meant wanting minimize-to-tray also meant the app could never be
        // closed from its own window button.
        return _vm.Settings.EnableTrayIcon && _vm.Settings.CloseToTray;
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

    /// <summary>
    /// Ctrl+Q is deliberately NOT bound here (r16 03-workbench-and-desktop.md
    /// 3.6): it used to quit instantly with no confirmation, generation or an
    /// agent run in progress or not, even with focus inside a TextBox
    /// mid-thought. Quit remains available via the tray menu and window
    /// close, both of which go through the existing close/shutdown path.
    /// </summary>
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
            ToolTipText = "Hermaeus",
            IsVisible = true,
            Menu = BuildMenu()
        };
        try
        {
            using var iconStream = AssetLoader.Open(new Uri("avares://Hermaeus.Desktop/Assets/hermaeus-tray.png"));
            tray.Icon = new WindowIcon(iconStream);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Hermaeus tray icon could not be loaded: {ex.Message}");
        }
        tray.Clicked += (_, _) =>
        {
            _trayIntegration.Confirm();
            ShowAndActivate();
        };
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
        menu.Items.Add(Item("Show Hermaeus", ShowAndActivate));
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
        var shutdown = new NativeMenuItem("Stop Services");
        shutdown.Click += async (_, _) =>
        {
            if (!shutdown.IsEnabled)
                return;
            shutdown.Header = "Stopping services…";
            shutdown.IsEnabled = false;
            var failed = false;
            try
            {
                await _vm.ShutdownAsync();
            }
            catch (Exception ex)
            {
                failed = true;
                shutdown.Header = "Stop services failed";
                Console.Error.WriteLine($"Error stopping services from tray: {ex}");
            }
            finally
            {
                if (!failed)
                    shutdown.Header = "Stop Services";
                shutdown.IsEnabled = true;
            }
        };
        menu.Items.Add(shutdown);
        _quitItem = new NativeMenuItem("Quit Hermaeus");
        _quitItem.Click += (_, _) => Quit();
        menu.Items.Add(_quitItem);
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
