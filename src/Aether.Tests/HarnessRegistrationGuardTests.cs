using System.Reflection;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

/// <summary>
/// docs/review/04-tech-debt.md item 4.1. The custom harness convention
/// (an "internal static class FooTests" whose public parameterless
/// Task-returning methods are wired up one by one as HarnessCase entries in
/// HarnessCases, consumed by a [Theory]/[MemberData] wrapper) has no
/// compiler or xunit-level check that every such method actually got wired
/// up: r6 found two TraceBindingTests methods that existed but were never
/// registered and silently never ran across multiple releases. This guard
/// makes that failure mode loud instead of silent.
/// </summary>
internal static class HarnessRegistrationGuardTests
{
    /// <summary>
    /// Every "internal static class FooTests" that participates in the
    /// harness convention. Add a new class here the same commit you add it.
    /// </summary>
    private static readonly Type[] KnownHarnessTestClasses =
    [
        typeof(AcceptanceTests),
        typeof(BackupMigrationTests),
        typeof(LocalApiTests),
        typeof(MarkdownViewerTests),
        typeof(McpTests),
        typeof(AgentTests),
        typeof(RagTests),
        typeof(ServiceTests),
        typeof(SetupWizardOnboardingTests),
        typeof(SetupWizardMigrationTests),
        typeof(SingleInstanceGuardTests),
        typeof(TraceBindingTests),
        typeof(TtsTests),
        typeof(VoicePronunciationTests),
        typeof(VoiceTests)
    ];

    /// <summary>
    /// "ClassName.MethodName" entries that intentionally look like harness
    /// cases (public static, parameterless, Task-returning) but are shared
    /// helpers rather than registered cases in their own right.
    /// </summary>
    private static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
    {
        // (none yet)
    };

    public static Task EveryHarnessTestClassMethodIsRegisteredExactlyOnce()
    {
        var registered = CollectRegisteredCaseCounts();
        var problems = new List<string>();

        foreach (var type in KnownHarnessTestClasses)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.GetParameters().Length > 0 || !typeof(Task).IsAssignableFrom(method.ReturnType))
                    continue;

                var key = $"{type.Name}.{method.Name}";
                if (Allowlist.Contains(key))
                    continue;

                var count = registered.GetValueOrDefault(key, 0);
                if (count == 0)
                    problems.Add($"{key} is never registered in any HarnessCases list and will never run");
                else if (count > 1)
                    problems.Add($"{key} is registered {count} times in HarnessCases lists");
            }
        }

        True(problems.Count == 0, "Harness registration problems:\n" + string.Join('\n', problems));
        return Task.CompletedTask;
    }

    private static Dictionary<string, int> CollectRegisteredCaseCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var property in typeof(HarnessCases).GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (property.GetValue(null) is not IEnumerable<object[]> cases)
                continue;

            foreach (var row in cases)
            {
                if (row.Length != 1 || row[0] is not HarnessCase harnessCase)
                    continue;

                var method = harnessCase.Run.Method;
                var key = $"{method.DeclaringType!.Name}.{method.Name}";
                counts[key] = counts.GetValueOrDefault(key, 0) + 1;
            }
        }
        return counts;
    }
}
