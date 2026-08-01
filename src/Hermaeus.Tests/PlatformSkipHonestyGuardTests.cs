using System.Text.RegularExpressions;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// r29 doc 04 4.3: a [Fact] whose body opens with an OperatingSystem.Is* early
/// return reports Passed on the platform it does not support, so a green leg
/// claims to have verified work it never executed. Use [WindowsOnlyFact], which
/// reports Skipped.
///
/// Deliberately dumb, in the style of BindingExpressionGuardTests and
/// DocsCoverageGuardTests: one scan over the test sources. A guard that needs
/// maintenance is a guard that gets deleted the first time it is inconvenient.
/// </summary>
public sealed class PlatformSkipHonestyGuardTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    /// <summary>
    /// A [Fact] attribute, then a signature and an opening brace, then an
    /// OperatingSystem.Is* guard that returns rather than asserting.
    /// </summary>
    private static readonly Regex FactWithPlatformEarlyReturn = new(
        @"\[Fact(\([^)]*\))?\][^\[{]*\{\s*if\s*\(\s*!?\s*OperatingSystem\.Is\w+\(\)\s*\)\s*return\s*;",
        RegexOptions.Compiled);

    [Fact]
    public void No_fact_skips_a_platform_by_returning_early()
    {
        var tests = Path.Combine(RepoRoot, "src", "Hermaeus.Tests");
        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;
            // This file carries the offending shape as a literal, to prove the
            // regex can fail.
            if (Path.GetFileName(path) == "PlatformSkipHonestyGuardTests.cs")
                continue;

            var source = File.ReadAllText(path);
            foreach (Match match in FactWithPlatformEarlyReturn.Matches(source))
            {
                var line = source.Take(match.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetFileName(path)}:{line}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These tests return early on the platform they do not support, so xunit records them as Passed for work "
            + "that never ran. Use [WindowsOnlyFact], which reports Skipped, and delete the early return: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void The_guard_detects_the_shape_it_exists_to_stop()
    {
        // A guard that cannot fail is worse than none.
        Assert.Matches(FactWithPlatformEarlyReturn,
            """
            [Fact]
            public void Something()
            {
                if (!OperatingSystem.IsWindows()) return;
                Assert.True(true);
            }
            """);
    }

    [Fact]
    public void The_guard_leaves_alone_a_test_that_branches_to_platform_appropriate_assertions()
    {
        // BackupMigrationTests and ServiceTests do this, and it is correct: the
        // test asserts on both platforms, it just asserts different things.
        Assert.DoesNotMatch(FactWithPlatformEarlyReturn,
            """
            [Fact]
            public void Something()
            {
                if (OperatingSystem.IsWindows())
                    Assert.Equal("\\", separator);
                else
                    Assert.Equal("/", separator);
            }
            """);
    }
}
