using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

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

    [Fact]
    public async Task CounterOnly_evidence_does_not_originate_a_new_lesson()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();

        var lesson = await store.RecordEvidenceAsync(new AgentLessonEvidence(
            AgentLessonScope.Workspace, "C:/ws", AgentLessonKind.Approval, "approval:edit_file",
            "The user approves edit_file requests in this context.", "", AgentLessonOutcome.Worked, CounterOnly: true));

        Assert.Equal(0, lesson.EvidenceCount);
        var all = await store.ListAllAsync(includeRetired: true);
        Assert.Empty(all);
    }

    [Fact]
    public async Task CounterOnly_evidence_contradicts_an_existing_lesson()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();

        // Reinforce first so confidence is well above the retirement floor;
        // a single piece of evidence starts too close to it for one
        // contradiction to show a meaningful drop instead of an immediate
        // reset back to the same initial confidence.
        var rejectionEvidence = new AgentLessonEvidence(
            AgentLessonScope.Workspace, "C:/ws", AgentLessonKind.Approval, "approval:edit_file",
            "The user rejects edit_file requests in this context.", "", AgentLessonOutcome.UserRejected);
        AgentLesson rejected = await store.RecordEvidenceAsync(rejectionEvidence);
        for (var i = 0; i < 3; i++)
            rejected = await store.RecordEvidenceAsync(rejectionEvidence);
        Assert.True(rejected.Confidence > 0.5, "several confirming rejections should build meaningful confidence");

        var countered = await store.RecordEvidenceAsync(new AgentLessonEvidence(
            AgentLessonScope.Workspace, "C:/ws", AgentLessonKind.Approval, "approval:edit_file",
            "The user approves edit_file requests in this context.", "", AgentLessonOutcome.Worked, CounterOnly: true));

        Assert.True(countered.Confidence < rejected.Confidence, "counter-evidence against an existing lesson should weaken it like any other contradiction");
    }

    [Fact]
    public async Task ConfirmAsync_bumps_evidence_on_active_unpinned_lessons_only()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.InitializeAsync();

        var active = await store.RecordEvidenceAsync(CommandEvidence(AgentLessonOutcome.Worked, "task-1"));
        var pinned = await store.RecordEvidenceAsync(new AgentLessonEvidence(
            AgentLessonScope.Workspace, "C:/ws", AgentLessonKind.Stated, "stated:pinned",
            "Pinned claim.", "", AgentLessonOutcome.Observation));
        await store.SetPinnedAsync(pinned.Id, true);

        await store.ConfirmAsync([active.Id, pinned.Id, "unknown-id"], "task-confirm");

        var confirmedActive = await store.GetByIdAsync(active.Id);
        Assert.Equal(2, confirmedActive!.EvidenceCount);
        var confirmedPinned = await store.GetByIdAsync(pinned.Id);
        Assert.Equal(1, confirmedPinned!.EvidenceCount);
    }

    [Fact]
    public async Task Migration_to_v2_collapses_outcome_suffixed_signatures_and_strips_the_suffix()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var dataRoot = temp.PathFor("data");
        settings.Settings.DataManagement.DataRootDirectory = dataRoot;
        var agentRoot = Path.Combine(dataRoot, "agent");
        Directory.CreateDirectory(agentRoot);
        var dbPath = Path.Combine(agentRoot, "lessons.db");

        await using (var c = new SqliteConnection($"Data Source={dbPath}"))
        {
            await c.OpenAsync();
            await using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE agent_lessons (
                        id TEXT PRIMARY KEY,
                        scope TEXT NOT NULL,
                        scope_id TEXT NOT NULL DEFAULT '',
                        kind TEXT NOT NULL,
                        signature TEXT NOT NULL,
                        claim TEXT NOT NULL,
                        guidance TEXT NOT NULL,
                        outcome TEXT NOT NULL,
                        confidence REAL NOT NULL DEFAULT 0.3,
                        evidence_count INTEGER NOT NULL DEFAULT 1,
                        created_at TEXT NOT NULL,
                        updated_at TEXT NOT NULL,
                        last_confirmed_at TEXT NOT NULL,
                        status TEXT NOT NULL DEFAULT 'Active',
                        is_pinned INTEGER NOT NULL DEFAULT 0,
                        source_task_ids_json TEXT NOT NULL DEFAULT '[]'
                    );
                    CREATE UNIQUE INDEX idx_agent_lessons_dedupe ON agent_lessons(scope, scope_id, signature);
                    CREATE TABLE aether_schema_versions (
                        scope TEXT PRIMARY KEY,
                        version INTEGER NOT NULL,
                        updated_at TEXT NOT NULL
                    );
                    INSERT INTO aether_schema_versions (scope, version, updated_at) VALUES ('agent_lessons', 1, '2026-01-01T00:00:00Z');";
                await cmd.ExecuteNonQueryAsync();
            }

            InsertV1Row(c, "row-ok", "command:dotnet test:ok:generic", "Worked", 2, "2026-01-01T00:00:00Z");
            InsertV1Row(c, "row-fail", "command:dotnet test:fail:CS1002", "Failed", 5, "2026-01-02T00:00:00Z");
        }

        var store = new SqliteLessonStore(settings);
        await store.InitializeAsync();

        var all = await store.ListAllAsync(includeRetired: true);
        var survivors = all.Where(l => l.Signature.StartsWith("command:dotnet test", StringComparison.Ordinal)).ToList();
        Assert.Single(survivors);
        Assert.Equal("command:dotnet test", survivors[0].Signature);
        Assert.Equal(5, survivors[0].EvidenceCount);
        Assert.Equal("row-fail", survivors[0].Id);
    }

    private static void InsertV1Row(SqliteConnection c, string id, string signature, string outcome, int evidenceCount, string updatedAt)
    {
        using var insert = c.CreateCommand();
        insert.CommandText = @"
            INSERT INTO agent_lessons (id, scope, scope_id, kind, signature, claim, guidance, outcome, confidence, evidence_count, created_at, updated_at, last_confirmed_at, status, is_pinned, source_task_ids_json)
            VALUES ($id, 'Workspace', 'C:/ws', 'Command', $sig, 'claim', '', $outcome, 0.5, $count, $updatedAt, $updatedAt, $updatedAt, 'Active', 0, '[]')";
        insert.Parameters.AddWithValue("$id", id);
        insert.Parameters.AddWithValue("$sig", signature);
        insert.Parameters.AddWithValue("$outcome", outcome);
        insert.Parameters.AddWithValue("$count", evidenceCount);
        insert.Parameters.AddWithValue("$updatedAt", updatedAt);
        insert.ExecuteNonQuery();
    }
}
