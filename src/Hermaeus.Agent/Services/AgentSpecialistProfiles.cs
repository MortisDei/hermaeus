namespace Hermaeus.Agent.Services;

/// <summary>
/// Fixed, in-code catalog of sub-task specialist profiles (r15
/// 01-subtask-orchestration.md 1.3). Not user-editable this round: a
/// profile only ever contributes a handful of constraint strings appended
/// to a child task's <see cref="Hermaeus.Agent.Models.AgentTaskState.Constraints"/>
/// at creation; it can never change what a child is allowed to do, only
/// what it pays attention to.
/// </summary>
public static class AgentSpecialistProfiles
{
    public const string DefaultProfileName = "general";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Catalog =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["general"] =
            [
                "Keep changes focused on the stated goal; avoid unrelated refactoring."
            ],
            ["correctness"] =
            [
                "Focus on logic errors, edge cases, and incorrect behavior.",
                "Prefer minimal, targeted fixes over rewrites.",
                "Report findings with a concrete repro or failure scenario."
            ],
            ["security"] =
            [
                "Focus on input handling, process launching, path traversal, and secrets; report findings rather than refactoring broadly.",
                "Flag anything that widens what an untrusted input can do.",
                "Do not attempt to exploit anything outside the workspace."
            ],
            ["tests"] =
            [
                "Focus on test coverage and regression risk for the stated goal.",
                "Prefer adding or updating tests over changing production logic.",
                "Call out any behavior that cannot be verified without a live model or network."
            ],
            ["performance"] =
            [
                "Focus on algorithmic complexity, allocations, and I/O patterns relevant to the goal.",
                "Prefer measurements or clear reasoning over speculative optimization.",
                "Avoid changes that trade correctness for speed."
            ],
            ["docs"] =
            [
                "Focus on documentation accuracy for the stated goal.",
                "Do not document planned behavior as existing behavior.",
                "Keep changes scoped to the docs that actually changed."
            ]
        };

    /// <summary>All known profile names, for tool/prompt descriptions and validation.</summary>
    public static IReadOnlyList<string> Names { get; } = Catalog.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();

    public static bool IsKnown(string? profileName) =>
        !string.IsNullOrWhiteSpace(profileName) && Catalog.ContainsKey(profileName.Trim());

    /// <summary>
    /// Resolves a profile's focus constraints. Unknown names fall back to
    /// <see cref="DefaultProfileName"/> as defense in depth; the
    /// plan_subtasks approval-time validation (doc 01 1.2 step 3) rejects
    /// unknown profile names outright, so this fallback should never
    /// actually be exercised by an approved plan.
    /// </summary>
    public static IReadOnlyList<string> Resolve(string? profileName)
    {
        var key = string.IsNullOrWhiteSpace(profileName) ? DefaultProfileName : profileName.Trim();
        return Catalog.TryGetValue(key, out var constraints) ? constraints : Catalog[DefaultProfileName];
    }
}
