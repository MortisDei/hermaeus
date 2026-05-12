using Aether.Agent.Models;
using Aether.Rag;
using Aether.Rag.Storage;

namespace Aether.Agent.Services;

public sealed class AgentContextBuilder : IAgentContextBuilder
{
    private readonly IAgentWorkspaceTools _workspaceTools;
    private readonly RagQueryService _rag;
    private readonly SqliteRagStore _ragStore;

    public AgentContextBuilder(
        IAgentWorkspaceTools workspaceTools,
        RagQueryService rag,
        SqliteRagStore ragStore)
    {
        _workspaceTools = workspaceTools;
        _rag = rag;
        _ragStore = ragStore;
    }

    public async Task<AgentContextPack> BuildAsync(
        AgentTaskState state,
        AgentWorkspaceOptions options,
        CancellationToken ct = default)
    {
        var pack = new AgentContextPack
        {
            CurrentGoal = state.Goal,
            ActiveStep = state.ActiveStep,
            Constraints = state.Constraints.Take(8).ToList(),
            TaskStateSummary = state.Summary,
            ToolResults = state.ToolResults.TakeLast(5).ToList(),
            KnownRisks =
            [
                "Read-only tools may inspect local files under the selected workspace root.",
                "Writes, command execution, network access, commit, and push are not executed by this alpha agent."
            ]
        };

        AddWorkspaceContext(pack, options);
        await AddRagContextAsync(pack, state, options, ct);
        return pack;
    }

    private void AddWorkspaceContext(AgentContextPack pack, AgentWorkspaceOptions options)
    {
        try
        {
            var query = PickSearchQuery(pack.CurrentGoal);
            var results = string.IsNullOrWhiteSpace(query)
                ? _workspaceTools.ListFiles(options)
                    .Take(options.MaxContextItems)
                    .Select(path => new AgentRetrievedItem("workspace", path, path, 0))
                    .ToList()
                : _workspaceTools.SearchFiles(options, query)
                    .Take(options.MaxContextItems)
                    .Select(r => new AgentRetrievedItem("workspace", r.RelativePath, r.Snippet, 0, r.ModifiedUtc))
                    .ToList();

            pack.RetrievedFiles.AddRange(results);
        }
        catch (Exception ex)
        {
            pack.KnownRisks.Add($"Workspace context unavailable: {ex.Message}");
        }
    }

    private async Task AddRagContextAsync(
        AgentContextPack pack,
        AgentTaskState state,
        AgentWorkspaceOptions options,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.RagDatasetId)) return;

        try
        {
            var datasets = await _ragStore.GetDatasetsAsync(ct);
            var dataset = datasets.FirstOrDefault(d => d.Id == options.RagDatasetId);
            if (dataset is null)
            {
                pack.KnownRisks.Add("Selected RAG dataset was not found.");
                return;
            }

            var retrieval = await _rag.RetrieveAsync(
                dataset.Id,
                string.IsNullOrWhiteSpace(state.ActiveStep) ? state.Goal : $"{state.Goal}\n{state.ActiveStep}",
                new RagQueryOptions(TopK: Math.Max(1, Math.Min(5, options.MaxContextItems))),
                ct);

            pack.RetrievedMemory.AddRange(retrieval.Selected
                .Take(options.MaxContextItems)
                .Select(s => new AgentRetrievedItem(
                    "rag",
                    s.Chunk.SourceTitle,
                    s.Chunk.Content,
                    s.Score,
                    s.Chunk.SourceModifiedUtc)));
        }
        catch (Exception ex)
        {
            pack.KnownRisks.Add($"RAG context unavailable: {ex.Message}");
        }
    }

    private static string PickSearchQuery(string goal)
    {
        var words = goal
            .Split([' ', '\t', '\r', '\n', '.', ',', ':', ';', '/', '\\', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4)
            .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key.Length)
            .Select(g => g.Key)
            .Take(2)
            .ToList();

        return words.Count == 0 ? string.Empty : words[0];
    }
}
