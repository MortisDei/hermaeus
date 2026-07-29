using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Hermaeus.Desktop.Views;

/// <summary>
/// r25 doc 04 4.2: in the per-case breakdown, a case the runner-up actually won
/// is the detail a single headline number hides, so it is emphasised rather than
/// left to the reader to spot by comparing two columns of percentages.
/// </summary>
public static class BenchmarkConverters
{
    public static readonly IValueConverter RunnerUpEmphasis =
        new FuncValueConverter<bool, FontWeight>(won => won ? FontWeight.SemiBold : FontWeight.Normal);
}
