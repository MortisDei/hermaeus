namespace Aether.Core.Models;

/// <summary>
/// User interface (UI) configuration including theme, layout preferences,
/// input shortcuts, and notification settings.
/// </summary>
public class UiSettings
{
    /// <summary>
    /// Application theme ("System", "Light", or "Dark").
    /// </summary>
    public string Theme { get; set; } = "System";

    /// <summary>
    /// Use Ctrl+Enter to send chat messages instead of Enter.
    /// </summary>
    public bool CtrlEnterToSend { get; set; } = false;

    /// <summary>
    /// Base font size in points for the UI.
    /// </summary>
    public double FontSize { get; set; } = 14;

    /// <summary>
    /// Start the application minimized.
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>
    /// Show the Quick Chat overlay.
    /// </summary>
    public bool ShowQuickChat { get; set; } = false;

    /// <summary>
    /// Enable tray icon integration.
    /// </summary>
    public bool EnableTrayIcon { get; set; } = true;

    /// <summary>
    /// Minimize to tray instead of taskbar.
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Enable local system hotkeys (e.g., Quick Chat toggle).
    /// </summary>
    public bool EnableLocalHotkeys { get; set; } = true;

    /// <summary>
    /// Enable Windows system-wide hotkeys (requires elevated privileges on Windows).
    /// </summary>
    public bool EnableGlobalHotkeys { get; set; } = false;

    /// <summary>
    /// Show a text label next to each icon in the top toolbar, instead of
    /// relying on hover tooltips to discover what each icon does
    /// (r6 01-first-five-minutes.md 1.1).
    /// </summary>
    public bool ShowNavLabels { get; set; } = false;
}
