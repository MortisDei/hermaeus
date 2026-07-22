using System;
using System.Globalization;
using Hermaeus.Agent.Models;
using Avalonia.Data.Converters;

namespace Hermaeus.Desktop.Views;

/// <summary>Converts an <see cref="AgentTaskStatus"/> to a status chip color for the recent-tasks list (r16 03-workbench-and-desktop.md 3.1), using the shared <see cref="StatusPalette"/> (3.5).</summary>
public sealed class AgentTaskStatusColorConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) => v switch
    {
        AgentTaskStatus.Complete => StatusPalette.Ok,
        AgentTaskStatus.Failed => StatusPalette.Error,
        AgentTaskStatus.Blocked => StatusPalette.Warn,
        AgentTaskStatus.WaitingForUser => StatusPalette.Warn,
        AgentTaskStatus.Running => StatusPalette.Info,
        _ => StatusPalette.Neutral
    };

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Avalonia.AvaloniaProperty.UnsetValue;
}
