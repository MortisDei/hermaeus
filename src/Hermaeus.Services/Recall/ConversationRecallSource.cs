using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services.Recall;

public sealed class ConversationRecallSource : IRecallSource
{
    private readonly RecallIndexStore _index;

    public ConversationRecallSource(RecallIndexStore index) => _index = index;

    public string Name => "Conversations";

    public async Task<IReadOnlyList<RecallHit>> SearchAsync(string query, string projectScope, CancellationToken ct)
    {
        var (results, _) = await _index.SearchAsync("message", query, projectScope, ct);
        return results.Select(e => new RecallHit(
            RecallKind.Message,
            e.Title,
            RecallSnippet.Build(e.Body, query),
            e.CreatedAt,
            e.ProjectId,
            e.RelevanceScore,
            new RecallTarget(ConversationId: e.SourceId, MessageIndex: int.TryParse(e.SubId, out var idx) ? idx : -1)
        )).ToList();
    }
}
