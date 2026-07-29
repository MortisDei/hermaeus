namespace Hermaeus.Core.Models;

public enum RecallKind { Message, Task, Memory, Document }

/// <summary>
/// Where Enter goes for a <see cref="RecallHit"/>. Only the fields matching
/// the hit's <see cref="RecallHit.Kind"/> are populated; a typed instruction
/// rather than a string so navigation can never point nowhere (r24 doc 02 2.4).
/// </summary>
public sealed record RecallTarget(
    string ConversationId = "",
    int MessageIndex = -1,
    string TaskId = "",
    string MemoryId = "",
    string DatasetId = "",
    string ChunkId = "");

public sealed record RecallHit(
    RecallKind Kind,
    string Title,
    string Snippet,
    DateTime Timestamp,
    string ProjectId,
    double Score,
    RecallTarget Target);

/// <summary>
/// The fused result of a Recall query: hits in ranked order, the names of
/// any source that timed out (named, never silently dropped), and whether
/// the answer is keyword-only because no embedding service was reachable.
/// </summary>
public sealed record RecallResult(
    IReadOnlyList<RecallHit> Hits,
    IReadOnlyList<string> OmittedSources,
    bool KeywordOnly);

/// <summary>
/// r24 doc 02 2.3: the durable, meaningful parts of an agent task to index,
/// assembled by the Agent-side caller (goal, summary, final answer,
/// reservations, plan step descriptions, report.md when one exists) so the
/// Recall indexing service in Hermaeus.Services never needs to depend on
/// Hermaeus.Agent - both sit on Core, as peers, per the solution map.
/// </summary>
public sealed record RecallTaskInput(
    string TaskId,
    string? ParentTaskId,
    string Goal,
    string Body,
    string ProjectId,
    DateTime CreatedAt);
