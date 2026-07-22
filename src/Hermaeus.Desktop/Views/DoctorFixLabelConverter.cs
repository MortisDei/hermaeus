using System;
using Avalonia.Data.Converters;

namespace Hermaeus.Desktop.Views;

public sealed class DoctorFixLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is string s)
        {
            if (s.StartsWith("Open", StringComparison.OrdinalIgnoreCase))
                return "Open";
            return s;
        }
        return "Open";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
