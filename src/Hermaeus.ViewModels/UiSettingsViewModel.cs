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
    [ObservableProperty] private bool _closeToTray = true;
    [ObservableProperty] private bool _enableLocalHotkeys = true;
    [ObservableProperty] private bool _enableGlobalHotkeys;
    [ObservableProperty] private string _globalHotkeyStatus = "System-wide hotkeys are off.";
    [ObservableProperty] private bool _showNavLabels;
    [ObservableProperty] private string _headingFontFamily = string.Empty;
    [ObservableProperty] private string _bodyFontFamily = string.Empty;
    [ObservableProperty] private string _monoFontFamily = string.Empty;
    [ObservableProperty] private bool _petEnabled;
    [ObservableProperty] private string _selectedPetId = "moss";
    [ObservableProperty] private double _petPositionX = -1;
    [ObservableProperty] private double _petPositionY = -1;
    [ObservableProperty] private PetPackageOptionViewModel? _selectedPet;

    private readonly IPetPackageCatalog? _petCatalog;

    public UiBoundCollection<PetPackageOptionViewModel> PetChoices { get; } = [];
    public string PetStatus { get; private set; } = string.Empty;
    public Func<Task<string?>>? RequestPetManifestPicker { get; set; }
    public bool HasPetChoices => PetChoices.Count > 0;
    public bool HasSelectedPet => SelectedPet is not null;
    public bool HasPetStatus => PetStatus.Length > 0;

    public string[] Themes { get; } = ["System", "Dark", "Light"];

    public UiSettingsViewModel(IPetPackageCatalog? petCatalog = null)
    {
        _petCatalog = petCatalog;
        RefreshPetChoices();
    }

    public void ReloadFrom(AppSettings settings)
    {
        FontSize = settings.Ui.FontSize;
        SelectedTheme = settings.Ui.Theme;
        CtrlEnterToSend = settings.Ui.CtrlEnterToSend;
        StartMinimized = settings.Ui.StartMinimized;
        ShowQuickChat = settings.Ui.ShowQuickChat;
        EnableTrayIcon = settings.Ui.EnableTrayIcon;
        MinimizeToTray = settings.Ui.MinimizeToTray;
        CloseToTray = settings.Ui.CloseToTray;
        EnableLocalHotkeys = settings.Ui.EnableLocalHotkeys;
        EnableGlobalHotkeys = settings.Ui.EnableGlobalHotkeys;
        ShowNavLabels = settings.Ui.ShowNavLabels;
        HeadingFontFamily = settings.Ui.HeadingFontFamily;
        BodyFontFamily = settings.Ui.BodyFontFamily;
        MonoFontFamily = settings.Ui.MonoFontFamily;
        PetEnabled = settings.Ui.PetEnabled;
        SelectedPetId = settings.Ui.SelectedPetId;
        PetPositionX = settings.Ui.PetPositionX;
        PetPositionY = settings.Ui.PetPositionY;
        RefreshPetChoices();
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
        settings.Ui.CloseToTray = CloseToTray;
        settings.Ui.EnableLocalHotkeys = EnableLocalHotkeys;
        settings.Ui.EnableGlobalHotkeys = EnableGlobalHotkeys;
        settings.Ui.ShowNavLabels = ShowNavLabels;
        settings.Ui.HeadingFontFamily = HeadingFontFamily;
        settings.Ui.BodyFontFamily = BodyFontFamily;
        settings.Ui.MonoFontFamily = MonoFontFamily;
        settings.Ui.PetEnabled = PetEnabled;
        settings.Ui.SelectedPetId = SelectedPetId;
        settings.Ui.PetPositionX = PetPositionX;
        settings.Ui.PetPositionY = PetPositionY;
    }

    [RelayCommand]
    private async Task ImportPetAsync()
    {
        if (_petCatalog is null || RequestPetManifestPicker is null)
        {
            PetStatus = "Pet import is unavailable in this host.";
            OnPropertyChanged(nameof(PetStatus));
            OnPropertyChanged(nameof(HasPetStatus));
            return;
        }

        var manifestPath = await RequestPetManifestPicker();
        if (string.IsNullOrWhiteSpace(manifestPath))
            return;

        try
        {
            var result = await _petCatalog.ImportAsync(manifestPath);
            if (!result.Succeeded || result.Package is null)
            {
                PetStatus = result.Error;
                OnPropertyChanged(nameof(PetStatus));
                OnPropertyChanged(nameof(HasPetStatus));
                return;
            }

            RefreshPetChoices();
            SelectedPetId = result.Package.Manifest.Id;
            PetStatus = $"Imported {result.Package.Manifest.DisplayName}. Enable the companion when you want it visible.";
            OnPropertyChanged(nameof(PetStatus));
            OnPropertyChanged(nameof(HasPetStatus));
        }
        catch (OperationCanceledException)
        {
            PetStatus = "Pet import cancelled.";
            OnPropertyChanged(nameof(PetStatus));
            OnPropertyChanged(nameof(HasPetStatus));
        }
        catch (Exception ex)
        {
            PetStatus = $"Pet import failed: {ex.Message}";
            OnPropertyChanged(nameof(PetStatus));
            OnPropertyChanged(nameof(HasPetStatus));
        }
    }

    private void RefreshPetChoices()
    {
        PetChoices.Clear();
        if (_petCatalog is null)
            return;

        foreach (var package in _petCatalog.GetAvailablePackages())
            PetChoices.Add(new PetPackageOptionViewModel(package));

        SelectedPet = PetChoices.FirstOrDefault(option =>
            string.Equals(option.Id, SelectedPetId, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(HasPetChoices));
    }

    partial void OnSelectedPetChanged(PetPackageOptionViewModel? value)
    {
        if (value is not null && !string.Equals(SelectedPetId, value.Id, StringComparison.Ordinal))
            SelectedPetId = value.Id;
        OnPropertyChanged(nameof(HasSelectedPet));
    }

    partial void OnSelectedPetIdChanged(string value)
    {
        var selected = PetChoices.FirstOrDefault(option =>
            string.Equals(option.Id, value, StringComparison.OrdinalIgnoreCase));
        if (!ReferenceEquals(SelectedPet, selected))
            SelectedPet = selected;
    }
}
