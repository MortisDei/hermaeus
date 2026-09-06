using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using System.Globalization;

namespace Hermaeus.Desktop.Views;

public partial class SystemOverviewView : UserControl
{
    public SystemOverviewView() => InitializeComponent();
}

public sealed class SystemOverviewStatusColorConverter : IValueConverter
{
    public static readonly SystemOverviewStatusColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string status
            ? status switch
            {
                "Ready" or "Present" or "OK" => StatusPalette.Ok,
                "Missing" => StatusPalette.Error,
                "Not set" or "Low" => StatusPalette.Warn,
                _ => StatusPalette.Neutral
            }
            : StatusPalette.Neutral;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        AvaloniaProperty.UnsetValue;
}
