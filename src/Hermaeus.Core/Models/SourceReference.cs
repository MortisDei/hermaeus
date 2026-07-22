namespace Hermaeus.Core.Models;

/// <summary>
/// What kind of thing a <see cref="SourceReference"/> points back to. The
/// long-term goal (docs/review/archived/r1/07-roadmap.md, "provenance everywhere") is for
/// any answer to be traceable to memories, chunks, files, and tool output
/// through this one shared shape rather than each surface inventing its own.
/// </summary>
public enum ProvenanceKind
{
    Rag,
    Memory,
    Workspace,
    AgentTool
}

/// <summary>
/// A pointer back to where a piece of content actually came from: a RAG
/// chunk, a memory, a workspace file, or an agent tool result. Deliberately
/// small and serializable so it can ride on traces, tool results, and UI
/// view models without pulling in project-specific types.
/// </summary>
public sealed record SourceReference(
    ProvenanceKind Kind,
    string Title,
    string? Locator = null,
    string? Snippet = null,
    double? Score = null,
    DateTime? Timestamp = null);
