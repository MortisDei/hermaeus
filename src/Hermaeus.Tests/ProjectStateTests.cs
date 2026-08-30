using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Rag;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Microsoft.Data.Sqlite;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class ProjectStateTests
{
    private static (ProjectStore Store, Project Project) NewStore(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new ProjectStore(settings);
        return (store, new Project { Id = "project-1", Name = "Project" });
    }

    private static ProjectState State(string projectId, long revision = 0) => new()
    {
        ProjectId = projectId,
        Revision = revision,
        CurrentObjective = "Ship R31",
        Milestone = "Batch 9",
        Status = "Active",
        Items =
        [
            new ProjectStateItem
            {
                Id = "decision-1", Kind = ProjectStateItemKind.AcceptedDecision,
                Text = "Keep state explicit", ArtifactLocator = "docs/projects.md", Order = 0,
                Origin = EvidenceOrigin.UserProvided,
                Source = new SourceReference(ProvenanceKind.Workspace, "Project plan", "docs/projects.md")
            }
        ]
    };

    [Fact]
    public async Task State_round_trips_every_field_and_provenance()
    {
        using var temp = new TempDir(); var (store, project) = NewStore(temp);
        await store.SaveAsync(project);
        var saved = await store.SaveStateAsync(State(project.Id), 0);
        var loaded = await store.GetStateAsync(project.Id);
        Assert.Equal(1, saved.Revision); Assert.Equal("Ship R31", loaded.CurrentObjective);
        Assert.Equal(ProjectStateItemKind.AcceptedDecision, Assert.Single(loaded.Items).Kind);
        Assert.Equal(EvidenceOrigin.UserProvided, loaded.Items[0].Origin);
        Assert.Equal("docs/projects.md", loaded.Items[0].Source!.Locator);
    }

    [Fact]
    public async Task State_save_rejects_a_stale_revision_without_changing_data()
    {
        using var temp = new TempDir(); var (store, project) = NewStore(temp); await store.SaveAsync(project);
        var first = await store.SaveStateAsync(State(project.Id), 0);
        var stale = State(project.Id); stale.Status = "Wrong";
        var error = await Assert.ThrowsAsync<ProjectStateRevisionConflictException>(() => store.SaveStateAsync(stale, 0));
        Assert.Equal(0, error.ExpectedRevision); Assert.Equal(1, error.ActualRevision);
        Assert.Equal("Active", (await store.GetStateAsync(project.Id)).Status);
        Assert.Equal(1, first.Revision);
    }

    [Fact]
    public async Task Saving_a_revision_can_remove_every_accepted_field_and_item()
    {
        using var temp = new TempDir(); var (store, project) = NewStore(temp); await store.SaveAsync(project);
        var first = await store.SaveStateAsync(State(project.Id), 0);
        first.CurrentObjective = string.Empty; first.Milestone = string.Empty; first.Status = string.Empty; first.Items.Clear();
        var empty = await store.SaveStateAsync(first, 1);
        Assert.Equal(2, empty.Revision); Assert.Empty(empty.Items); Assert.Equal(string.Empty, empty.CurrentObjective);
    }

    [Fact]
    public async Task Proposal_creation_does_not_change_accepted_state()
    {
        using var temp = new TempDir(); var (store, project) = NewStore(temp); await store.SaveAsync(project);
        var accepted = await store.SaveStateAsync(State(project.Id), 0);
        var proposed = accepted.Clone(); proposed.Status = "Proposed";
        await store.CreateProposalAsync(new ProjectStateProposal { ProjectId = project.Id, BaseRevision = 1, ProposedState = proposed });
        Assert.Equal("Active", (await store.GetStateAsync(project.Id)).Status);
        Assert.Single(await store.GetProposalsAsync(project.Id));
    }

    [Fact]
    public async Task Accepting_a_proposal_creates_one_revision_and_marks_it_reviewed()
    {
        using var temp = new TempDir(); var (store, project) = NewStore(temp); await store.SaveAsync(project);
        var accepted = await store.SaveStateAsync(State(project.Id), 0);
        var proposed = accepted.Clone(); proposed.Status = "Done"; proposed.UpdatedByOrigin = EvidenceOrigin.ModelInference;
        var proposal = await store.CreateProposalAsync(new ProjectStateProposal
        {
            ProjectId = project.Id, BaseRevision = 1, ProposedState = proposed,
            Origin = EvidenceOrigin.ModelInference,
            Source = new SourceReference(ProvenanceKind.AgentTool, "Explicit proposal", "task-1", EvidenceOrigin: EvidenceOrigin.ModelInference)
        });
        var result = await store.AcceptProposalAsync(proposal.Id);
        Assert.Equal(2, result.Revision); Assert.Equal("Done", result.Status);
        Assert.Empty(await store.GetProposalsAsync(project.Id));
        Assert.Equal(ProjectStateProposalStatus.Accepted, Assert.Single(await store.GetProposalsAsync(project.Id, true)).Status);
    }

    [Fact]
    public async Task Edited_acceptance_records_user_origin()
    {
        using var temp = new TempDir(); var (store, project) = NewStore(temp); await store.SaveAsync(project);
        var accepted = await store.SaveStateAsync(State(project.Id), 0);
        var proposal = await store.CreateProposalAsync(new ProjectStateProposal
        { ProjectId = project.Id, BaseRevision = 1, ProposedState = accepted.Clone() });
        var edited = accepted.Clone(); edited.CurrentObjective = "User edited"; edited.Items[0].Text = "Edited decision";
        var result = await store.AcceptProposalAsync(proposal.Id, edited);
        Assert.Equal(EvidenceOrigin.UserProvided, result.UpdatedByOrigin);
        Assert.Equal(EvidenceOrigin.UserProvided, Assert.Single(result.Items).Origin);
    }

    [Fact]
    public async Task Stale_proposal_acceptance_is_atomic_and_remains_pending()
    {
        using var temp = new TempDir(); var (store, project) = NewStore(temp); await store.SaveAsync(project);
        var first = await store.SaveStateAsync(State(project.Id), 0);
        var proposal = await store.CreateProposalAsync(new ProjectStateProposal
        { ProjectId = project.Id, BaseRevision = 1, ProposedState = first.Clone() });
        first.Status = "Changed"; await store.SaveStateAsync(first, 1);
        await Assert.ThrowsAsync<ProjectStateRevisionConflictException>(() => store.AcceptProposalAsync(proposal.Id));
        Assert.Equal("Changed", (await store.GetStateAsync(project.Id)).Status);
        Assert.Equal(ProjectStateProposalStatus.Pending, Assert.Single(await store.GetProposalsAsync(project.Id)).Status);
    }

    [Fact]
    public async Task Rejecting_a_proposal_records_reason_without_changing_state()
    {
        using var temp = new TempDir(); var (store, project) = NewStore(temp); await store.SaveAsync(project);
        var accepted = await store.SaveStateAsync(State(project.Id), 0);
        var proposal = await store.CreateProposalAsync(new ProjectStateProposal
        { ProjectId = project.Id, BaseRevision = 1, ProposedState = accepted.Clone() });
        await store.RejectProposalAsync(proposal.Id, "Not factual");
        var reviewed = Assert.Single(await store.GetProposalsAsync(project.Id, true));
        Assert.Equal(ProjectStateProposalStatus.Rejected, reviewed.Status); Assert.Equal("Not factual", reviewed.RejectionReason);
        Assert.Equal(1, (await store.GetStateAsync(project.Id)).Revision);
    }

    [Fact]
    public async Task Project_delete_removes_owned_state_and_proposals()
    {
        using var temp = new TempDir(); var (store, project) = NewStore(temp); await store.SaveAsync(project);
        var accepted = await store.SaveStateAsync(State(project.Id), 0);
        await store.CreateProposalAsync(new ProjectStateProposal { ProjectId = project.Id, BaseRevision = 1, ProposedState = accepted.Clone() });
        await store.DeleteAsync(project.Id);
        Assert.Equal(0, (await store.GetStateAsync(project.Id)).Revision);
        Assert.Empty(await store.GetProposalsAsync(project.Id, true));
    }

    [Fact]
    public async Task V1_projects_database_migrates_additively()
    {
        using var temp = new TempDir(); var settings = NewSettings(temp); var data = temp.PathFor("data"); Directory.CreateDirectory(data);
        settings.Settings.DataManagement.DataRootDirectory = data;
        await using (var c = new SqliteConnection($"Data Source={Path.Combine(data, "projects.db")}"))
        {
            await c.OpenAsync(); var command = c.CreateCommand();
            command.CommandText = """
                CREATE TABLE projects (id TEXT PRIMARY KEY, name TEXT NOT NULL, description TEXT NOT NULL, folder_root TEXT NOT NULL,
                  dataset_id TEXT NOT NULL, default_model_id TEXT NOT NULL, default_system_prompt TEXT NOT NULL, color TEXT NOT NULL,
                  created_at TEXT NOT NULL, updated_at TEXT NOT NULL, last_opened_at TEXT NOT NULL, is_archived INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE hermaeus_schema_versions (scope TEXT PRIMARY KEY, version INTEGER NOT NULL, updated_at TEXT NOT NULL);
                INSERT INTO hermaeus_schema_versions VALUES ('projects', 1, '2026-01-01T00:00:00Z');
                """;
            await command.ExecuteNonQueryAsync();
        }
        var store = new ProjectStore(settings); await store.InitializeAsync();
        await store.SaveAsync(new Project { Id = "p", Name = "P" });
        Assert.Equal(1, (await store.SaveStateAsync(State("p"), 0)).Revision);
    }

    [Fact]
    public async Task State_survives_a_data_root_migration()
    {
        using var temp = new TempDir(); var previous = temp.PathFor("old"); var next = temp.PathFor("new"); Directory.CreateDirectory(previous);
        var settings = NewSettings(temp); settings.Settings.DataManagement.DataRootDirectory = previous;
        var store = new ProjectStore(settings); var project = new Project { Id = "p", Name = "P" }; await store.SaveAsync(project);
        await store.SaveStateAsync(State(project.Id), 0); SqliteConnection.ClearAllPools();
        settings.Settings.DataManagement.DataRootDirectory = next; await settings.SaveAsync(previous);
        Assert.Equal("Ship R31", (await new ProjectStore(settings).GetStateAsync(project.Id)).CurrentObjective);
    }

    [Fact]
    public async Task State_refuses_more_than_64_items()
    {
        using var temp = new TempDir(); var (store, project) = NewStore(temp); await store.SaveAsync(project);
        var state = State(project.Id); state.Items = Enumerable.Range(0, 65).Select(i => new ProjectStateItem { Text = i.ToString() }).ToList();
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveStateAsync(state, 0));
    }

    [Fact]
    public void Empty_state_preserves_an_empty_context_boundary()
    {
        var context = ProjectStateContextBuilder.Build(new ProjectState { ProjectId = "p" });
        Assert.Equal(string.Empty, context.Text); Assert.Empty(context.Sources); Assert.Equal(0, context.Revision);
    }

    [Fact]
    public void Context_is_bounded_and_contains_only_accepted_state_fields()
    {
        var state = State("p", 7);
        state.Items = Enumerable.Range(0, 20).Select(i => new ProjectStateItem
        { Id = $"i{i}", Kind = ProjectStateItemKind.NextAction, Text = $"Action {i}", Order = i }).ToList();
        var context = ProjectStateContextBuilder.Build(state);
        Assert.Equal(7, context.Revision); Assert.True(context.Text.Length <= ProjectStateContextBuilder.MaxCharacters);
        Assert.Equal(ProjectStateContextBuilder.MaxItems, context.Sources.Count);
        Assert.DoesNotContain("proposal", context.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Chat_receipt_labels_Project_State_separately_with_revision_locator()
    {
        var source = Assert.Single(ProjectStateContextBuilder.Build(State("p", 3)).Sources);
        var section = Assert.Single(ChatContextReceipt.Build([source]));
        Assert.Equal("Project State", section.Label); Assert.Contains(":state:3:", source.Locator, StringComparison.Ordinal);
    }

    [Fact]
    public void Agent_receipt_labels_Project_State_separately()
    {
        var pack = new AgentContextPack();
        pack.ProjectState.Add(new AgentRetrievedItem("project-state", "Project State revision 4", "accepted", 1, Locator: "project:p:state:4"));
        var section = Assert.Single(AgentContextReceiptBuilder.Build(pack));
        Assert.Equal("Project State", section.SectionLabel); Assert.Equal(["Project State revision 4"], section.ItemIdentifiers);
    }

    [Fact]
    public async Task Agent_context_builder_includes_only_accepted_state_for_bound_tasks()
    {
        using var temp = new TempDir(); var settings = NewSettings(temp); settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var projects = new ProjectStore(settings); await projects.SaveAsync(new Project { Id = "p", Name = "P" });
        var accepted = await projects.SaveStateAsync(State("p"), 0);
        var proposed = accepted.Clone(); proposed.CurrentObjective = "Pending secret";
        await projects.CreateProposalAsync(new ProjectStateProposal { ProjectId = "p", BaseRevision = 1, ProposedState = proposed });
        var taskStore = new FileAgentTaskStateStore(settings); await taskStore.InitializeAsync();
        var ragStore = new SqliteRagStore(settings); await ragStore.InitializeAsync();
        var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
        var workspace = temp.PathFor("workspace"); Directory.CreateDirectory(workspace);
        var builder = new AgentContextBuilder(
            new AgentWorkspaceTools(), new AgentRetrievalService(rag, ragStore),
            new WorkspaceMemoryStore(new MemoryStore(settings), settings),
            new WorkspaceActivationService(new WorkspaceManifestService(), new FileWorkspaceProfileStore(settings)),
            taskStore, settings, projectState: projects);
        var pack = await builder.BuildAsync(
            new AgentTaskState { TaskId = "task", Goal = "Work", ProjectId = "p" },
            new AgentWorkspaceOptions(workspace, null, "fake"));
        var item = Assert.Single(pack.ProjectState);
        Assert.Contains("Ship R31", item.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Pending secret", item.Content, StringComparison.Ordinal);
        Assert.Equal("Project State", Assert.Single(AgentContextReceiptBuilder.Build(pack), section => section.SectionLabel == "Project State").SectionLabel);
    }
}
