using System;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;
using Hermaeus.Services;

namespace Hermaeus.Desktop.Controls;

public class DiffTypeToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DiffType dt)
        {
            return dt switch
            {
                DiffType.Added => Brushes.Green,
                DiffType.Removed => Brushes.IndianRed,
                DiffType.Modified => Brushes.Goldenrod,
                _ => Brushes.Transparent
            };
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => AvaloniaProperty.UnsetValue;
}
