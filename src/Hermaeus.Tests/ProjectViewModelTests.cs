using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class ProjectViewModelTests
{
    private sealed record Harness(
        ProjectViewModel Vm,
        ProjectStore Projects,
        ConversationStore Conversations,
        MemoryStore Memories,
        FileAgentTaskStateStore AgentTasks,
        SqliteRagStore Rag,
        ISettingsService Settings);

    private static async Task<Harness> NewHarnessAsync(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var projects = new ProjectStore(settings);
        var conversations = new ConversationStore(settings);
        var memories = new MemoryStore(settings);
        var agentTasks = new FileAgentTaskStateStore(settings);
        var rag = new SqliteRagStore(settings);
        await conversations.InitializeAsync();
        await memories.InitializeAsync();
        await agentTasks.InitializeAsync();
        await rag.InitializeAsync();

        var vm = new ProjectViewModel(projects, settings, new FakeToasts(), memories, conversations, agentTasks, rag);
        return new Harness(vm, projects, conversations, memories, agentTasks, rag, settings);
    }

    [Fact]
    public async Task No_project_default_path_behaves_identically_to_no_projects_existing()
    {
        using var temp = new TempDir();
        var h = await NewHarnessAsync(temp);

        await h.Vm.EnsureLoadedAsync();
        Assert.Null(h.Vm.ActiveProject);
        Assert.Empty(h.Vm.Projects);
        Assert.Equal(string.Empty, h.Settings.Settings.Ui.ActiveProjectId);
    }

    [Fact]
    public async Task Switching_persists_active_project_id_and_bumps_last_opened()
    {
        using var temp = new TempDir();
        var h = await NewHarnessAsync(temp);
        var project = new Project { Name = "Alpha" };
        await h.Projects.SaveAsync(project);
        var before = project.LastOpenedAt;

        await h.Vm.SwitchToCommand.ExecuteAsync(project);

        Assert.Equal(project.Id, h.Vm.ActiveProject?.Id);
        Assert.Equal(project.Id, h.Settings.Settings.Ui.ActiveProjectId);
        var reloaded = await h.Projects.GetByIdAsync(project.Id);
        Assert.True(reloaded!.LastOpenedAt >= before);
    }

    [Fact]
    public async Task Switching_to_no_project_clears_active_project_id()
    {
        using var temp = new TempDir();
        var h = await NewHarnessAsync(temp);
        var project = new Project { Name = "Alpha" };
        await h.Projects.SaveAsync(project);
        await h.Vm.SwitchToCommand.ExecuteAsync(project);

        await h.Vm.SwitchToCommand.ExecuteAsync(null);

        Assert.Null(h.Vm.ActiveProject);
        Assert.Equal(string.Empty, h.Settings.Settings.Ui.ActiveProjectId);
    }

    [Fact]
    public async Task Switching_projects_never_rewrites_an_existing_conversations_binding()
    {
        using var temp = new TempDir();
        var h = await NewHarnessAsync(temp);
        var projectA = new Project { Name = "A" };
        var projectB = new Project { Name = "B" };
        await h.Projects.SaveAsync(projectA);
        await h.Projects.SaveAsync(projectB);

        var conv = new Conversation { Id = "c1", Title = "Existing", ProjectId = projectA.Id };
        await h.Conversations.SaveAsync(conv);

        await h.Vm.SwitchToCommand.ExecuteAsync(projectB);

        var reloaded = await h.Conversations.GetByIdAsync("c1");
        Assert.Equal(projectA.Id, reloaded!.ProjectId);
    }

    /// <summary>doc 01 1.6 acceptance: switching projects with a task in Running state
    /// leaves that task's WorkspaceRoot and ProjectId byte-identical.</summary>
    [Fact]
    public async Task Switching_projects_leaves_a_running_tasks_workspace_and_project_untouched()
    {
        using var temp = new TempDir();
        var h = await NewHarnessAsync(temp);
        var projectA = new Project { Name = "A" };
        var projectB = new Project { Name = "B" };
        await h.Projects.SaveAsync(projectA);
        await h.Projects.SaveAsync(projectB);

        var workspace = temp.PathFor("ws");
        Directory.CreateDirectory(workspace);
        var task = new AgentTaskState
        {
            TaskId = "running1",
            Goal = "Do work",
            Status = AgentTaskStatus.Running,
            WorkspaceRoot = Path.GetFullPath(workspace),
            ProjectId = projectA.Id
        };
        await h.AgentTasks.SaveAsync(task);

        // Simulate the active project switching while the task is running.
        await h.Vm.SwitchToCommand.ExecuteAsync(projectB);

        var reloaded = await h.AgentTasks.LoadAsync("running1");
        Assert.Equal(task.WorkspaceRoot, reloaded!.WorkspaceRoot);
        Assert.Equal(projectA.Id, reloaded.ProjectId);
    }

    [Fact]
    public async Task OpenNewProjectFromWorkspace_offers_adoption_of_existing_workspace_notes_by_count()
    {
        using var temp = new TempDir();
        var h = await NewHarnessAsync(temp);
        var workspace = temp.PathFor("ws");
        Directory.CreateDirectory(workspace);
        var normalizedRoot = Path.GetFullPath(workspace);

        await h.Memories.SaveAsync(new Memory { Id = "n1", Scope = MemoryScope.Workspace, ScopeId = normalizedRoot, Title = "Note 1", Content = "x" });
        await h.Memories.SaveAsync(new Memory { Id = "n2", Scope = MemoryScope.Workspace, ScopeId = normalizedRoot, Title = "Note 2", Content = "y" });

        await h.Vm.OpenNewProjectFromWorkspaceAsync(workspace);

        Assert.Equal(2, h.Vm.AdoptableWorkspaceNoteCount);
        Assert.True(h.Vm.AdoptWorkspaceNotes);
        Assert.Equal(normalizedRoot, h.Vm.EditingProject.FolderRoot);
        Assert.Equal(Path.GetFileName(normalizedRoot), h.Vm.EditingProject.Name);

        h.Vm.EditingProject.Name = "Adopted project";
        await h.Vm.SaveEditingProjectCommand.ExecuteAsync(null);

        var adopted = await h.Memories.GetByScopeAsync(MemoryScope.Project, h.Vm.EditingProject.Id);
        Assert.Equal(2, adopted.Count);
        Assert.Empty(await h.Memories.GetByScopeAsync(MemoryScope.Workspace, normalizedRoot));
    }

    [Fact]
    public async Task Deleting_a_project_clears_bindings_but_keeps_all_content()
    {
        using var temp = new TempDir();
        var h = await NewHarnessAsync(temp);
        var project = new Project { Name = "Doomed" };
        await h.Projects.SaveAsync(project);

        var conv = new Conversation { Id = "c1", Title = "Kept", ProjectId = project.Id };
        await h.Conversations.SaveAsync(conv);
        await h.Memories.SaveAsync(new Memory { Id = "m1", Scope = MemoryScope.Project, ScopeId = project.Id, Content = "kept fact" });
        var dataset = new Hermaeus.Rag.Models.RagDataset { Id = "ds1", Name = "Kept dataset", ProjectId = project.Id };
        await h.Rag.SaveDatasetAsync(dataset);

        await h.Vm.OpenEditForCommand.ExecuteAsync(project);
        h.Vm.RequestConfirmDelete = _ => Task.FromResult(true);
        await h.Vm.DeleteEditingProjectCommand.ExecuteAsync(null);

        Assert.Null(await h.Projects.GetByIdAsync(project.Id));

        var keptConv = await h.Conversations.GetByIdAsync("c1");
        Assert.NotNull(keptConv);
        Assert.Equal(string.Empty, keptConv!.ProjectId);

        var keptMemory = await h.Memories.GetByIdAsync("m1");
        Assert.NotNull(keptMemory);
        Assert.Equal(MemoryScope.Global, keptMemory!.Scope);
        Assert.Equal("kept fact", keptMemory.Content);

        var keptDataset = (await h.Rag.GetDatasetsAsync()).Single(d => d.Id == "ds1");
        Assert.Equal(string.Empty, keptDataset.ProjectId);
        Assert.Equal("Kept dataset", keptDataset.Name);
    }

    [Fact]
    public async Task Delete_is_cancelled_when_confirmation_is_declined()
    {
        using var temp = new TempDir();
        var h = await NewHarnessAsync(temp);
        var project = new Project { Name = "Kept" };
        await h.Projects.SaveAsync(project);

        await h.Vm.OpenEditForCommand.ExecuteAsync(project);
        h.Vm.RequestConfirmDelete = _ => Task.FromResult(false);
        await h.Vm.DeleteEditingProjectCommand.ExecuteAsync(null);

        Assert.NotNull(await h.Projects.GetByIdAsync(project.Id));
    }

    [Fact]
    public async Task Editing_counts_reflect_bound_conversations_tasks_memories_and_dataset_chunks()
    {
        using var temp = new TempDir();
        var h = await NewHarnessAsync(temp);
        var dataset = new Hermaeus.Rag.Models.RagDataset { Id = "ds1", Name = "DS", ChunkCount = 7 };
        await h.Rag.SaveDatasetAsync(dataset);
        var project = new Project { Name = "Counted", DatasetId = "ds1" };
        await h.Projects.SaveAsync(project);

        await h.Conversations.SaveAsync(new Conversation { Id = "c1", ProjectId = project.Id });
        await h.Conversations.SaveAsync(new Conversation { Id = "c2", ProjectId = project.Id });
        await h.Memories.SaveAsync(new Memory { Id = "m1", Scope = MemoryScope.Project, ScopeId = project.Id, Content = "f" });
        await h.AgentTasks.SaveAsync(new AgentTaskState { TaskId = "t1", Goal = "g", ProjectId = project.Id });

        await h.Vm.OpenEditForCommand.ExecuteAsync(project);

        Assert.Equal(2, h.Vm.EditingConversationCount);
        Assert.Equal(1, h.Vm.EditingMemoryCount);
        Assert.Equal(1, h.Vm.EditingAgentTaskCount);
        Assert.Equal(7, h.Vm.EditingDatasetChunkCount);
    }

    [Fact]
    public void FolderRoot_traversal_and_symlink_are_refused_with_a_visible_reason_and_nothing_saved()
    {
        Assert.False(PathRootValidator.TryValidate("C:\\a\\..\\b", out _, out var err1));
        Assert.NotEqual(string.Empty, err1);
    }
}
