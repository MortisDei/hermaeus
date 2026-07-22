using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Hermaeus.Desktop.Views;

/// <summary>True indents a recent-tasks row to mark it as a sub-task child (r16 03-workbench-and-desktop.md 3.1).</summary>
public sealed class BoolToIndentMarginConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        v is true ? new Thickness(24, 0, 0, 6) : new Thickness(0, 0, 0, 6);

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => AvaloniaProperty.UnsetValue;
}
