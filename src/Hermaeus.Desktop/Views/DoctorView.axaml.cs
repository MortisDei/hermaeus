using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Hermaeus.Core.Models;
using Hermaeus.ViewModels;
using System.Globalization;

namespace Hermaeus.Desktop.Views;

public partial class DoctorView : UserControl
{
    public DoctorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not DoctorViewModel vm) return;
        vm.RequestCopyToClipboard = async text =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is null) return false;
            try { await top.Clipboard.SetTextAsync(text); return true; }
            catch { return false; }
        };
        vm.RequestConfirmAsync = async (title, message) =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
                return false;
            var dialog = new ConfirmActionDialog(title, message);
            return await dialog.ShowDialog<bool>(owner);
        };
    }
}

public sealed class DoctorStatusColorConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c)
    {
        return v switch
        {
            DoctorCheckStatus.Ready => StatusPalette.Ok,
            DoctorCheckStatus.Warning => StatusPalette.Warn,
            DoctorCheckStatus.Error => StatusPalette.Error,
            _ => StatusPalette.Neutral
        };
    }

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}
