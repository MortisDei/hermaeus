using System.Text.RegularExpressions;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// A binding expression that needs a runtime type lookup crashes the app on
/// the layout pass rather than rendering an empty row, and no unit test sees
/// it because the failure is in Avalonia's template builder.
///
/// This happened: r28 wrote
/// <c>{Binding $parent[ItemsControl].((vm:ActivityViewModel)DataContext).OpenCommand}</c>
/// in ActivityView, which built, passed 1745 tests, and took the app down
/// with "Unable to resolve type vm:ActivityViewModel" the first time the
/// Activity panel was opened. Every other `$parent[...]` binding in this
/// repository (ChatView, DoctorView, MainWindow) omits the cast and works.
///
/// Deliberately dumb, in the same style as DocsCoverageGuardTests: one regex
/// over the axaml files. A guard that needs maintenance is a guard that gets
/// deleted the first time it is inconvenient.
/// </summary>
public sealed class BindingExpressionGuardTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    /// <summary>Matches a prefixed type cast inside a binding, e.g. <c>((vm:Foo)DataContext)</c>.</summary>
    private static readonly Regex PrefixedCastInBinding = new(
        @"\{Binding[^}]*\(\(\s*\w+:", RegexOptions.Compiled);

    [Fact]
    public void No_binding_casts_to_a_prefixed_type()
    {
        var desktop = Path.Combine(RepoRoot, "src", "Hermaeus.Desktop");
        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(desktop, "*.axaml", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');
            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (PrefixedCastInBinding.IsMatch(line))
                    offenders.Add($"{relative}:{lineNumber}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These bindings cast to a prefixed type, which needs a runtime type lookup that fails inside a "
            + "compiled DataTemplate and crashes the app on the layout pass. Reach the parent's command with "
            + "$parent[ItemsControl].DataContext.SomeCommand instead, as every other view here does: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void The_guard_detects_the_expression_that_caused_the_crash()
    {
        // A guard that cannot fail is worse than none.
        Assert.Matches(PrefixedCastInBinding,
            """Command="{Binding $parent[ItemsControl].((vm:ActivityViewModel)DataContext).OpenCommand}" """);
        Assert.DoesNotMatch(PrefixedCastInBinding,
            """Command="{Binding $parent[ItemsControl].DataContext.OpenCommand}" """);
    }
}
