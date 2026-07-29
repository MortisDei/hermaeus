using Avalonia.Media;
using Hermaeus.Core.Models;

namespace Hermaeus.Desktop.Views;

/// <summary>
/// The six brand-palette accent colours a project's colour dot may use
/// (docs/mascot.md "Brand colour palette"; r24 doc 01 1.1). Keys mirror
/// <see cref="ProjectColors"/> exactly, so a project's stored key can never
/// point at a colour this app cannot render.
/// </summary>
public static class ProjectPalette
{
    public static readonly IBrush Forest = new SolidColorBrush(Color.Parse("#436B3F"));
    public static readonly IBrush Copper = new SolidColorBrush(Color.Parse("#B87333"));
    public static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#D19A42"));
    public static readonly IBrush Teal = new SolidColorBrush(Color.Parse("#2F6F6D"));
    public static readonly IBrush Indigo = new SolidColorBrush(Color.Parse("#3D4A6B"));
    public static readonly IBrush Berry = new SolidColorBrush(Color.Parse("#7B3D5A"));

    public static IBrush Resolve(string? key) => key switch
    {
        ProjectColors.Forest => Forest,
        ProjectColors.Copper => Copper,
        ProjectColors.Amber => Amber,
        ProjectColors.Teal => Teal,
        ProjectColors.Indigo => Indigo,
        ProjectColors.Berry => Berry,
        _ => Forest
    };
}
