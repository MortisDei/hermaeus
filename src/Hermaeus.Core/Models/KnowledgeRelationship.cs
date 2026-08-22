namespace Hermaeus.Core.Models;

/// <summary>
/// Stable, small identifiers for things that can participate in an evidence
/// relationship. They deliberately name existing records rather than forcing
/// their owning subsystems into one persistence model.
/// </summary>
public enum KnowledgeEntityKind
{
    Memory,
    AgentLesson,
    BenchmarkRun,
    ModelProfile,
    RuntimeProfile
}

/// <summary>Bounded relationship vocabulary for evidence-backed knowledge.</summary>
public enum KnowledgeRelationshipKind
{
    RelatedTo,
    DerivedFrom,
    Supports,
    Contradicts,
    Updates,
    Supersedes,
    TestedBy
}

/// <summary>A loose, typed reference to an existing Hermaeus entity.</summary>
public sealed record KnowledgeEntityReference(KnowledgeEntityKind Kind, string Id);

/// <summary>
/// A directed relationship from the containing record to <see cref="Target"/>.
/// The optional evidence uses the shared <see cref="SourceReference"/> shape,
/// including its direct/user-provided/inferred origin.
/// </summary>
public sealed record KnowledgeRelationship(
    KnowledgeEntityReference Target,
    KnowledgeRelationshipKind Kind = KnowledgeRelationshipKind.RelatedTo,
    SourceReference? Evidence = null,
    DateTime? RecordedAt = null);

/// <summary>Query-only explanation of why a related memory was considered.</summary>
public sealed record RelationshipRetrieval(
    string SourceMemoryId,
    string SourceMemoryTitle,
    KnowledgeRelationshipKind Kind,
    SourceReference? Evidence);

/// <summary>
/// Shared relationship semantics. This deliberately remains a one-hop helper,
/// not a graph traversal or a ranking system of its own.
/// </summary>
public static class KnowledgeRelationshipSemantics
{
    public static List<KnowledgeRelationship> Normalize(
        IEnumerable<KnowledgeRelationship>? typed,
        IEnumerable<string>? legacyRelatedMemoryIds)
    {
        var result = new List<KnowledgeRelationship>();
        var seen = new HashSet<(KnowledgeEntityKind Kind, string Id, KnowledgeRelationshipKind Relationship)>(
            EqualityComparer<(KnowledgeEntityKind, string, KnowledgeRelationshipKind)>.Default);

        foreach (var relationship in typed ?? [])
        {
            if (string.IsNullOrWhiteSpace(relationship.Target.Id))
                continue;

            var normalized = relationship with
            {
                Target = relationship.Target with { Id = relationship.Target.Id.Trim() }
            };
            if (seen.Add((normalized.Target.Kind, normalized.Target.Id, normalized.Kind)))
                result.Add(normalized);
        }

        foreach (var id in legacyRelatedMemoryIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var normalizedId = id.Trim();
            if (seen.Add((KnowledgeEntityKind.Memory, normalizedId, KnowledgeRelationshipKind.RelatedTo)))
            {
                result.Add(new KnowledgeRelationship(
                    new KnowledgeEntityReference(KnowledgeEntityKind.Memory, normalizedId)));
            }
        }

        return result;
    }

    /// <summary>
    /// A relationship is written from the older assertion to the assertion
    /// that replaced it: old fact -&gt; superseded by -&gt; current fact. The enum
    /// keeps the bounded vocabulary name <see cref="KnowledgeRelationshipKind.Supersedes"/>.
    /// </summary>
    public static bool IsSuperseded(Memory memory) => memory.Relationships.Any(r =>
        r.Kind == KnowledgeRelationshipKind.Supersedes
        && r.Target.Kind == KnowledgeEntityKind.Memory
        && !string.IsNullOrWhiteSpace(r.Target.Id));

    public static bool IsOneHopExpandable(KnowledgeRelationship relationship) =>
        relationship.Target.Kind == KnowledgeEntityKind.Memory
        && !string.IsNullOrWhiteSpace(relationship.Target.Id);

    public static string DisplayName(KnowledgeRelationshipKind kind) => kind switch
    {
        KnowledgeRelationshipKind.RelatedTo => "related to",
        KnowledgeRelationshipKind.DerivedFrom => "derived from",
        KnowledgeRelationshipKind.Supports => "supports",
        KnowledgeRelationshipKind.Contradicts => "contradicts",
        KnowledgeRelationshipKind.Updates => "updates",
        KnowledgeRelationshipKind.Supersedes => "superseded by",
        KnowledgeRelationshipKind.TestedBy => "tested by",
        _ => kind.ToString()
    };
}
