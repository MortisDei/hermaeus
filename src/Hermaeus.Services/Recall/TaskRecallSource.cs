using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services.Recall;

public sealed class TaskRecallSource : IRecallSource
{
    private readonly RecallIndexStore _index;

    public TaskRecallSource(RecallIndexStore index) => _index = index;

    public string Name => "Agent tasks";

    public async Task<IReadOnlyList<RecallHit>> SearchAsync(string query, string projectScope, CancellationToken ct)
    {
        var (results, _) = await _index.SearchAsync("task", query, projectScope, ct);
        var hits = new List<RecallHit>(results.Count);
        foreach (var e in results)
        {
            var title = e.Title;
            if (!string.IsNullOrWhiteSpace(e.SubId))
            {
                var parentGoal = await _index.GetTitleAsync("task", e.SubId, ct);
                if (!string.IsNullOrWhiteSpace(parentGoal))
                    title = $"{title} (sub task of: {parentGoal})";
            }

            hits.Add(new RecallHit(
                RecallKind.Task,
                title,
                RecallSnippet.Build(e.Body, query),
                e.CreatedAt,
                e.ProjectId,
                e.RelevanceScore,
                new RecallTarget(TaskId: e.SourceId)));
        }

        return hits;
    }
}
