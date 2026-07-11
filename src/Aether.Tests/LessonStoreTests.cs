using Aether.Agent.Models;
using Aether.Agent.Services;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

public sealed class LessonStoreTests
{
    private static SqliteLessonStore NewStore(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        return new SqliteLessonStore(settings);
    }

    private static AgentLessonEvidence CommandEvidence(AgentLessonOutcome outcome, string taskId = "task-1") => new(
        AgentLessonScope.Workspace, "C:/ws", AgentLessonKind.Command,
        "command:dotnet test:fail:CS0246",
        "dotnet test fails with CS0246.",
        "Check the missing using directive.",
        outcome, taskId);

    [Fact]
    public async Task Recording_new_evidence_creates_a_lesson()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();

        var lesson = await store.RecordEvidenceAsync(CommandEvidence(AgentLessonOutcome.Failed));

        Assert.Equal(1, lesson.EvidenceCount);
        Assert.Equal(AgentLessonStatus.Active, lesson.Status);
        Assert.True(lesson.Confidence is > 0 and < 1);
    }

    [Fact]
    public async Task Repeated_matching_evidence_reinforces_instead_of_duplicating()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();

        var first = await store.RecordEvidenceAsync(CommandEvidence(AgentLessonOutcome.Failed, "task-1"));
        var second = await store.RecordEvidenceAsync(CommandEvidence(AgentLessonOutcome.Failed, "task-2"));
        var third = await store.RecordEvidenceAsync(CommandEvidence(AgentLessonOutcome.Failed, "task-3"));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Id, third.Id);
        Assert.Equal(3, third.EvidenceCount);
        Assert.True(third.Confidence > first.Confidence, "confidence should rise with repeated confirming evidence");

        var all = await store.ListAllAsync(includeRetired: true);
        Assert.Single(all);
    }

    [Fact]
    public async Task Contradicting_evidence_decays_confidence_and_eventually_retires()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();

        var failing = await store.RecordEvidenceAsync(CommandEvidence(AgentLessonOutcome.Failed));
        for (var i = 0; i < 3; i++)
            failing = await store.RecordEvidenceAsync(CommandEvidence(AgentLessonOutcome.Failed));
        Assert.True(failing.Confidence > 0.5, "several confirming runs should build meaningful confidence");

        // The command starts succeeding: contradicting evidence for the same signature.
        var contradicted = await store.RecordEvidenceAsync(CommandEvidence(AgentLessonOutcome.Worked));
        Assert.True(contradicted.Confidence < failing.Confidence, "a contradiction should reduce confidence");

        var retired = await store.RecordEvidenceAsync(CommandEvidence(AgentLessonOutcome.Worked));
        Assert.Equal(AgentLessonStatus.Retired, retired.Status);

        var activeOnly = await store.ListAllAsync(includeRetired: false);
        Assert.Empty(activeOnly);
        var withRetired = await store.ListAllAsync(includeRetired: true);
        Assert.Single(withRetired);
    }

    [Fact]
    public async Task Reviving_evidence_after_retirement_reactivates_the_lesson()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();

        for (var i = 0; i < 3; i++)
            await store.RecordEvidenceAsync(CommandEvidence(AgentLessonOutcome.Failed));
        var retired = await store.RecordEvidenceAsync(CommandEvidence(AgentLessonOutcome.Worked));
        retired = await store.RecordEvidenceAsync(CommandEvidence(AgentLessonOutcome.Worked));
        Assert.Equal(AgentLessonStatus.Retired, retired.Status);

        // The failure recurs: matches the now-dominant "Worked" outcome? No -
        // it's a Failed outcome again, i.e. a contradiction of the retired
        // "Worked" state, which should not revive it (still a contradiction).
        // A same-outcome confirmation of "Worked" should revive it instead.
        var revived = await store.RecordEvidenceAsync(CommandEvidence(AgentLessonOutcome.Worked));
        Assert.Equal(AgentLessonStatus.Active, revived.Status);
    }

    [Fact]
    public async Task Pinned_lessons_ignore_new_evidence()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();

        var lesson = await store.RecordEvidenceAsync(CommandEvidence(AgentLessonOutcome.Failed));
        await store.SetPinnedAsync(lesson.Id, true);
        var beforeConfidence = (await store.GetByIdAsync(lesson.Id))!.Confidence;

        await store.RecordEvidenceAsync(CommandEvidence(AgentLessonOutcome.Worked));
        await store.RecordEvidenceAsync(CommandEvidence(AgentLessonOutcome.Failed));

        var after = await store.GetByIdAsync(lesson.Id);
        Assert.Equal(beforeConfidence, after!.Confidence);
        Assert.Equal(1, after.EvidenceCount);
    }

    [Fact]
    public async Task ListRelevantAsync_scopes_to_global_and_the_given_workspace()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();

        await store.RecordEvidenceAsync(new AgentLessonEvidence(
            AgentLessonScope.Global, "", AgentLessonKind.Approval, "approval:run_command:rejected",
            "User rejects run_command in general.", "", AgentLessonOutcome.UserRejected));
        await store.RecordEvidenceAsync(new AgentLessonEvidence(
            AgentLessonScope.Workspace, "C:/ws-a", AgentLessonKind.Command, "command:dotnet build:ok:generic",
            "dotnet build works in ws-a.", "", AgentLessonOutcome.Worked));
        await store.RecordEvidenceAsync(new AgentLessonEvidence(
            AgentLessonScope.Workspace, "C:/ws-b", AgentLessonKind.Command, "command:dotnet build:ok:generic",
            "dotnet build works in ws-b.", "", AgentLessonOutcome.Worked));

        var forWsA = await store.ListRelevantAsync("C:/ws-a", includeRetired: false, limit: 50);
        Assert.Equal(2, forWsA.Count);
        Assert.Contains(forWsA, l => l.ScopeId == "C:/ws-a");
        Assert.Contains(forWsA, l => l.Scope == AgentLessonScope.Global);
        Assert.DoesNotContain(forWsA, l => l.ScopeId == "C:/ws-b");

        var globalOnly = await store.ListRelevantAsync(null, includeRetired: false, limit: 50);
        Assert.Single(globalOnly);
    }

    [Fact]
    public async Task Manual_update_delete_and_status_change_work()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();

        var lesson = await store.RecordEvidenceAsync(CommandEvidence(AgentLessonOutcome.Failed));
        await store.UpdateAsync(lesson.Id, "Corrected claim.", "Corrected guidance.");
        var updated = await store.GetByIdAsync(lesson.Id);
        Assert.Equal("Corrected claim.", updated!.Claim);

        await store.SetStatusAsync(lesson.Id, AgentLessonStatus.Retired);
        var retired = await store.GetByIdAsync(lesson.Id);
        Assert.Equal(AgentLessonStatus.Retired, retired!.Status);

        await store.DeleteAsync(lesson.Id);
        Assert.Null(await store.GetByIdAsync(lesson.Id));
    }
}
