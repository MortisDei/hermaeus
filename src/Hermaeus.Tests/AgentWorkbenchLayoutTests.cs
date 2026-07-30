using System.Xml.Linq;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// The workbench is a fixed status line, a pinned decision strip, and four
/// tabs. Most of a view restructure is verified by review and by running the
/// app; what is asserted here is the part that can silently regress: the tab
/// the panel opens on, the badge rule, and the layout invariant that no
/// unbounded collection may sit in the non-scrolling header again
/// (docs/review/archived/r26 doc 02).
/// </summary>
public sealed class AgentWorkbenchLayoutTests
{
    private static string AgentViewPath()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return Path.Combine(root, "src", "Hermaeus.Desktop", "Views", "AgentView.axaml");
    }

    [Fact]
    public void The_header_holds_no_collection_that_could_grow_without_bound()
    {
        var doc = XDocument.Load(AgentViewPath());
        var grid = doc.Root!.Elements().Single(e => e.Name.LocalName == "Grid");

        // Row 0 is the status line and row 1 the decision strip; the strip's
        // own list is inside a ScrollViewer, so only row 0 is checked here.
        var statusLine = grid.Elements()
            .Single(e => (string?)e.Attribute("Grid.Row") == "0");

        var collections = statusLine.Descendants()
            .Where(e => e.Name.LocalName is "ItemsControl" or "ListBox" or "ItemsRepeater")
            .Select(e => e.Name.LocalName)
            .ToList();

        Assert.True(collections.Count == 0,
            "The status line must stay a fixed set of scalars; it grew a collection: " + string.Join(", ", collections));
    }

    [Fact]
    public void The_decision_strip_is_outside_the_tab_control()
    {
        var doc = XDocument.Load(AgentViewPath());
        var grid = doc.Root!.Elements().Single(e => e.Name.LocalName == "Grid");

        var tabControl = grid.Elements().Single(e => e.Name.LocalName == "TabControl");
        var strip = grid.Elements().Single(e => (string?)e.Attribute("Grid.Row") == "1");

        Assert.False(strip.Ancestors().Contains(tabControl),
            "The decision the agent is waiting on must never be behind a tab.");
        Assert.Equal(4, tabControl.Elements().Count(e => e.Name.LocalName == "TabItem"));
    }

    [Fact]
    public void Every_tab_scrolls_on_its_own()
    {
        var doc = XDocument.Load(AgentViewPath());
        var tabControl = doc.Descendants().Single(e => e.Name.LocalName == "TabControl");

        foreach (var tab in tabControl.Elements().Where(e => e.Name.LocalName == "TabItem"))
        {
            var content = tab.Elements().Where(e => e.Name.LocalName != "TabItem.Header").ToList();
            Assert.All(content, element => Assert.Equal("ScrollViewer", element.Name.LocalName));
        }
    }

    [Fact]
    public void The_panel_opens_on_the_run_tab()
    {
        Assert.Equal(0, AgentViewModel.RunTabIndex);
    }

    private static AgentTaskState Task(AgentTaskStatus status) =>
        new() { TaskId = "t", Goal = "goal", Status = status };

    private static AgentRunLedger Ledger(
        IReadOnlyList<AgentLedgerFileEntry>? files = null,
        IReadOnlyList<AgentLedgerCommandEntry>? commands = null,
        IReadOnlyList<AgentLedgerApprovalEntry>? approvals = null) =>
        new(files ?? [], commands ?? [], approvals ?? [], []);

    private static AgentLedgerFileEntry File(string path, AgentLedgerFileKind kind, int lineDelta) =>
        new(path, kind, 1, AgentLedgerFileStatus.Applied, lineDelta, null, string.Empty, "t");

    [Fact]
    public void A_running_task_produces_no_outcome_block()
    {
        var outcome = AgentRunOutcome.Describe(Ledger(), Task(AgentTaskStatus.Running));

        Assert.False(outcome.HasOutcome);
        Assert.Equal(AgentRunOutcomeSummary.None, outcome);
    }

    [Fact]
    public void A_waiting_task_produces_no_outcome_block()
    {
        Assert.False(AgentRunOutcome.Describe(Ledger(), Task(AgentTaskStatus.WaitingForUser)).HasOutcome);
    }

    [Fact]
    public void A_mixed_run_counts_created_edited_and_the_line_delta()
    {
        var outcome = AgentRunOutcome.Describe(
            Ledger(files:
            [
                File("a.cs", AgentLedgerFileKind.Edited, 40),
                File("b.cs", AgentLedgerFileKind.Edited, 41),
                File("c.cs", AgentLedgerFileKind.Created, -12)
            ]),
            Task(AgentTaskStatus.Complete));

        Assert.True(outcome.HasOutcome);
        Assert.Equal("Changed 3 files (2 edited, 1 created), +81 -12.", outcome.FilesLine);
    }

    [Fact]
    public void A_run_that_changed_nothing_says_so_plainly()
    {
        var outcome = AgentRunOutcome.Describe(Ledger(), Task(AgentTaskStatus.Complete));

        Assert.Equal("This run changed no files and ran no commands.", outcome.Headline);
        Assert.Equal("Changed no files.", outcome.FilesLine);
        Assert.Equal("Ran no commands.", outcome.CommandsLine);
        Assert.Equal("Asked for no approvals.", outcome.ApprovalsLine);
    }

    [Fact]
    public void A_failed_command_is_reported_even_when_the_task_completed()
    {
        var outcome = AgentRunOutcome.Describe(
            Ledger(commands:
            [
                new AgentLedgerCommandEntry("dotnet build", 0, false, DateTime.UtcNow, "t"),
                new AgentLedgerCommandEntry("dotnet test", 1, false, DateTime.UtcNow, "t")
            ]),
            Task(AgentTaskStatus.Complete));

        Assert.True(outcome.HasFailedCommand);
        Assert.Equal("Ran 2 commands, 1 failed.", outcome.CommandsLine);
        Assert.Contains("failed command", outcome.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void A_timed_out_command_counts_as_a_failure()
    {
        var outcome = AgentRunOutcome.Describe(
            Ledger(commands: [new AgentLedgerCommandEntry("dotnet test", null, true, DateTime.UtcNow, "t")]),
            Task(AgentTaskStatus.Complete));

        Assert.True(outcome.HasFailedCommand);
        Assert.Equal("Ran 1 command, 1 failed.", outcome.CommandsLine);
    }

    [Fact]
    public void Approvals_are_split_into_approved_and_rejected()
    {
        var outcome = AgentRunOutcome.Describe(
            Ledger(approvals:
            [
                new AgentLedgerApprovalEntry("run_command", true, DateTime.UtcNow, "t"),
                new AgentLedgerApprovalEntry("edit_file", false, DateTime.UtcNow, "t")
            ]),
            Task(AgentTaskStatus.Complete));

        Assert.Equal("Asked for 2 approvals: 1 approved, 1 rejected.", outcome.ApprovalsLine);
    }

    [Fact]
    public void A_completed_task_with_pending_plan_steps_surfaces_the_unfinished_plan_line()
    {
        var state = Task(AgentTaskStatus.Complete);
        state.PendingSteps.Add("write the regression test");
        state.PendingSteps.Add("update the docs");

        var outcome = AgentRunOutcome.Describe(Ledger(), state);

        Assert.True(outcome.HasUnfinishedPlan);
        Assert.Equal("Finished with 2 planned steps not run.", outcome.UnfinishedPlanLine);
    }

    [Fact]
    public void Reservations_are_carried_into_the_outcome_when_present_and_absent()
    {
        var withNone = AgentRunOutcome.Describe(Ledger(), Task(AgentTaskStatus.Complete));
        Assert.False(withNone.HasReservations);

        var state = Task(AgentTaskStatus.Complete);
        state.Reservations.Add("Could not verify the integration path.");
        var withSome = AgentRunOutcome.Describe(Ledger(), state);

        Assert.True(withSome.HasReservations);
        Assert.Equal("Could not verify the integration path.", Assert.Single(withSome.Reservations));
    }

    [Fact]
    public void A_failed_task_still_reports_what_it_did()
    {
        var outcome = AgentRunOutcome.Describe(
            Ledger(files: [File("a.cs", AgentLedgerFileKind.Created, 10)]),
            Task(AgentTaskStatus.Failed));

        Assert.True(outcome.HasOutcome);
        Assert.Equal("Changed 1 file (0 edited, 1 created), +10 -0.", outcome.FilesLine);
    }
}
