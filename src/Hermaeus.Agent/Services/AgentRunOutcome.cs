using Hermaeus.Agent.Models;

namespace Hermaeus.Agent.Services;

/// <summary>
/// What a finished run actually did, in four short lines composed entirely
/// from the run ledger and the task's own state (r26 03 3.2). It computes no
/// new facts: every number here is already in <see cref="AgentRunLedger"/> or
/// <see cref="AgentTaskState"/>. It is deliberately not the model's own
/// account of the run (that is the task summary, rendered separately) and it
/// carries no score, grade or percentage.
/// </summary>
public static class AgentRunOutcome
{
    public static AgentRunOutcomeSummary Describe(AgentRunLedger ledger, AgentTaskState state)
    {
        var terminal = state.Status is AgentTaskStatus.Complete or AgentTaskStatus.Failed or AgentTaskStatus.Blocked;
        if (!terminal)
            return AgentRunOutcomeSummary.None;

        var created = ledger.Files.Count(f => f.Kind == AgentLedgerFileKind.Created);
        var edited = ledger.Files.Count - created;
        var added = ledger.Files.Where(f => f.LineDelta > 0).Sum(f => f.LineDelta);
        var removed = ledger.Files.Where(f => f.LineDelta < 0).Sum(f => -f.LineDelta);

        var filesLine = ledger.Files.Count == 0
            ? "Changed no files."
            : $"Changed {Plural(ledger.Files.Count, "file")} ({edited} edited, {created} created), +{added} -{removed}.";

        var failedCommands = ledger.Commands.Count(c => c.TimedOut || (c.ExitCode is { } code && code != 0));
        var commandsLine = ledger.Commands.Count == 0
            ? "Ran no commands."
            : failedCommands == 0
                ? $"Ran {Plural(ledger.Commands.Count, "command")}, all succeeded."
                : $"Ran {Plural(ledger.Commands.Count, "command")}, {failedCommands} failed.";

        var approved = ledger.Approvals.Count(a => a.Approved);
        var approvalsLine = ledger.Approvals.Count == 0
            ? "Asked for no approvals."
            : $"Asked for {Plural(ledger.Approvals.Count, "approval")}: {approved} approved, {ledger.Approvals.Count - approved} rejected.";

        var changedNothing = ledger.Files.Count == 0 && ledger.Commands.Count == 0;
        var headline = changedNothing
            ? "This run changed no files and ran no commands."
            : failedCommands > 0
                ? "This run finished with a failed command."
                : "This run finished.";

        var unfinished = state.PendingSteps.Count > 0
            ? $"Finished with {Plural(state.PendingSteps.Count, "planned step")} not run."
            : string.Empty;

        return new AgentRunOutcomeSummary(
            HasOutcome: true,
            Headline: headline,
            FilesLine: filesLine,
            CommandsLine: commandsLine,
            ApprovalsLine: approvalsLine,
            UnfinishedPlanLine: unfinished,
            HasFailedCommand: failedCommands > 0,
            Reservations: [.. state.Reservations]);
    }

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? string.Empty : "s")}";
}

/// <summary>The rendered outcome. <see cref="None"/> is what a run that has not finished produces.</summary>
public sealed record AgentRunOutcomeSummary(
    bool HasOutcome,
    string Headline,
    string FilesLine,
    string CommandsLine,
    string ApprovalsLine,
    string UnfinishedPlanLine,
    bool HasFailedCommand,
    IReadOnlyList<string> Reservations)
{
    public static readonly AgentRunOutcomeSummary None =
        new(false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, false, []);

    public bool HasUnfinishedPlan => UnfinishedPlanLine.Length > 0;
    public bool HasReservations => Reservations.Count > 0;
}
