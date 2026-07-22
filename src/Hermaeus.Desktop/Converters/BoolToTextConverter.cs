using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Hermaeus.Desktop.Views;

/// <summary>
/// Generic replacement for the four identically-shaped bool-to-"X.../X"
/// label converters ModelManagementView.axaml.cs used to define separately
/// (Pulling/Pull, Tuning/Auto tune, Updating/Update, Downloading/Download -
/// r16 03-workbench-and-desktop.md 3.5). <see cref="ConverterParameter"/> is
/// <c>"TrueText|FalseText"</c>.
/// </summary>
public sealed class BoolToTextConverter : IValueConverter
{
    public static readonly BoolToTextConverter Instance = new();

    public object Convert(object? v, Type t, object? parameter, CultureInfo c)
    {
        var parts = (parameter as string ?? string.Empty).Split('|', 2);
        var trueText = parts.Length > 0 ? parts[0] : string.Empty;
        var falseText = parts.Length > 1 ? parts[1] : string.Empty;
        return v is true ? trueText : falseText;
    }

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => AvaloniaProperty.UnsetValue;
}
