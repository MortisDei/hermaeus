using Hermaeus.Agent.Models;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Agent.Services;

public sealed class AgentContextBuilder : IAgentContextBuilder
{
    // Generous per-section token budgets so a single oversized note or chunk
    // cannot flood the agent prompt; selection itself is shared with chat/RAG
    // via ContextPackBuilder.
    private const int MemoryTokenBudget = 4000;
    private const int RagTokenBudget = 4000;
    private const int InstructionsTokenBudget = 3000;
    private const int LessonsTokenBudget = 1500;
    private const int SubTaskReportsTokenBudget = 4000;

    private readonly IAgentWorkspaceTools _workspaceTools;
    private readonly IAgentRetrievalService _retrieval;
    private readonly IAgentWorkspaceMemoryStore _workspaceMemory;
    private readonly IWorkspaceActivationService _activation;
    private readonly IAgentTaskStateStore _taskStateStore;
    private readonly ISettingsService _settings;
    private readonly ILessonStore? _lessons;
    private readonly IProjectStateStore? _projectState;

    public AgentContextBuilder(
        IAgentWorkspaceTools workspaceTools,
        IAgentRetrievalService retrieval,
        IAgentWorkspaceMemoryStore workspaceMemory,
        IWorkspaceActivationService activation,
        IAgentTaskStateStore taskStateStore,
        ISettingsService settings,
        ILessonStore? lessons = null,
        IProjectStateStore? projectState = null)
    {
        _workspaceTools = workspaceTools;
        _retrieval = retrieval;
        _workspaceMemory = workspaceMemory;
        _activation = activation;
        _taskStateStore = taskStateStore;
        _settings = settings;
        _lessons = lessons;
        _projectState = projectState;
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
            // r29 doc 03 3.3: the user's words, arriving after the task
            // started. Labelled so the model cannot mistake them for system
            // authority; they sit alongside the goal and constraints, never in
            // the system prompt.
            SteeringInstructions = state.Decisions
                .Where(d => d.Decision == AgentSteering.DecisionKey)
                .TakeLast(AgentSteering.MaxPending)
                .Select(d => d.Reason)
                .ToList(),
            KnownRisks =
            [
                "Read-only tools may inspect local files under the selected workspace root.",
                "Writes (edit_file, create_file, apply_draft_patch) and commands (run_command, workspace recipes only) require approval; network access, installs, commits, pushes, and history rewrites remain blocked."
            ]
        };

        // The one-word goal heuristic is only useful before the model has
        // any transcript history of its own; from step 2 onward it can (and
        // should) drive search_files/glob_files itself, so spending pack
        // budget on this every step would be wasted context.
        if (state.StepCount == 0)
            AddWorkspaceContext(pack, options);
        await AddWorkspaceMemoryAsync(pack, options, ct);
        await AddRagContextAsync(pack, state, options, ct);
        await AddProjectInstructionsAsync(pack, options, ct);
        await AddProjectStateAsync(pack, state, ct);
        await AddTranscriptHistoryAsync(pack, state, ct);
        await AddLessonsAsync(pack, options, ct);
        AddSubTaskReports(pack, state);
        return pack;
    }

    private async Task AddProjectStateAsync(AgentContextPack pack, AgentTaskState task, CancellationToken ct)
    {
        if (_projectState is null || string.IsNullOrWhiteSpace(task.ProjectId)) return;
        try
        {
            var accepted = await _projectState.GetStateAsync(task.ProjectId, ct);
            var context = ProjectStateContextBuilder.Build(accepted);
            if (string.IsNullOrEmpty(context.Text)) return;
            pack.ProjectState.Add(new AgentRetrievedItem(
                "project-state",
                $"Project State revision {context.Revision}",
                context.Text,
                1.0,
                accepted.UpdatedAtUtc,
                $"project:{task.ProjectId}:state:{context.Revision}"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            pack.KnownRisks.Add($"Project State unavailable: {ex.Message}");
        }
    }

    /// <summary>
    /// One item per sub-task spec for an orchestration parent (r15
    /// 01-subtask-orchestration.md 1.6). No-op for every other task -
    /// <see cref="AgentTaskState.SubTaskPlan"/> is only ever populated on a
    /// parent by an approved plan_subtasks action.
    /// </summary>
    private static void AddSubTaskReports(AgentContextPack pack, AgentTaskState state)
    {
        if (state.SubTaskPlan.Count == 0) return;

        var candidates = state.SubTaskPlan
            .Select(spec => new ContextPart(
                "subtask",
                spec.Goal,
                $"[{spec.Status}, profile {spec.ProfileName}"
                    + (string.IsNullOrWhiteSpace(spec.ResolvedModelId) ? string.Empty : $", model {spec.ModelDisplayName} ({spec.ResolvedModelId})")
                    + $"] {spec.Goal}"
                    + (string.IsNullOrWhiteSpace(spec.ResultSummary) ? string.Empty : $" -> {spec.ResultSummary}"),
                Data: spec))
            .Reverse() // most recent children favored if the budget can't fit all of them
            .ToList();
        var packed = ContextPackBuilder.Pack(candidates, SubTaskReportsTokenBudget, maxParts: state.SubTaskPlan.Count);
        foreach (var part in packed.Parts.AsEnumerable().Reverse())
        {
            var spec = (AgentSubTaskSpec)part.Data!;
            pack.SubTaskReports.Add(new AgentRetrievedItem("subtask", spec.Goal, part.Content, 1.0, Locator: spec.TaskId));
        }
    }

    private async Task AddLessonsAsync(AgentContextPack pack, AgentWorkspaceOptions options, CancellationToken ct)
    {
        if (_lessons is null) return;

        try
        {
            var scopeId = string.IsNullOrWhiteSpace(options.WorkspaceRoot) ? null : Path.GetFullPath(options.WorkspaceRoot);
            var lessons = await _lessons.ListRelevantAsync(scopeId, includeRetired: false, limit: 50, ct);
            if (lessons.Count == 0) return;

            // Scope + confidence ordering alone lets an accumulated,
            // unrelated high-confidence lesson crowd out one that actually
            // shares terms with the current goal; rank by relevance before
            // packing (docs/review/02-lessons-v2.md L5).
            var queryTerms = AgentLessonText.Tokenize(pack.CurrentGoal)
                .Concat(pack.ToolResults.TakeLast(3).SelectMany(t => AgentLessonText.Tokenize(t.Tool)))
                .ToHashSet(StringComparer.Ordinal);

            var candidates = lessons
                .OrderByDescending(l => LessonRelevanceScore(l, queryTerms))
                .Select(l => new ContextPart(
                    "lesson",
                    l.Signature,
                    $"[{l.Outcome}, confidence {l.Confidence:F2}, seen {l.EvidenceCount}x] {l.Claim}"
                        + (string.IsNullOrWhiteSpace(l.Guidance) ? string.Empty : $" -> {l.Guidance}"),
                    Data: l))
                .ToList();
            var packed = ContextPackBuilder.Pack(candidates, LessonsTokenBudget, maxParts: options.MaxContextItems * 2);
            foreach (var part in packed.Parts)
            {
                var lesson = (AgentLesson)part.Data!;
                pack.Lessons.Add(new AgentRetrievedItem(
                    "lesson",
                    lesson.Id,
                    part.Content,
                    lesson.Confidence,
                    lesson.UpdatedAt,
                    Locator: lesson.Id));
            }
        }
        catch (Exception ex)
        {
            pack.KnownRisks.Add($"Lessons unavailable: {ex.Message}");
        }
    }

    /// <summary>Pinned lessons always sort first; otherwise shared terms with the current goal/recent tools outweigh raw confidence.</summary>
    private static double LessonRelevanceScore(AgentLesson lesson, HashSet<string> queryTerms)
    {
        var overlap = AgentLessonText.Tokenize($"{lesson.Claim} {lesson.Signature}").Count(queryTerms.Contains);
        return (lesson.IsPinned ? 10 : 0) + (overlap * 2) + lesson.Confidence;
    }

    private async Task AddTranscriptHistoryAsync(AgentContextPack pack, AgentTaskState state, CancellationToken ct)
    {
        try
        {
            var entries = await _taskStateStore.LoadTranscriptAsync(state.TaskId, ct);
            if (entries.Count == 0) return;

            var compacted = AgentTranscriptCompactor.Compact(entries);
            foreach (var diagnostic in compacted.Diagnostics)
                pack.TranscriptDiagnostics.Add(diagnostic.Describe());

            var budget = Math.Max(_settings.Settings.Agent.TranscriptTokenBudget, 512);
            // Pack from most-recent backward so the budget favors recency, then
            // restore chronological order for the model to read. Two entries
            // can share the same Step (an assistant thought and its tool
            // result), so the original read order is carried alongside the
            // entry to break ties deterministically instead of relying on
            // OrderBy(Step) alone, which would only preserve stability within
            // a single pass and not across the earlier Reverse().
            var candidates = compacted.Entries
                .Select((replay, index) => new ContextPart(
                    TranscriptSource(replay.Entry.Role),
                    replay.Entry.Role == "tool" ? $"step {replay.Entry.Step}: {replay.Entry.ToolName}" : $"step {replay.Entry.Step}",
                    replay.Entry.Content,
                    Data: (replay, index)))
                .Reverse()
                .ToList();
            var packed = ContextPackBuilder.Pack(candidates, budget, maxParts: compacted.Entries.Count);
            var ordered = packed.Parts
                .Select(p => (((AgentTranscriptReplayEntry Replay, int Index))p.Data!, p.Content))
                .OrderBy(t => t.Item1.Index)
                .Select(t => (t.Item1.Replay, t.Content));

            foreach (var (replay, content) in ordered)
            {
                var entry = replay.Entry;
                pack.TranscriptHistory.Add(new AgentRetrievedItem(
                    TranscriptSource(entry.Role),
                    replay.RepeatCount > 1
                        ? $"{entry.ToolName} (repeated {replay.RepeatCount} times)"
                        : entry.ToolName ?? $"step {entry.Step}",
                    content,
                    1.0,
                    entry.Timestamp,
                    Locator: $"step-{entry.Step}"));
            }
        }
        catch (Exception ex)
        {
            pack.KnownRisks.Add($"Transcript history unavailable: {ex.Message}");
        }
    }

    private static string TranscriptSource(string role) => role switch
    {
        "tool" => "transcript-tool",
        "user" => "transcript-user",
        _ => "transcript-assistant"
    };

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
                    .Where(r => !r.IsTruncationNotice)
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
