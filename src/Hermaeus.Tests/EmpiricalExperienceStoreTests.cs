using System.Text.Json;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Hermaeus.Tests;

public sealed class EmpiricalExperienceStoreTests
{
    private static SqliteEmpiricalExperienceStore NewStore(TempDir temp, out SettingsService settings)
    {
        settings = Helpers.NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data-a");
        return new SqliteEmpiricalExperienceStore(settings, new RedactionService());
    }

    private static EmpiricalExperienceDraft Draft(
        string domain = EmpiricalExperienceDomains.AgentToolOutcome,
        NormalizedOutcome outcome = NormalizedOutcome.Succeeded,
        EvidenceOrigin origin = EvidenceOrigin.DirectObservation) => new()
    {
        Domain = domain,
        ProjectId = "project-1",
        WorkspaceFingerprint = "workspace-fingerprint-1",
        ContextJson = "{\"z\":2,\"a\":1}",
        ActionJson = "{\"tool\":\"read_file\"}",
        Outcome = NormalizedToolOutcome.Create(outcome, "test-evidence", "bounded detail", new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc)),
        Provenance =
        [
            new EmpiricalExperienceProvenance("task:one:step:1", new SourceReference(
                ProvenanceKind.AgentTool, "task tool result", "task-one", "raw evidence", EvidenceOrigin: origin))
        ],
        RuntimeFingerprint = "runtime-1",
        ModelFingerprint = "model-1"
    };

    [Theory]
    [InlineData(EmpiricalExperienceDomains.AgentToolOutcome)]
    [InlineData(EmpiricalExperienceDomains.GpuFitObservation)]
    [InlineData(EmpiricalExperienceDomains.LabRun)]
    public async Task Every_initial_domain_round_trips(string domain)
    {
        using var temp = new TempDir();
        var store = NewStore(temp, out _);
        var saved = await store.AddAsync(Draft(domain));
        var reloaded = await store.GetAsync(saved.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(domain, reloaded!.Domain);
        Assert.Equal("{\"a\":1,\"z\":2}", reloaded.ContextJson);
        Assert.Equal(NormalizedOutcome.Succeeded, reloaded.Outcome.Outcome);
        Assert.Single(reloaded.Provenance);
    }

    [Fact]
    public async Task Batch_add_rolls_back_the_entire_set_on_insert_failure()
    {
        using var temp = new TempDir();
        var store = NewStore(temp, out _);
        var first = Draft() with { Id = "batch:one" };
        var duplicate = Draft() with { Id = "batch:one" };

        await Assert.ThrowsAsync<SqliteException>(() => store.AddBatchAsync([first, duplicate]));

        Assert.Null(await store.GetAsync("batch:one"));
    }

    [Fact]
    public void Typed_codecs_round_trip_only_their_domain_shapes()
    {
        var agent = new AgentToolExperienceCodec();
        var context = new AgentToolExperienceContext("task-1", 4, 7, "run_command");
        var action = new AgentToolExperienceAction("{\"command\":\"dotnet test\"}", 1);

        Assert.Equal(EmpiricalExperienceDomains.AgentToolOutcome, agent.Domain);
        Assert.Equal(context, agent.DecodeContext(agent.EncodeContext(context)));
        Assert.Equal(action, agent.DecodeAction(agent.EncodeAction(action)));
        Assert.Equal(EmpiricalExperienceDomains.GpuFitObservation, new GpuFitExperienceCodec().Domain);
        Assert.Equal(EmpiricalExperienceDomains.LabRun, new LabRunExperienceCodec().Domain);
    }

    [Fact]
    public void Canonical_hash_is_property_order_independent()
    {
        var first = ExperienceJson.CanonicalizeJson("{\"z\":2,\"a\":1}");
        var second = ExperienceJson.CanonicalizeJson("{\"a\":1,\"z\":2}");
        Assert.Equal(first, second);
        Assert.Equal(ExperienceJson.Hash(first), ExperienceJson.Hash(second));
    }

    [Fact]
    public async Task Query_filters_domain_outcome_origin_scope_and_exact_hashes()
    {
        using var temp = new TempDir();
        var store = NewStore(temp, out _);
        var wanted = await store.AddAsync(Draft(origin: EvidenceOrigin.Extracted));
        await store.AddAsync(Draft(EmpiricalExperienceDomains.LabRun, NormalizedOutcome.Failed));

        var rows = await store.QueryAsync(new EmpiricalExperienceQuery
        {
            Domain = wanted.Domain, ProjectId = wanted.ProjectId, WorkspaceFingerprint = wanted.WorkspaceFingerprint,
            RuntimeFingerprint = wanted.RuntimeFingerprint, ModelFingerprint = wanted.ModelFingerprint,
            ContextHash = wanted.ContextHash, ActionHash = wanted.ActionHash, Outcome = NormalizedOutcome.Succeeded,
            Origin = EvidenceOrigin.Extracted, Status = EmpiricalExperienceStatus.Current,
            CreatedFromUtc = wanted.CreatedAtUtc.AddSeconds(-1), CreatedToUtc = wanted.CreatedAtUtc.AddSeconds(1)
        });

        Assert.Single(rows);
        Assert.Equal(wanted.Id, rows[0].Id);
    }

    [Fact]
    public async Task Correction_inserts_replacement_and_supersedes_prior_atomically()
    {
        using var temp = new TempDir();
        var store = NewStore(temp, out _);
        var prior = await store.AddAsync(Draft(outcome: NormalizedOutcome.Unknown));
        var replacement = await store.CorrectAsync(prior.Id, Draft(outcome: NormalizedOutcome.Succeeded));

        Assert.Equal(prior.Id, replacement.CorrectsExperienceId);
        Assert.Equal(EmpiricalExperienceStatus.Superseded, (await store.GetAsync(prior.Id))!.Status);
        Assert.Equal(EmpiricalExperienceStatus.Current, (await store.GetAsync(replacement.Id))!.Status);
    }

    [Fact]
    public async Task Failed_correction_leaves_prior_current_and_adds_nothing()
    {
        using var temp = new TempDir();
        var store = NewStore(temp, out _);
        var prior = await store.AddAsync(Draft());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CorrectAsync(prior.Id, Draft(EmpiricalExperienceDomains.LabRun)));

        Assert.Equal(EmpiricalExperienceStatus.Current, (await store.GetAsync(prior.Id))!.Status);
        Assert.Single(await store.QueryAsync(new EmpiricalExperienceQuery { Limit = 20 }));
    }

    [Fact]
    public async Task Removal_is_hard_delete_and_cascades_provenance()
    {
        using var temp = new TempDir();
        var store = NewStore(temp, out var settings);
        var saved = await store.AddAsync(Draft());
        await store.RemoveAsync(saved.Id);

        Assert.Null(await store.GetAsync(saved.Id));
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(SettingsService.ResolveDataRoot(settings.Settings), "experience.db")}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM experience_provenance WHERE experience_id=$id";
        command.Parameters.AddWithValue("$id", saved.Id);
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Removal_refuses_while_a_correction_depends_on_target()
    {
        using var temp = new TempDir();
        var store = NewStore(temp, out _);
        var prior = await store.AddAsync(Draft());
        await store.CorrectAsync(prior.Id, Draft());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.RemoveAsync(prior.Id));
        Assert.Contains("dependent", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await store.GetAsync(prior.Id));
    }

    [Fact]
    public async Task Export_is_versioned_and_redacts_secrets_and_home_paths()
    {
        using var temp = new TempDir();
        var store = NewStore(temp, out _);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var draft = Draft() with
        {
            ContextJson = JsonSerializer.Serialize(new { api_key = "sk-abcdefghijklmnop", path = Path.Combine(home, "private.txt") }),
            Provenance = [new EmpiricalExperienceProvenance("source-1", new SourceReference(ProvenanceKind.AgentTool, Path.Combine(home, "private.txt")))]
        };
        var saved = await store.AddAsync(draft);
        var json = await store.ExportAsync([saved.Id]);

        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-abcdefghijklmnop", json, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(home)) Assert.DoesNotContain(home, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Store_reinitializes_after_data_root_switch()
    {
        using var temp = new TempDir();
        var store = NewStore(temp, out var settings);
        var first = await store.AddAsync(Draft());
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data-b");
        var second = await store.AddAsync(Draft(EmpiricalExperienceDomains.LabRun));

        Assert.Null(await store.GetAsync(first.Id));
        Assert.NotNull(await store.GetAsync(second.Id));
        Assert.True(File.Exists(temp.PathFor("data-a/experience.db")));
        Assert.True(File.Exists(temp.PathFor("data-b/experience.db")));
    }

    [Fact]
    public async Task Invalid_payload_is_rejected_before_any_transaction()
    {
        using var temp = new TempDir();
        var store = NewStore(temp, out _);
        await Assert.ThrowsAnyAsync<JsonException>(() => store.AddAsync(Draft() with { ContextJson = "not json" }));
        Assert.Empty(await store.QueryAsync(new EmpiricalExperienceQuery()));
    }

    [Theory]
    [InlineData("C:\\Users\\Someone\\workspace")]
    [InlineData("/home/someone/workspace")]
    [InlineData("opaque/except-not-really")]
    public async Task Workspace_fingerprint_refuses_path_like_values(string fingerprint)
    {
        using var temp = new TempDir();
        var store = NewStore(temp, out _);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(Draft() with { WorkspaceFingerprint = fingerprint }));
    }

    [Fact]
    public async Task Provenance_and_document_bounds_are_enforced()
    {
        using var temp = new TempDir();
        var store = NewStore(temp, out _);
        var tooMany = Enumerable.Range(0, 17)
            .Select(i => new EmpiricalExperienceProvenance($"source-{i}", new SourceReference(ProvenanceKind.Lab, "source")))
            .ToArray();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(Draft() with { Provenance = tooMany }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(Draft() with { ContextJson = JsonSerializer.Serialize(new { text = new string('x', ExperienceJson.MaxDocumentBytes) }) }));
    }

    [Fact]
    public async Task Preexisting_unrelated_database_content_survives_additive_migration()
    {
        using var temp = new TempDir();
        var store = NewStore(temp, out var settings);
        var path = Path.Combine(SettingsService.ResolveDataRoot(settings.Settings), "experience.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE prior_content (value TEXT); INSERT INTO prior_content VALUES ('keep');";
            await command.ExecuteNonQueryAsync();
        }

        await store.InitializeAsync();
        await using var verify = new SqliteConnection($"Data Source={path}"); await verify.OpenAsync();
        var check = verify.CreateCommand(); check.CommandText = "SELECT value FROM prior_content";
        Assert.Equal("keep", (string?)await check.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Data_root_manifest_includes_experience_database()
    {
        using var temp = new TempDir();
        var store = NewStore(temp, out var settings);
        await store.AddAsync(Draft());
        var root = SettingsService.ResolveDataRoot(settings.Settings);
        Assert.Contains(DataRootManifest.EnumerateAll(root), file => file.RelativePath == "experience.db");
    }

    [Fact]
    public async Task Agent_indexes_new_gate_results_without_storing_raw_output_as_authority()
    {
        using var temp = new TempDir();
        var store = NewStore(temp, out var settings);
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var taskStore = new FileAgentTaskStateStore(settings);
        var tools = new AgentWorkspaceTools();
        var agent = new AgentService(
            taskStore, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), new FakeAgentLlm(),
            settings: settings, workspaceTools: tools, experiences: store);
        var options = new AgentWorkspaceOptions(workspace, ModelId: "local-model-id");
        var task = await agent.CreateTaskAsync("index this step", options, projectId: "project-1");

        await agent.RunStepAsync(task.TaskId, options);
        var rows = await store.QueryAsync(new EmpiricalExperienceQuery { Domain = EmpiricalExperienceDomains.AgentToolOutcome });

        var gate = Assert.Single(rows);
        Assert.Equal(NormalizedOutcome.Blocked, gate.Outcome.Outcome);
        Assert.DoesNotContain(workspace, gate.WorkspaceFingerprint ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(task.TaskId, gate.ContextJson, StringComparison.Ordinal);
        Assert.Contains("safety_gate", gate.ContextJson, StringComparison.Ordinal);
    }
}
