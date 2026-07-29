using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Hermaeus.Desktop.Views;

public sealed class ProjectColorConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) => ProjectPalette.Resolve(v as string);

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Avalonia.AvaloniaProperty.UnsetValue;
}
