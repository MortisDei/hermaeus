using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Hermaeus.Agent.Models;

namespace Hermaeus.Desktop.Views;

/// <summary>Converts persisted Scenario Eval evidence state to a status chip color.</summary>
public sealed class AgentScenarioStatusColorConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) => v switch
    {
        AgentScenarioEvidenceStatus.Pass => StatusPalette.Ok,
        AgentScenarioEvidenceStatus.Fail => StatusPalette.Error,
        AgentScenarioEvidenceStatus.Stale => StatusPalette.Warn,
        true => StatusPalette.Ok,
        false => StatusPalette.Error,
        _ => StatusPalette.Neutral
    };

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Avalonia.AvaloniaProperty.UnsetValue;
}
