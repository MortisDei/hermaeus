using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Aether.Services;
using Aether.ViewModels;
using System.Globalization;

namespace Aether.Desktop.Views;

public partial class ModelManagementView : UserControl
{
    public ModelManagementView()
    {
        InitializeComponent();
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
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
    }

    /// <summary>
    /// r13 02-model-library.md 2.2 root cause: each expanded card holds 8 NumericUpDowns,
    /// and the pointer is almost always over one, so Avalonia's NumericUpDown consumed
    /// every wheel notch as a spin before it ever reached the outer ScrollViewer - the
    /// owner could never scroll a 32-model list past whatever fit without using the thin
    /// scrollbar. ServicesView/SettingsView hit the same problem with their own
    /// NumericUpDown-heavy editors and fixed it the same way: intercept the wheel in the
    /// tunnel phase (runs before any control's own handler) and always drive the page
    /// ScrollViewer directly, regardless of what is under the pointer.
    /// </summary>
    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var max = Math.Max(0, ModelListScrollViewer.Extent.Height - ModelListScrollViewer.Viewport.Height);
        if (max <= 0) return;

        var next = Math.Clamp(ModelListScrollViewer.Offset.Y - e.Delta.Y * 56, 0, max);
        if (Math.Abs(next - ModelListScrollViewer.Offset.Y) < 0.1) return;

        ModelListScrollViewer.Offset = new Vector(ModelListScrollViewer.Offset.X, next);
        e.Handled = true;
    }
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
        => v is true
            ? (IBrush)new SolidColorBrush(Color.Parse("#EF5350"))
            : new SolidColorBrush(Color.Parse("#66BB6A"));
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}

public class PullingTextConverter : IValueConverter
{
    public static readonly PullingTextConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => v is true ? "Pulling..." : "Pull";
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}

public class TuningLabelConverter : IValueConverter
{
    public static readonly TuningLabelConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => v is true ? "Tuning..." : "Auto tune";
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}

public class FitTierBrushConverter : IValueConverter
{
    public static readonly FitTierBrushConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => v is ModelFitTier tier
            ? (IBrush)new SolidColorBrush(Color.Parse(tier switch
            {
                ModelFitTier.FitsGpu => "#4CAF50",
                ModelFitTier.FitsPartial => "#FF9800",
                ModelFitTier.TooLarge => "#F44336",
                _ => "#757575"
            }))
            : new SolidColorBrush(Color.Parse("#757575"));
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}

public class UpdateStatusBrushConverter : IValueConverter
{
    public static readonly UpdateStatusBrushConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => v is ModelUpdateStatus status
            ? (IBrush)new SolidColorBrush(Color.Parse(status switch
            {
                ModelUpdateStatus.UpdateAvailable => "#FF9800",
                ModelUpdateStatus.UpToDate => "#4CAF50",
                ModelUpdateStatus.NoLongerPublished => "#757575",
                _ => "#757575"
            }))
            : new SolidColorBrush(Color.Parse("#757575"));
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}

public class UpdatingLabelConverter : IValueConverter
{
    public static readonly UpdatingLabelConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => v is true ? "Updating..." : "Update";
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

public class DownloadingLabelConverter : IValueConverter
{
    public static readonly DownloadingLabelConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => v is true ? "Downloading..." : "Download";
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
