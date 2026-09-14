using System.ComponentModel;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.ViewModels;

/// <summary>
/// UI-facing state for the optional ambient pet overlay. Settings remain the
/// source of truth, so edits made by the overlay use the same settings
/// autosave path as the Settings page.
/// </summary>
public sealed class DesktopPetViewModel : ViewModelBase
{
    private readonly IPetPackageCatalog _catalog;
    private UiSettingsViewModel? _settings;
    private double _viewportWidth;
    private double _viewportHeight;
    private double _spriteWidth;
    private double _spriteHeight;
    private double _positionX;
    private double _positionY;
    private bool _settingPosition;

    public DesktopPetViewModel(IPetPackageCatalog? catalog = null)
    {
        _catalog = catalog ?? EmptyPetCatalog.Instance;
    }

    public bool IsEnabled => _settings?.PetEnabled == true;

    public string SelectedPetId => _settings?.SelectedPetId ?? "moss";

    public PetPackage? SelectedPackage =>
        _catalog.GetAvailablePackages().FirstOrDefault(package =>
            string.Equals(package.Manifest.Id, SelectedPetId, StringComparison.OrdinalIgnoreCase));

    public bool IsVisible => IsEnabled && SelectedPackage is not null;

    public string StatusLabel => !IsEnabled
        ? "Companion is off."
        : SelectedPackage is null
            ? "The selected companion is unavailable. Choose an installed package in Settings."
            : SelectedPackage.Manifest.DisplayName;

    public double PositionX => _positionX;
    public double PositionY => _positionY;

    public void BindSettings(UiSettingsViewModel settings)
    {
        if (ReferenceEquals(_settings, settings))
            return;

        if (_settings is not null)
            _settings.PropertyChanged -= OnSettingsPropertyChanged;
        _settings = settings;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
        SetInitialPosition();
        RaiseAll();
    }

    public void UpdateViewport(double width, double height, double spriteWidth, double spriteHeight)
    {
        _viewportWidth = Math.Max(0, width);
        _viewportHeight = Math.Max(0, height);
        _spriteWidth = Math.Max(0, spriteWidth);
        _spriteHeight = Math.Max(0, spriteHeight);
        SetInitialPosition();
    }

    public void SetPosition(double x, double y)
    {
        if (_settings is null)
            return;

        var clamped = Clamp(x, y);
        _positionX = clamped.X;
        _positionY = clamped.Y;
        _settingPosition = true;
        try
        {
            _settings.PetPositionX = _positionX;
            _settings.PetPositionY = _positionY;
        }
        finally
        {
            _settingPosition = false;
        }
        OnPropertyChanged(nameof(PositionX));
        OnPropertyChanged(nameof(PositionY));
    }

    public (double X, double Y) Clamp(double x, double y)
    {
        var maxX = Math.Max(0, _viewportWidth - _spriteWidth);
        var maxY = Math.Max(0, _viewportHeight - _spriteHeight);
        return (Math.Clamp(double.IsFinite(x) ? x : 0, 0, maxX),
            Math.Clamp(double.IsFinite(y) ? y : 0, 0, maxY));
    }

    private void SetInitialPosition()
    {
        if (_settings is null || _viewportWidth <= 0 || _viewportHeight <= 0)
            return;

        var x = _settings.PetPositionX >= 0
            ? _settings.PetPositionX
            : Math.Max(0, _viewportWidth - _spriteWidth - 18);
        var y = _settings.PetPositionY >= 0
            ? _settings.PetPositionY
            : Math.Max(0, _viewportHeight - _spriteHeight - 18);
        var clamped = Clamp(x, y);
        if (_positionX == clamped.X && _positionY == clamped.Y)
            return;
        _positionX = clamped.X;
        _positionY = clamped.Y;
        OnPropertyChanged(nameof(PositionX));
        OnPropertyChanged(nameof(PositionY));
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_settingPosition && e.PropertyName is (nameof(UiSettingsViewModel.PetPositionX)
            or nameof(UiSettingsViewModel.PetPositionY)))
        {
            SetInitialPosition();
        }

        if (e.PropertyName is nameof(UiSettingsViewModel.PetEnabled)
            or nameof(UiSettingsViewModel.SelectedPetId)
            or nameof(UiSettingsViewModel.PetPositionX)
            or nameof(UiSettingsViewModel.PetPositionY))
        {
            RaiseAll();
        }
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(SelectedPetId));
        OnPropertyChanged(nameof(SelectedPackage));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(StatusLabel));
    }

    private sealed class EmptyPetCatalog : IPetPackageCatalog
    {
        public static readonly EmptyPetCatalog Instance = new();

        public IReadOnlyList<PetPackage> GetAvailablePackages() => [];

        public Task<PetPackageResult> ImportAsync(
            string manifestPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PetPackageResult.Failure("Pet catalog is unavailable."));
    }
}
