using System;
using System.Globalization;
using Aether.Agent.Models;
using Avalonia.Data.Converters;

namespace Aether.Desktop.Views;

/// <summary>Converts an <see cref="AgentSubTaskStatus"/> to a status chip color, mirroring AgentScenarioStatusColorConverter's pattern (r15 02-orchestration-ui.md 2.2).</summary>
public sealed class AgentSubTaskStatusColorConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) => v switch
    {
        AgentSubTaskStatus.Complete => StatusPalette.Ok,
        AgentSubTaskStatus.Failed => StatusPalette.Error,
        AgentSubTaskStatus.Running => StatusPalette.Info,
        AgentSubTaskStatus.Skipped => StatusPalette.Neutral,
        _ => StatusPalette.Neutral
    };

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Avalonia.AvaloniaProperty.UnsetValue;
}
