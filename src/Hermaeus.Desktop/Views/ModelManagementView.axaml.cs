using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using System.Globalization;

namespace Hermaeus.Desktop.Views;

public partial class ModelManagementView : UserControl
{
    private ModelManagementViewModel? _wiredViewModel;
    private Action? _releaseCallbacks;
    private Func<bool>? _ownsCallbacks;
    private long _callbackGeneration;

    public ModelManagementView()
    {
        InitializeComponent();
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => WireViewModel();
        Unloaded += (_, _) => UnwireViewModel();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnwireViewModel();
        WireViewModel();
    }

    private void UnwireViewModel()
    {
        _callbackGeneration++;
        _releaseCallbacks?.Invoke();
        _releaseCallbacks = null;
        _ownsCallbacks = null;
        _wiredViewModel = null;
    }

    private bool OwnsCallbacks(long generation) =>
        generation == _callbackGeneration && _ownsCallbacks?.Invoke() == true;

    private void WireViewModel()
    {
        if (!IsLoaded || ReferenceEquals(_wiredViewModel, DataContext)) return;
        if (DataContext is not ModelManagementViewModel vm)
            return;
        _wiredViewModel = vm;
        var generation = _callbackGeneration;

        vm.RequestOrganizeConfirmation = async plan =>
        {
            if (!OwnsCallbacks(generation) || TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new OrganizeModelsPreviewDialog();
            dialog.SetPlan(plan);
            return await dialog.ShowDialog<bool>(owner) && OwnsCallbacks(generation);
        };

        vm.RequestEmptyDirectoryCleanupConfirmation = async count =>
        {
            if (!OwnsCallbacks(generation) || TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new EmptyDirectoryCleanupDialog();
            dialog.SetCount(count);
            return await dialog.ShowDialog<bool>(owner) && OwnsCallbacks(generation);
        };

        vm.RequestRepoIdInput = async item =>
        {
            if (!OwnsCallbacks(generation) || TopLevel.GetTopLevel(this) is not Window owner)
                return null;

            var dialog = new LinkHuggingFaceRepoDialog();
            dialog.SetModelName(item.EffectiveName);
            var result = await dialog.ShowDialog<string?>(owner);
            return OwnsCallbacks(generation) ? result : null;
        };

        vm.RequestDeleteModelConfirmation = async plan =>
        {
            if (!OwnsCallbacks(generation) || TopLevel.GetTopLevel(this) is not Window owner)
                return false;
            var dialog = new ConfirmActionDialog("Delete model", plan.Description);
            return await dialog.ShowDialog<bool>(owner) && OwnsCallbacks(generation);
        };

        vm.RequestCompanionDisableConfirmation = async plan =>
        {
            if (!OwnsCallbacks(generation) || TopLevel.GetTopLevel(this) is not Window owner)
                return CompanionDisableChoice.Cancel;
            var dialog = new CompanionDisableDialog(plan);
            var result = await dialog.ShowDialog<CompanionDisableChoice>(owner);
            return OwnsCallbacks(generation) ? result : CompanionDisableChoice.Cancel;
        };

        vm.RequestCompanionRemovalConfirmation = async plan =>
        {
            if (!OwnsCallbacks(generation) || TopLevel.GetTopLevel(this) is not Window owner)
                return false;
            var dialog = new ConfirmActionDialog("Clear companion", plan.Description);
            return await dialog.ShowDialog<bool>(owner) && OwnsCallbacks(generation);
        };
        var ownedRequestOrganizeConfirmation = vm.RequestOrganizeConfirmation;
        var ownedRequestEmptyDirectoryCleanupConfirmation = vm.RequestEmptyDirectoryCleanupConfirmation;
        var ownedRequestRepoIdInput = vm.RequestRepoIdInput;
        var ownedRequestDeleteModelConfirmation = vm.RequestDeleteModelConfirmation;
        var ownedRequestCompanionDisableConfirmation = vm.RequestCompanionDisableConfirmation;
        var ownedRequestCompanionRemovalConfirmation = vm.RequestCompanionRemovalConfirmation;
        _ownsCallbacks = () => ReferenceEquals(_wiredViewModel, vm)
            && vm.RequestOrganizeConfirmation == ownedRequestOrganizeConfirmation
            && vm.RequestEmptyDirectoryCleanupConfirmation == ownedRequestEmptyDirectoryCleanupConfirmation
            && vm.RequestRepoIdInput == ownedRequestRepoIdInput
            && vm.RequestDeleteModelConfirmation == ownedRequestDeleteModelConfirmation
            && vm.RequestCompanionDisableConfirmation == ownedRequestCompanionDisableConfirmation
            && vm.RequestCompanionRemovalConfirmation == ownedRequestCompanionRemovalConfirmation;
        _releaseCallbacks = () =>
        {
            if (vm.RequestOrganizeConfirmation == ownedRequestOrganizeConfirmation) vm.RequestOrganizeConfirmation = null;
            if (vm.RequestEmptyDirectoryCleanupConfirmation == ownedRequestEmptyDirectoryCleanupConfirmation) vm.RequestEmptyDirectoryCleanupConfirmation = null;
            if (vm.RequestRepoIdInput == ownedRequestRepoIdInput) vm.RequestRepoIdInput = null;
            if (vm.RequestDeleteModelConfirmation == ownedRequestDeleteModelConfirmation) vm.RequestDeleteModelConfirmation = null;
            if (vm.RequestCompanionDisableConfirmation == ownedRequestCompanionDisableConfirmation) vm.RequestCompanionDisableConfirmation = null;
            if (vm.RequestCompanionRemovalConfirmation == ownedRequestCompanionRemovalConfirmation) vm.RequestCompanionRemovalConfirmation = null;
        };
    }

    /// <summary>
    /// r13 02-model-library.md 2.2 root cause: each expanded card holds 8 NumericUpDowns,
    /// and the pointer is almost always over one, so Avalonia's NumericUpDown consumed
    /// every wheel notch as a spin before it ever reached the outer ScrollViewer - the
    /// owner could never scroll a 32-model list past whatever fit without using the thin
    /// scrollbar. ServicesView/SettingsView hit the same problem with their own
    /// NumericUpDown-heavy editors and fixed it the same way (r16
    /// 03-workbench-and-desktop.md 3.5 consolidated the three copies into
    /// <see cref="WheelScrollHelper"/>): intercept the wheel in the tunnel
    /// phase (runs before any control's own handler) and always drive the
    /// page ScrollViewer directly, regardless of what is under the pointer.
    /// </summary>
    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e) =>
        WheelScrollHelper.Handle(ModelListScrollViewer, e);
}

public class NotEmptyConverter : IValueConverter
{
    public static readonly NotEmptyConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => v is string s && !string.IsNullOrEmpty(s);
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}

public class ErrorColorConverter : IValueConverter
{
    public static readonly ErrorColorConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => v is true ? StatusPalette.Error : StatusPalette.Ok;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}

public class FitTierBrushConverter : IValueConverter
{
    public static readonly FitTierBrushConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => v is ModelFitTier tier
            ? tier switch
            {
                ModelFitTier.FitsGpu => StatusPalette.Ok,
                ModelFitTier.FitsPartial => StatusPalette.Warn,
                ModelFitTier.TooLarge => StatusPalette.Error,
                _ => StatusPalette.Neutral
            }
            : StatusPalette.Neutral;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}

public class UpdateStatusBrushConverter : IValueConverter
{
    public static readonly UpdateStatusBrushConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => v is ModelUpdateStatus status
            ? status switch
            {
                ModelUpdateStatus.UpdateAvailable => StatusPalette.Warn,
                ModelUpdateStatus.UpToDate => StatusPalette.Ok,
                ModelUpdateStatus.NoLongerPublished => StatusPalette.Neutral,
                _ => StatusPalette.Neutral
            }
            : StatusPalette.Neutral;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}

public class RepoLinkLabelConverter : IValueConverter
{
    public static readonly RepoLinkLabelConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => v is string s && !string.IsNullOrEmpty(s) ? $"Linked: {s}" : "Link to Hugging Face repo...";
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}

public class UpdateAvailableConverter : IValueConverter
{
    public static readonly UpdateAvailableConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => v is ModelUpdateStatus.UpdateAvailable;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}

public class NotDownloadedConverter : IValueConverter
{
    public static readonly NotDownloadedConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => v is HfDownloadState state && state != HfDownloadState.Downloaded && state != HfDownloadState.Downloading;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}
