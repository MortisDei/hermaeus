using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace Aether.Desktop.Converters;

public class UppercaseConverter : IValueConverter
{
    public static readonly UppercaseConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return null;
        return value.ToString()?.ToUpper(culture ?? CultureInfo.InvariantCulture);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}
