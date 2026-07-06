namespace Aether.Core.Models;

/// <summary>
/// Which projection produced an <see cref="EvalRun"/>: quick A/B (Compare
/// Models), a saved suite run (Benchmarks), or a retrieval run (RAG eval).
/// </summary>
public enum EvalMode
{
    QuickCompare,
    Suite,
    Retrieval
}

/// <summary>Deterministic expectations a case can be checked against.</summary>
public sealed record EvalExpectations(
    IReadOnlyList<string>? Keywords = null,
    IReadOnlyList<string>? ExpectedSources = null,
    bool? ShouldRefuse = null);

/// <summary>One prompt (and optional retrieval question) asked of a target.</summary>
public sealed record EvalCase(
    string Id,
    string Prompt,
    string? SystemPrompt = null,
    EvalExpectations? Expectations = null);

/// <summary>What a case was run against: a model, optionally paired with a RAG dataset.</summary>
public sealed record EvalTarget(
    string ModelId,
    string? DatasetId = null,
    string? Label = null);

/// <summary>The outcome of running one case against one target.</summary>
public sealed record CaseResult(
    string CaseId,
    string Output,
    long LatencyMs,
    long? FirstTokenMs = null,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    IReadOnlyDictionary<string, double>? Scores = null,
    string? Error = null);

/// <summary>One execution of a set of cases against a target, saved or transient.</summary>
public sealed record EvalRun(
    string Id,
    EvalMode Mode,
    EvalTarget Target,
    IReadOnlyList<CaseResult> CaseResults,
    DateTime StartedAt,
    DateTime? FinishedAt = null,
    string? SuiteId = null);
