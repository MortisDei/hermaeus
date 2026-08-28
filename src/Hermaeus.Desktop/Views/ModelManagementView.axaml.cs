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
    public ModelManagementView()
    {
        InitializeComponent();
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnHfSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ModelManagementViewModel vm
            || !vm.SearchHuggingFaceCommand.CanExecute(null))
            return;

        vm.SearchHuggingFaceCommand.Execute(null);
        e.Handled = true;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not ModelManagementViewModel vm)
            return;

        vm.RequestOrganizeConfirmation = async plan =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new OrganizeModelsPreviewDialog();
            dialog.SetPlan(plan);
            return await dialog.ShowDialog<bool>(owner);
        };

        vm.RequestEmptyDirectoryCleanupConfirmation = async count =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;

            var dialog = new EmptyDirectoryCleanupDialog();
            dialog.SetCount(count);
            return await dialog.ShowDialog<bool>(owner);
        };

        vm.RequestRepoIdInput = async item =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return null;

            var dialog = new LinkHuggingFaceRepoDialog();
            dialog.SetModelName(item.EffectiveName);
            return await dialog.ShowDialog<string?>(owner);
        };

        vm.RequestDeleteModelConfirmation = async plan =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;
            var dialog = new ConfirmActionDialog("Delete model", plan.Description);
            return await dialog.ShowDialog<bool>(owner);
        };

        vm.RequestCompanionDisableConfirmation = async plan =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return CompanionDisableChoice.Cancel;
            var dialog = new CompanionDisableDialog(plan);
            return await dialog.ShowDialog<CompanionDisableChoice>(owner);
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
