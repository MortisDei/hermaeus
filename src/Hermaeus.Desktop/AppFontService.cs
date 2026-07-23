using Avalonia;
using Avalonia.Media;

namespace Hermaeus.Desktop;

/// <summary>
/// r21: pushes the user's font choices (Settings &gt; Interface &gt; Typography)
/// into the app resource dictionary that <c>AppStyles.axaml</c> and the
/// hardcoded monospace spots (<see cref="Controls.MarkdownViewer"/> and the
/// various code/diff views) read from. Defaults are OS-native fonts, not the
/// embedded brand typefaces this replaced.
/// </summary>
internal static class AppFontService
{
    internal const string DefaultHeadingFont = "Segoe UI,sans-serif";
    internal const string DefaultBodyFont = "Segoe UI,sans-serif";
    internal const string DefaultMonoFont = "Consolas,monospace";
    internal const double DefaultChatFontSize = 14;

    /// <summary>
    /// Mirrors the "AppMonoFont" app resource for code paths that build
    /// <see cref="FontFamily"/> objects directly in C# instead of through a
    /// XAML DynamicResource.
    /// </summary>
    internal static FontFamily MonoFont { get; private set; } = new(DefaultMonoFont);

    internal static void Apply(string? headingFontFamily, string? bodyFontFamily, string? monoFontFamily, double chatFontSize)
    {
        var heading = SafeFontFamily(headingFontFamily, DefaultHeadingFont);
        var body = SafeFontFamily(bodyFontFamily, DefaultBodyFont);
        var mono = SafeFontFamily(monoFontFamily, DefaultMonoFont);
        MonoFont = mono;

        if (Application.Current is not { } app)
            return;

        app.Resources["AppHeadingFont"] = heading;
        app.Resources["AppBodyFont"] = body;
        app.Resources["AppMonoFont"] = mono;
        app.Resources["AppChatFontSize"] = chatFontSize > 0 ? chatFontSize : DefaultChatFontSize;
    }

    private static FontFamily SafeFontFamily(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new FontFamily(fallback);

        try
        {
            return new FontFamily(value);
        }
        catch (Exception)
        {
            // A user-typed family name is free-form text; malformed input
            // (e.g. something that parses as a URI) must degrade to the
            // system default, never crash settings save or app startup.
            return new FontFamily(fallback);
        }
    }
}
