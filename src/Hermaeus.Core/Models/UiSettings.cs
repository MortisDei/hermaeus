namespace Hermaeus.Core.Models;

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
    /// Minimizing the window hides it to the tray instead of the taskbar.
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Closing the window hides it to the tray instead of exiting.
    ///
    /// This used to be the same setting as <see cref="MinimizeToTray"/>, so
    /// anyone who wanted minimize-to-tray also had no way to actually close the
    /// app, and the checkbox labelled "Minimize to tray" carried a tooltip
    /// describing what closing did. Defaults to true, which is exactly the
    /// previous behaviour for an existing install.
    /// </summary>
    public bool CloseToTray { get; set; } = true;

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

    /// <summary>
    /// Font family for page titles and headings. Empty means the OS-default
    /// UI font (r21: replaces the embedded Cinzel brand typeface).
    /// </summary>
    public string HeadingFontFamily { get; set; } = string.Empty;

    /// <summary>
    /// Font family for chat and general UI text. Empty means the OS-default
    /// UI font (r21: replaces the embedded Source Sans 3 brand typeface).
    /// </summary>
    public string BodyFontFamily { get; set; } = string.Empty;

    /// <summary>
    /// Font family for code blocks and other technical/monospace text.
    /// Empty means the OS-default monospace font (r21: replaces the
    /// embedded JetBrains Mono brand typeface).
    /// </summary>
    public string MonoFontFamily { get; set; } = string.Empty;

    /// <summary>
    /// r24 doc 01: the currently active project, or empty for "No project".
    /// Switching never rewrites any existing record; it only changes what
    /// new conversations, tasks, datasets and memories default to.
    /// </summary>
    public string ActiveProjectId { get; set; } = string.Empty;
}
