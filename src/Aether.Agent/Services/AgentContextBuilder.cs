using Aether.Agent.Models;
using Aether.Core.Services;

namespace Aether.Agent.Services;

public sealed class AgentContextBuilder : IAgentContextBuilder
{
    // Generous per-section token budgets so a single oversized note or chunk
    // cannot flood the agent prompt; selection itself is shared with chat/RAG
    // via ContextPackBuilder.
    private const int MemoryTokenBudget = 4000;
    private const int RagTokenBudget = 4000;
    private const int InstructionsTokenBudget = 3000;

    private readonly IAgentWorkspaceTools _workspaceTools;
    private readonly IAgentRetrievalService _retrieval;
    private readonly IAgentWorkspaceMemoryStore _workspaceMemory;
    private readonly IWorkspaceActivationService _activation;

    public AgentContextBuilder(
        IAgentWorkspaceTools workspaceTools,
        IAgentRetrievalService retrieval,
        IAgentWorkspaceMemoryStore workspaceMemory,
        IWorkspaceActivationService activation)
    {
        _workspaceTools = workspaceTools;
        _retrieval = retrieval;
        _workspaceMemory = workspaceMemory;
        _activation = activation;
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
        await AddProjectInstructionsAsync(pack, options, ct);
        return pack;
    }

    private async Task AddProjectInstructionsAsync(AgentContextPack pack, AgentWorkspaceOptions options, CancellationToken ct)
    {
        try
        {
            var activation = await _activation.ActivateAsync(options.WorkspaceRoot, ct);
            if (activation.InstructionPaths.Count == 0) return;

            var candidates = new List<ContextPart>();
            foreach (var path in activation.InstructionPaths)
            {
                try
                {
                    var read = _workspaceTools.ReadFile(options, path);
                    candidates.Add(new ContextPart("instructions", read.RelativePath, read.Content, Data: read));
                }
                catch
                {
                    // A stale or removed instruction path should not fail context building.
                }
            }

            var packed = ContextPackBuilder.Pack(candidates, InstructionsTokenBudget, maxParts: options.MaxContextItems);
            pack.ProjectInstructions.AddRange(packed.Parts.Select(part =>
            {
                var read = (AgentFileReadResult)part.Data!;
                return new AgentRetrievedItem("instructions", read.RelativePath, part.Content, 1.0, Locator: read.RelativePath);
            }));
        }
        catch (Exception ex)
        {
            pack.KnownRisks.Add($"Project instructions unavailable: {ex.Message}");
        }
    }

    private async Task AddWorkspaceMemoryAsync(AgentContextPack pack, AgentWorkspaceOptions options, CancellationToken ct)
    {
        try
        {
            var entries = await _workspaceMemory.ListAsync(options.WorkspaceRoot, ct);
            var candidates = entries.OrderByDescending(e => e.UpdatedAt)
                .Select(entry => new ContextPart(
                    "workspace-memory", entry.Title, entry.Body, Data: entry))
                .ToList();
            var packed = ContextPackBuilder.Pack(candidates, MemoryTokenBudget, maxParts: options.MaxContextItems);
            foreach (var part in packed.Parts)
            {
                var entry = (AgentWorkspaceMemoryEntry)part.Data!;
                pack.RetrievedMemory.Add(new AgentRetrievedItem(
                    "workspace-memory",
                    entry.Title,
                    part.Content,
                    1.0,
                    entry.UpdatedAt,
                    Locator: entry.Id));
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
                    .Select(path => new AgentRetrievedItem("workspace", path, path, 0, Locator: path))
                    .ToList()
                : _workspaceTools.SearchFiles(options, query)
                    .Take(options.MaxContextItems)
                    .Select(r => new AgentRetrievedItem("workspace", r.RelativePath, r.Snippet, 0, r.ModifiedUtc, Locator: r.RelativePath))
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
            if (!await _retrieval.DatasetExistsAsync(options.RagDatasetId, ct))
            {
                pack.KnownRisks.Add("Selected RAG dataset was not found.");
                return;
            }

            var chunks = await _retrieval.RetrieveAsync(
                options.RagDatasetId,
                string.IsNullOrWhiteSpace(state.ActiveStep) ? state.Goal : $"{state.Goal}\n{state.ActiveStep}",
                Math.Max(1, Math.Min(5, options.MaxContextItems)),
                ct);

            var candidates = chunks
                .Select(c => new ContextPart("rag", c.Title, c.Content, Tokens: c.TokenCount, Data: c))
                .ToList();
            var packed = ContextPackBuilder.Pack(candidates, RagTokenBudget, maxParts: options.MaxContextItems);
            pack.RetrievedMemory.AddRange(packed.Parts
                .Select(part =>
                {
                    var chunk = (RetrievedChunk)part.Data!;
                    return new AgentRetrievedItem(
                        "rag",
                        chunk.Title,
                        part.Content,
                        chunk.Score,
                        chunk.SourceModifiedUtc,
                        Locator: chunk.Locator);
                }));
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
