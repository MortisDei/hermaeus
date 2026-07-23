using Avalonia;
using Avalonia.Styling;

namespace Hermaeus.Desktop;

/// <summary>
/// r21 follow-up: pushes the user's Settings &gt; Interface &gt; Theme choice
/// (System/Dark/Light) into <see cref="Application.RequestedThemeVariant"/>.
/// Previously this setting was saved but never applied to anything (same
/// class of bug <see cref="AppFontService"/> fixes for fonts).
/// </summary>
internal static class AppThemeService
{
    internal const string DefaultTheme = "System";

    internal static void Apply(string? theme)
    {
        if (Application.Current is not { } app)
            return;

        app.RequestedThemeVariant = theme switch
        {
            "Dark" => ThemeVariant.Dark,
            "Light" => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }
}
