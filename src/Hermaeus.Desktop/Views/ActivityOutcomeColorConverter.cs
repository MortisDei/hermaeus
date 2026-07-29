using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Hermaeus.Core.Models;

namespace Hermaeus.Desktop.Views;

/// <summary>Reuses the shared status palette (r16 3.5) so Activity outcomes read
/// consistently with agent task status and every other status chip in the app.</summary>
public sealed class ActivityOutcomeColorConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) => v switch
    {
        ActivityOutcome.Succeeded => StatusPalette.Ok,
        ActivityOutcome.Partial => StatusPalette.Warn,
        ActivityOutcome.Failed => StatusPalette.Error,
        ActivityOutcome.Running => StatusPalette.Info,
        ActivityOutcome.Cancelled => StatusPalette.Neutral,
        _ => StatusPalette.Neutral
    };

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Avalonia.AvaloniaProperty.UnsetValue;
}
