using Avalonia.Media;

namespace Hermaeus.Desktop.Views;

/// <summary>
/// The small set of semantic status brushes (r16 03-workbench-and-desktop.md
/// 3.5): before this, the same ok/warn/error meaning was drawn from three
/// different vocabularies depending which view you were in (Brushes.LimeGreen-
/// style named brushes in AgentSubTaskStatusColorConverter/
/// AgentScenarioStatusColorConverter, material hex in
/// FitTierBrushConverter/UpdateStatusBrushConverter/ErrorColorConverter -
/// slightly different greens/reds despite meaning the same thing). These
/// values read on both theme variants; the app runs
/// <c>RequestedThemeVariant="Default"</c>, so no separate dark-mode set is
/// needed.
/// </summary>
public static class StatusPalette
{
    public static readonly IBrush Ok = new SolidColorBrush(Color.Parse("#4CAF50"));
    public static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#FF9800"));
    public static readonly IBrush Error = new SolidColorBrush(Color.Parse("#F44336"));
    public static readonly IBrush Info = new SolidColorBrush(Color.Parse("#2196F3"));
    public static readonly IBrush Neutral = new SolidColorBrush(Color.Parse("#757575"));
}
