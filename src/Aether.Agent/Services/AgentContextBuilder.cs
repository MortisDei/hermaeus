using Aether.Agent.Models;
using Aether.Rag;
using Aether.Rag.Storage;

namespace Aether.Agent.Services;

public sealed class AgentContextBuilder : IAgentContextBuilder
{
    private readonly IAgentWorkspaceTools _workspaceTools;
    private readonly RagQueryService _rag;
    private readonly SqliteRagStore _ragStore;
    private readonly IAgentWorkspaceMemoryStore _workspaceMemory;

    public AgentContextBuilder(
        IAgentWorkspaceTools workspaceTools,
        RagQueryService rag,
        SqliteRagStore ragStore,
        IAgentWorkspaceMemoryStore workspaceMemory)
    {
        _workspaceTools = workspaceTools;
        _rag = rag;
        _ragStore = ragStore;
        _workspaceMemory = workspaceMemory;
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
                "Only approved draft patch application may write files; command execution, network access, commit, and push are blocked."
            ]
        };

        AddWorkspaceContext(pack, options);
        await AddWorkspaceMemoryAsync(pack, options, ct);
        await AddRagContextAsync(pack, state, options, ct);
        return pack;
    }

    private async Task AddWorkspaceMemoryAsync(AgentContextPack pack, AgentWorkspaceOptions options, CancellationToken ct)
    {
        try
        {
            var entries = await _workspaceMemory.ListAsync(options.WorkspaceRoot, ct);
            foreach (var entry in entries.OrderByDescending(e => e.UpdatedAt).Take(options.MaxContextItems))
            {
                pack.RetrievedMemory.Add(new AgentRetrievedItem(
                    "workspace-memory",
                    entry.Title,
                    entry.Body,
                    1.0,
                    entry.UpdatedAt));
            }

            if (entries.Count > 0)
                pack.KnownRisks.Add($"Workspace memory loaded: {entries.Count} note(s).");
        }
        catch (Exception ex)
        {
            pack.KnownRisks.Add($"Workspace memory unavailable: {ex.Message}");
        }
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
            .Take(1)
            .ToList();

        return words.Count == 0 ? string.Empty : words[0];
    }
}
