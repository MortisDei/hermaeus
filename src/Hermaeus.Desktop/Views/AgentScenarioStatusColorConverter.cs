using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Hermaeus.Desktop.Views;

/// <summary>Converts an <see cref="AgentScenarioRowViewModel.Passed"/> tri-state (not run / passed / failed) to a status chip color, mirroring DoctorStatusColorConverter's pattern.</summary>
public sealed class AgentScenarioStatusColorConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) => v switch
    {
        true => StatusPalette.Ok,
        false => StatusPalette.Error,
        _ => StatusPalette.Neutral
    };

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Avalonia.AvaloniaProperty.UnsetValue;
}
