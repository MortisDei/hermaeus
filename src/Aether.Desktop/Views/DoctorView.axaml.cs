using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Aether.Core.Models;
using Aether.ViewModels;
using System.Globalization;

namespace Aether.Desktop.Views;

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
            if (top?.Clipboard is not null)
                await top.Clipboard.SetTextAsync(text);
        };
    }
}

public sealed class DoctorStatusColorConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c)
    {
        return v switch
        {
            DoctorCheckStatus.Ready => Brushes.LimeGreen,
            DoctorCheckStatus.Warning => Brushes.Orange,
            DoctorCheckStatus.Error => Brushes.IndianRed,
            _ => Brushes.Gray
        };
    }

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}
