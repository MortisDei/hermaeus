using Hermaeus.Agent.Models;

namespace Hermaeus.Agent.Services;

/// <summary>
/// Builds a run's total footprint (r23 1.1, doc "01-run-ledger-and-task-rewind.md"):
/// every file it changed, every command it ran, every approval decision made
/// along the way. A pure function over already-loaded <see cref="AgentTaskState"/>,
/// in the same spirit as <see cref="AgentContextReceiptBuilder"/> - no new
/// persistence, no filesystem access. For an orchestration parent, pass the
/// children's loaded states too; their entries fold into the same sections,
/// each tagged with the child's own task id.
/// </summary>
public static class AgentRunLedgerBuilder
{
    public static AgentRunLedger Build(AgentTaskState task, IReadOnlyList<AgentTaskState>? childTasks = null)
    {
        var tasks = new List<AgentTaskState> { task };
        if (childTasks is { Count: > 0 })
            tasks.AddRange(childTasks);

        var files = new List<AgentLedgerFileEntry>();
        var commands = new List<AgentLedgerCommandEntry>();
        var approvals = new List<AgentLedgerApprovalEntry>();
        foreach (var t in tasks)
        {
            files.AddRange(BuildFiles(t));
            commands.AddRange(BuildCommands(t));
            approvals.AddRange(BuildApprovals(t));
        }

        var subTasks = task.SubTaskPlan
            .Select(s => new AgentLedgerSubTaskEntry(s.Goal, s.Status, s.TaskId))
            .ToList();

        return new AgentRunLedger(files, commands, approvals, subTasks);
    }

    private static IEnumerable<AgentLedgerFileEntry> BuildFiles(AgentTaskState task)
    {
        // GroupBy preserves first-occurrence order of each key from the
        // source sequence, and DraftPatches is append-only in chronological
        // order, so this is already "ordered by first touch" with no extra sort.
        var groups = task.DraftPatches
            .Where(p => p.Status is AgentDraftPatchStatus.Applied or AgentDraftPatchStatus.Reverted)
            .GroupBy(p => p.RelativePath);

        foreach (var group in groups)
        {
            var patches = group.ToList();
            var first = patches[0];
            var latest = patches[^1];

            // Rewind (1.3) flips every patch for a path to Reverted together,
            // so a mixed state only arises from an earlier per-patch (not
            // whole-run) revert; treat that conservatively as still Applied
            // rather than implying the file has no outstanding change.
            var status = patches.All(p => p.Status == AgentDraftPatchStatus.Reverted)
                ? AgentLedgerFileStatus.Reverted
                : AgentLedgerFileStatus.Applied;

            yield return new AgentLedgerFileEntry(
                RelativePath: group.Key,
                Kind: first.PreImageExisted ? AgentLedgerFileKind.Edited : AgentLedgerFileKind.Created,
                AppliedPatchCount: patches.Count(p => p.Status == AgentDraftPatchStatus.Applied),
                Status: status,
                LineDelta: CountLines(latest.AppliedContent) - CountLines(first.PreImageContent),
                LatestAppliedContent: latest.AppliedContent,
                TaskId: task.TaskId);
        }
    }

    private static IEnumerable<AgentLedgerCommandEntry> BuildCommands(AgentTaskState task) =>
        task.ToolResults
            .Where(r => string.Equals(r.Tool, "run_command", StringComparison.OrdinalIgnoreCase))
            .Select(r => new AgentLedgerCommandEntry(
                AgentToolExecutor.Arg(r.Arguments, "command"),
                r.ExitCode,
                r.TimedOut,
                r.Timestamp,
                task.TaskId));

    private static IEnumerable<AgentLedgerApprovalEntry> BuildApprovals(AgentTaskState task) =>
        task.ApprovalHistory.Select(a => new AgentLedgerApprovalEntry(a.Action, a.Approved, a.Timestamp, task.TaskId));

    private static int CountLines(string? content) =>
        string.IsNullOrEmpty(content) ? 0 : content.Split('\n').Length;
}
