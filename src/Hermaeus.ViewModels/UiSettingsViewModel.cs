using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

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
    [ObservableProperty] private string _headingFontFamily = string.Empty;
    [ObservableProperty] private string _bodyFontFamily = string.Empty;
    [ObservableProperty] private string _monoFontFamily = string.Empty;

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
        HeadingFontFamily = settings.Ui.HeadingFontFamily;
        BodyFontFamily = settings.Ui.BodyFontFamily;
        MonoFontFamily = settings.Ui.MonoFontFamily;
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
        settings.Ui.HeadingFontFamily = HeadingFontFamily;
        settings.Ui.BodyFontFamily = BodyFontFamily;
        settings.Ui.MonoFontFamily = MonoFontFamily;
    }
}
