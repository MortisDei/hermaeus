using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class UiSettingsViewModel : ObservableObject
{
    [ObservableProperty] private double _fontSize = 14;
    [ObservableProperty] private string _selectedTheme = "System";
    [ObservableProperty] private bool _ctrlEnterToSend;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _showQuickChat;
    [ObservableProperty] private bool _enableTrayIcon = true;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private bool _enableLocalHotkeys = true;
    [ObservableProperty] private bool _enableGlobalHotkeys;
    [ObservableProperty] private string _globalHotkeyStatus = "System-wide hotkeys are off.";
    [ObservableProperty] private bool _showNavLabels;

    public string[] Themes { get; } = ["System", "Dark", "Light"];

    public void ReloadFrom(AppSettings settings)
    {
        FontSize = settings.Ui.FontSize;
        SelectedTheme = settings.Ui.Theme;
        CtrlEnterToSend = settings.Ui.CtrlEnterToSend;
        StartMinimized = settings.Ui.StartMinimized;
        ShowQuickChat = settings.Ui.ShowQuickChat;
        EnableTrayIcon = settings.Ui.EnableTrayIcon;
        MinimizeToTray = settings.Ui.MinimizeToTray;
        EnableLocalHotkeys = settings.Ui.EnableLocalHotkeys;
        EnableGlobalHotkeys = settings.Ui.EnableGlobalHotkeys;
        ShowNavLabels = settings.Ui.ShowNavLabels;
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.Ui.FontSize = FontSize;
        settings.Ui.Theme = SelectedTheme;
        settings.Ui.CtrlEnterToSend = CtrlEnterToSend;
        settings.Ui.StartMinimized = StartMinimized;
        settings.Ui.ShowQuickChat = ShowQuickChat;
        settings.Ui.EnableTrayIcon = EnableTrayIcon;
        settings.Ui.MinimizeToTray = MinimizeToTray;
        settings.Ui.EnableLocalHotkeys = EnableLocalHotkeys;
        settings.Ui.EnableGlobalHotkeys = EnableGlobalHotkeys;
        settings.Ui.ShowNavLabels = ShowNavLabels;
    }
}
