namespace Aether.Agent.Models;

/// <summary>
/// One scenario's on-disk definition (scenario.json), loaded by
/// <see cref="Aether.Agent.Services.IAgentScenarioStore"/>. Serialized with
/// <see cref="Aether.Agent.Services.AgentJson"/> (snake_case, case-insensitive
/// read), same as every other agent-owned JSON document.
/// </summary>
public sealed class AgentScenarioManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public int MaxSteps { get; set; } = 8;
    /// <summary>Tool names auto-approved when the safety gate would otherwise pause for a human decision.</summary>
    public List<string> AutoApprove { get; set; } = [];
    public List<AgentScenarioSeedMemory> SeedMemory { get; set; } = [];
    public List<AgentScenarioSeedLesson> SeedLessons { get; set; } = [];
    /// <summary>relativeName -&gt; content, written into a sandbox directory that is a SIBLING of the workspace, never inside it.</summary>
    public Dictionary<string, string> OutsideFiles { get; set; } = [];
    /// <summary>When true, a step that throws is not automatically a scenario failure; evaluation still proceeds from the persisted state.</summary>
    public bool AllowRunError { get; set; }
    public AgentScenarioExpectations Expect { get; set; } = new();
}

public sealed class AgentScenarioSeedMemory
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public sealed class AgentScenarioSeedLesson
{
    public string Claim { get; set; } = string.Empty;
    public string Guidance { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    /// <summary><see cref="AgentLessonOutcome"/> name; defaults to Observation on parse failure.</summary>
    public string Outcome { get; set; } = "observation";
}

/// <summary>
/// Deterministic pass/fail conditions for a scenario run. Every list or
/// nullable property is optional; a check only runs when its property is
/// populated. See <see cref="Aether.Agent.Services.AgentScenarioChecks"/> for
/// evaluation semantics.
/// </summary>
public sealed class AgentScenarioExpectations
{
    public List<string> FinalStatusAnyOf { get; set; } = [];
    public List<string> RequireApprovalFor { get; set; } = [];
    public List<string> ForbidExecutionOf { get; set; } = [];
    public List<string> ExpectBlocked { get; set; } = [];
    public List<string> MustReadAnyOf { get; set; } = [];
    public List<string> MustNotRead { get; set; } = [];
    /// <summary>Workspace-relative paths, or exactly ["*"] to mean the whole workspace.</summary>
    public List<string> FilesUnchanged { get; set; } = [];
    public List<string> MustChange { get; set; } = [];
    public List<string> AnswerMustMentionAny { get; set; } = [];
    public List<string> AnswerMustNotMention { get; set; } = [];
    public int? MaxNewLessons { get; set; }
    /// <summary>"low" | "medium" | "high" (any AgentRiskLevel name); the pending/gated action's risk must be at least this level.</summary>
    public string? PendingRiskAtLeast { get; set; }
    public bool? ExpectRevertiblePatch { get; set; }
}

/// <summary>A loaded scenario: manifest plus the resolved paths the runner copies from.</summary>
public sealed record AgentScenario(
    AgentScenarioManifest Manifest,
    string SourceDirectory,
    string WorkspaceDirectory,
    bool IsBuiltIn);

public sealed record AgentScenarioCheckResult(
    string CheckId,
    bool Passed,
    string Detail);

public sealed record AgentScenarioRunResult(
    string ScenarioId,
    string Title,
    bool Passed,
    IReadOnlyList<AgentScenarioCheckResult> Checks,
    int Steps,
    long DurationMs,
    string FinalStatus,
    string? RunError);

public sealed class AgentScenarioSuiteResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ModelId { get; set; } = string.Empty;
    public List<AgentScenarioRunResult> Results { get; set; } = [];
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int PassedCount => Results.Count(r => r.Passed);
    public int Total => Results.Count;
}

/// <summary>Workspace-relative, forward-slash-normalized file changes between a scenario's before/after hash snapshots.</summary>
public sealed record AgentScenarioFileDiff(
    IReadOnlyList<string> ChangedPaths,
    IReadOnlyList<string> CreatedPaths,
    IReadOnlyList<string> DeletedPaths);
