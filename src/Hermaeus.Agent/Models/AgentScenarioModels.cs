using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hermaeus.Agent.Models;

/// <summary>
/// One scenario's on-disk definition (scenario.json), loaded by
/// <see cref="Hermaeus.Agent.Services.IAgentScenarioStore"/>. Serialized with
/// <see cref="Hermaeus.Agent.Services.AgentJson"/> (snake_case, case-insensitive
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
    /// <summary>
    /// Overrides <see cref="Hermaeus.Core.Models.AgentSettings.MaxOrchestrationSteps"/>
    /// for this scenario's run only; null keeps the default (r15
    /// 03-scenarios-and-hardening.md 3.1). Only meaningful for a scenario
    /// that exercises orchestration.
    /// </summary>
    public int? MaxOrchestrationSteps { get; set; }
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
/// populated. See <see cref="Hermaeus.Agent.Services.AgentScenarioChecks"/> for
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
    /// <summary>Ordered list of expected <see cref="AgentSubTaskStatus"/> names for the parent's SubTaskPlan, in spec order.</summary>
    public List<string> ExpectSubtaskStatuses { get; set; } = [];
    /// <summary>Substrings that must all appear in the parent's final message (the synthesis report).</summary>
    public List<string> ExpectReportContains { get; set; } = [];
    /// <summary>
    /// When true, no lesson left active in the sandbox lesson store after the
    /// run may match an approval-policy claim token (r23 4.5,
    /// AgentApprovalClaimTokens). A claim the model attempted and 4.2
    /// rejected passes this check (nothing was stored); a claim that made it
    /// into the store some other way fails it.
    /// </summary>
    public bool? ForbidActiveLessonMatching { get; set; }
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
    string? RunError,
    AgentScenarioEvidence? Evidence = null);

public enum AgentScenarioEvidenceStatus
{
    Unknown,
    Pass,
    Fail,
    Stale
}

public sealed record AgentScenarioEvidence(
    string ModelId,
    string ModelContentHash,
    string ScenarioDefinitionHash,
    string EvaluatorContractVersion,
    string RuntimeIdentity,
    DateTime ObservedAtUtc);

/// <summary>
/// The identity contract for persisted Scenario Eval results. A result is only
/// applicable when the model identity, model content, scenario definition and
/// evaluator contract still match. Runtime identity is retained as provenance,
/// but does not invalidate a result by itself.
/// </summary>
public static class AgentScenarioEvidenceContract
{
    public const string EvaluatorContractVersion = "r32-agent-scenario-evaluator-v1";
    public const string ModelIdKey = "model_id";
    public const string ModelContentHashKey = "model_content_hash";
    public const string ScenarioDefinitionHashKey = "scenario_definition_hash";
    public const string EvaluatorContractVersionKey = "evaluator_contract_version";
    public const string RuntimeIdentityKey = "runtime_identity";
    public const string ObservedAtUtcKey = "observed_at_utc";
    public const string ResultJsonKey = "result_json";

    private static readonly JsonSerializerOptions HashJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public static string ComputeScenarioDefinitionHash(AgentScenario scenario) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(scenario.Manifest, HashJsonOptions)))).ToLowerInvariant();

    public static async Task<string> ComputeModelContentHashAsync(string modelId, CancellationToken ct = default)
    {
        if (!File.Exists(modelId))
            return string.Empty;

        await using var stream = new FileStream(
            modelId,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
    }

    public static AgentScenarioEvidence Create(
        AgentScenario scenario,
        string modelId,
        string modelContentHash,
        string runtimeIdentity,
        DateTime observedAtUtc) => new(
        modelId,
        modelContentHash,
        ComputeScenarioDefinitionHash(scenario),
        EvaluatorContractVersion,
        string.IsNullOrWhiteSpace(runtimeIdentity) ? "Unknown" : runtimeIdentity,
        observedAtUtc.ToUniversalTime());

    public static AgentScenarioEvidenceStatus Assess(
        AgentScenarioRunResult result,
        AgentScenario scenario,
        string currentModelId,
        string currentModelContentHash)
    {
        var evidence = result.Evidence;
        if (evidence is null)
            return AgentScenarioEvidenceStatus.Unknown;

        if (!string.Equals(result.ScenarioId, scenario.Manifest.Id, StringComparison.Ordinal))
            return AgentScenarioEvidenceStatus.Stale;

        if (!string.Equals(evidence.ModelId, currentModelId, StringComparison.OrdinalIgnoreCase))
            return AgentScenarioEvidenceStatus.Stale;

        var currentScenarioHash = ComputeScenarioDefinitionHash(scenario);
        if (string.IsNullOrWhiteSpace(evidence.ModelContentHash)
            || string.IsNullOrWhiteSpace(currentModelContentHash)
            || string.IsNullOrWhiteSpace(evidence.ScenarioDefinitionHash)
            || string.IsNullOrWhiteSpace(evidence.EvaluatorContractVersion))
            return AgentScenarioEvidenceStatus.Unknown;

        if (!string.Equals(evidence.ModelContentHash, currentModelContentHash, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(evidence.ScenarioDefinitionHash, currentScenarioHash, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(evidence.EvaluatorContractVersion, EvaluatorContractVersion, StringComparison.Ordinal))
            return AgentScenarioEvidenceStatus.Stale;

        return result.Passed ? AgentScenarioEvidenceStatus.Pass : AgentScenarioEvidenceStatus.Fail;
    }
}

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
