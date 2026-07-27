using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services.Recall;

/// <summary>Wraps <see cref="IMemoryStore.SearchAsync"/> - it is already hybrid FTS-plus-
/// cosine, so this never reimplements it (doc 02 2.4).</summary>
public sealed class MemoryRecallSource : IRecallSource
{
    private readonly IMemoryStore _memories;

    public MemoryRecallSource(IMemoryStore memories) => _memories = memories;

    public string Name => "Memories";

    public async Task<IReadOnlyList<RecallHit>> SearchAsync(string query, string projectScope, CancellationToken ct)
    {
        var results = await _memories.SearchAsync(query, ct);
        var scoped = string.IsNullOrEmpty(projectScope)
            ? results
            : results.Where(m => m.Scope == MemoryScope.Global || (m.Scope == MemoryScope.Project && m.ScopeId == projectScope));

        return scoped.Select(m => new RecallHit(
            RecallKind.Memory,
            string.IsNullOrWhiteSpace(m.Title) ? Truncate(m.Content) : m.Title,
            RecallSnippet.Build(m.Content, query),
            m.UpdatedAt,
            m.Scope == MemoryScope.Project ? m.ScopeId : string.Empty,
            m.RelevanceScore ?? 0,
            new RecallTarget(MemoryId: m.Id)
        )).ToList();
    }

    private static string Truncate(string text) => text.Length <= 60 ? text : text[..60].TrimEnd() + "...";
}
