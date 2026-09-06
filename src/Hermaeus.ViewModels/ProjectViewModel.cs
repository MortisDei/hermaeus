using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

/// <summary>
/// r24 doc 01: owns the project switcher and the project detail editor. Not
/// a nav panel by design (doc 06 "Explicit rejections") - a compact header
/// control plus a modal editor reachable from it.
///
/// Wizard-singleton lesson (docs/review 01-projects.md 1.3): this ViewModel
/// is a DI singleton constructed once. It must not save an active project id
/// before its own list has loaded, or a not-yet-loaded switcher could write
/// an empty active project over a real one. <see cref="EnsureLoadedAsync"/>
/// loads on first use; nothing here writes settings before that completes.
/// </summary>
public partial class ProjectViewModel : ViewModelBase
{
    private readonly IProjectStore _store;
    private readonly ISettingsService _settings;
    private readonly IToastService _toasts;
    private readonly IMemoryStore _memories;
    private readonly IKnowledgeRevisionStore _knowledge;
    private readonly IConversationStore _conversations;
    private readonly IAgentTaskStateStore _agentTasks;
    private readonly SqliteRagStore? _rag;
    private readonly IProjectStateStore? _stateStore;
    private bool _loaded;

    public UiBoundCollection<Project> Projects { get; } = [];
    public UiBoundCollection<RagDataset> AvailableDatasets { get; } = [];
    public UiBoundCollection<ProjectStateProposal> StateProposals { get; } = [];

    [ObservableProperty] private Project? _activeProject;
    [ObservableProperty] private bool _isSwitcherOpen;
    [ObservableProperty] private bool _showArchivedInSwitcher;

    [ObservableProperty] private bool _isEditorOpen;
    [ObservableProperty] private Project _editingProject = new();
    [ObservableProperty] private bool _isNewProject;
    [ObservableProperty] private string _folderRootError = string.Empty;
    [ObservableProperty] private int _editingConversationCount;
    [ObservableProperty] private int _editingAgentTaskCount;
    [ObservableProperty] private int _editingMemoryCount;
    [ObservableProperty] private int _editingDatasetChunkCount;
    [ObservableProperty] private int _adoptableWorkspaceNoteCount;
    [ObservableProperty] private bool _adoptWorkspaceNotes = true;
    [ObservableProperty] private ProjectState _editingState = new();
    [ObservableProperty] private ProjectStateProposal? _selectedStateProposal;
    [ObservableProperty] private ProjectState _editingProposalState = new();
    [ObservableProperty] private string _newStateItemText = string.Empty;
    [ObservableProperty] private ProjectStateItemKind _newStateItemKind = ProjectStateItemKind.NextAction;
    [ObservableProperty] private string _proposalRejectionReason = string.Empty;
    private string _pendingAdoptionWorkspaceRoot = string.Empty;

    public IReadOnlyList<string> ColorKeys => ProjectColors.All;
    public IReadOnlyList<ProjectStateItemKind> StateItemKinds { get; } = Enum.GetValues<ProjectStateItemKind>();
    public string ActiveProjectLabel => ActiveProject is null ? "No project" : ActiveProject.Name;
    public string EditorTitle => IsNewProject ? "New project" : "Edit project";
    public string ArchiveToggleLabel => EditingProject.IsArchived ? "Unarchive" : "Archive";
    public bool HasAdoptableWorkspaceNotes => AdoptableWorkspaceNoteCount > 0;

    /// <summary>Raised after a switch completes (including to "No project", null).
    /// MainWindowViewModel wires this to pre-fill dependent panels (doc 01 1.6).</summary>
    public event Action<Project?>? ProjectSwitched;

    public Action? RequestFolderRootPicker { get; set; }
    public Func<string, Task<bool>>? RequestConfirmDelete { get; set; }

    /// <summary>Wired by MainWindowViewModel so the switcher's two contextual creation
    /// entry points (doc 01 1.5) can read the current conversation/workspace without
    /// this ViewModel depending on Chat/Agent directly.</summary>
    public Func<(string Title, string DatasetId, string ModelId)>? ChatContextProvider { get; set; }
    public Func<string>? AgentWorkspaceProvider { get; set; }

    /// <summary>The view shows/hides the editor window from these instead of a bound
    /// bool, matching this app's existing dialog-wiring convention (e.g. Agent's
    /// RequestRewindConfirmation) rather than a Window subscribing to PropertyChanged.</summary>
    public Action? RequestOpenEditor { get; set; }
    public Action? RequestCloseEditor { get; set; }

    public ProjectViewModel(
        IProjectStore store,
        ISettingsService settings,
        IToastService toasts,
        IMemoryStore memories,
        IConversationStore conversations,
        IAgentTaskStateStore agentTasks,
        SqliteRagStore? rag = null,
        IProjectStateStore? stateStore = null,
        IKnowledgeRevisionStore? knowledge = null)
    {
        _store = store;
        _settings = settings;
        _toasts = toasts;
        _memories = memories;
        _knowledge = knowledge ?? memories as IKnowledgeRevisionStore
            ?? throw new ArgumentException("The memory store must expose knowledge revision writes.", nameof(memories));
        _conversations = conversations;
        _agentTasks = agentTasks;
        _rag = rag;
        _stateStore = stateStore;
    }

    /// <summary>doc 04 4.1: registered next to the ViewModel that owns the action.</summary>
    public void RegisterCommands(ICommandRegistry registry)
    {
        registry.Register(new AppCommand(
            Id: "projects.switch", Title: "Switch project", Area: "Chat",
            Description: "Open the project switcher.",
            Keywords: ["project", "switch", "workspace"], Shortcut: "",
            CanExecute: () => true,
            Execute: () => OpenSwitcherCommand.ExecuteAsync(null)));

        registry.Register(new AppCommand(
            Id: "projects.new", Title: "New project", Area: "Chat",
            Description: "Create a new project.",
            Keywords: ["project", "new", "create"], Shortcut: "",
            CanExecute: () => true,
            Execute: () => { OpenNewProjectEmptyCommand.Execute(null); return Task.CompletedTask; }));
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await ReloadAsync();
        var activeId = _settings.Settings.Ui.ActiveProjectId;
        if (!string.IsNullOrWhiteSpace(activeId))
            ActiveProject = Projects.FirstOrDefault(p => p.Id == activeId);
    }

    public async Task ReloadAsync()
    {
        var all = await _store.GetAllAsync();
        RunOnUi(() =>
        {
            Projects.Clear();
            foreach (var p in all.Where(p => ShowArchivedInSwitcher || !p.IsArchived))
                Projects.Add(p);
        });
    }

    [RelayCommand]
    private async Task OpenSwitcherAsync()
    {
        await EnsureLoadedAsync();
        IsSwitcherOpen = true;
    }

    [RelayCommand]
    private void CloseSwitcher() => IsSwitcherOpen = false;

    partial void OnShowArchivedInSwitcherChanged(bool value) => _ = ReloadAsync();

    /// <summary>Switching is instant, never rewrites an existing record, and only
    /// changes what new work inherits (doc 01 1.6). Pass null for "No project".</summary>
    [RelayCommand]
    private async Task SwitchToAsync(Project? project)
    {
        await EnsureLoadedAsync();
        if (project is not null)
        {
            project.LastOpenedAt = DateTime.UtcNow;
            await _store.SaveAsync(project);
        }

        ActiveProject = project;
        _settings.Settings.Ui.ActiveProjectId = project?.Id ?? string.Empty;
        await _settings.SaveAsync();
        IsSwitcherOpen = false;
        await ReloadAsync();
        ProjectSwitched?.Invoke(project);
    }

    partial void OnActiveProjectChanged(Project? value) => OnPropertyChanged(nameof(ActiveProjectLabel));
    partial void OnIsNewProjectChanged(bool value) => OnPropertyChanged(nameof(EditorTitle));
    partial void OnAdoptableWorkspaceNoteCountChanged(int value) => OnPropertyChanged(nameof(HasAdoptableWorkspaceNotes));
    partial void OnEditingProjectChanged(Project value) => OnPropertyChanged(nameof(ArchiveToggleLabel));

    // ── 1.5 zero-ceremony creation ──────────────────────────────────────────

    [RelayCommand]
    private void OpenNewProjectEmpty() => OpenEditor(new Project(), isNew: true);

    [RelayCommand]
    private void OpenNewProjectFromCurrentConversation()
    {
        var context = ChatContextProvider?.Invoke() ?? (string.Empty, string.Empty, string.Empty);
        OpenNewProjectFromConversation(context.Title, context.DatasetId, context.ModelId);
    }

    [RelayCommand]
    private async Task OpenNewProjectFromAgentWorkspaceAsync()
    {
        var root = AgentWorkspaceProvider?.Invoke() ?? string.Empty;
        await OpenNewProjectFromWorkspaceAsync(root);
    }

    /// <summary>Pre-fills from the current conversation: name, dataset and model.</summary>
    public void OpenNewProjectFromConversation(string conversationTitle, string datasetId, string modelId)
    {
        var project = new Project
        {
            Name = string.IsNullOrWhiteSpace(conversationTitle) || conversationTitle == "New Conversation"
                ? string.Empty
                : conversationTitle,
            DatasetId = datasetId,
            DefaultModelId = modelId
        };
        OpenEditor(project, isNew: true);
    }

    /// <summary>Pre-fills from the Agent's selected workspace root: name from the folder
    /// name, folder root pre-filled, and offers adoption of that root's existing
    /// workspace memory notes (a checkbox, default on, showing the count).</summary>
    public async Task OpenNewProjectFromWorkspaceAsync(string workspaceRoot)
    {
        var project = new Project
        {
            Name = string.IsNullOrWhiteSpace(workspaceRoot) ? string.Empty : Path.GetFileName(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            FolderRoot = workspaceRoot
        };
        _pendingAdoptionWorkspaceRoot = workspaceRoot;
        AdoptableWorkspaceNoteCount = string.IsNullOrWhiteSpace(workspaceRoot)
            ? 0
            : (await _memories.GetByScopeAsync(MemoryScope.Workspace, Path.GetFullPath(workspaceRoot))).Count;
        AdoptWorkspaceNotes = AdoptableWorkspaceNoteCount > 0;
        OpenEditor(project, isNew: true);
    }

    [RelayCommand]
    private async Task OpenEditForAsync(Project project)
    {
        OpenEditor(project.Clone(), isNew: false);
        await Task.WhenAll(RefreshEditingCountsAsync(project), RefreshProjectStateAsync(project.Id));
    }

    private async Task RefreshAvailableDatasetsAsync()
    {
        if (_rag is null) return;
        var datasets = await _rag.GetDatasetsAsync();
        RunOnUi(() =>
        {
            AvailableDatasets.Clear();
            foreach (var ds in datasets)
                AvailableDatasets.Add(ds);
        });
    }

    private void OpenEditor(Project project, bool isNew)
    {
        if (isNew && string.IsNullOrWhiteSpace(project.Color))
            project.Color = ProjectColors.Default;
        EditingProject = project;
        IsNewProject = isNew;
        EditingState = new ProjectState { ProjectId = project.Id };
        EditingProposalState = new ProjectState { ProjectId = project.Id };
        SelectedStateProposal = null;
        StateProposals.Clear();
        FolderRootError = string.Empty;
        if (!isNew)
        {
            _pendingAdoptionWorkspaceRoot = string.Empty;
            AdoptableWorkspaceNoteCount = 0;
        }
        IsSwitcherOpen = false;
        IsEditorOpen = true;
        RequestOpenEditor?.Invoke();
        _ = RefreshAvailableDatasetsAsync();
    }

    [RelayCommand]
    private void CloseEditor()
    {
        IsEditorOpen = false;
        RequestCloseEditor?.Invoke();
    }

    [RelayCommand]
    private void BrowseEditingFolderRoot() => RequestFolderRootPicker?.Invoke();

    [RelayCommand]
    private void SelectEditingColor(string color)
    {
        if (!ProjectColors.IsValid(color)) return;
        EditingProject.Color = color;
        OnPropertyChanged(nameof(EditingProject));
    }

    /// <summary>Called by the view after the folder picker returns a path.</summary>
    public void SetEditingFolderRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            EditingProject.FolderRoot = string.Empty;
            FolderRootError = string.Empty;
            return;
        }

        if (!PathRootValidator.TryValidate(path, out var normalized, out var error))
        {
            FolderRootError = error;
            return;
        }

        FolderRootError = string.Empty;
        EditingProject.FolderRoot = normalized;
    }

    [RelayCommand]
    private async Task SaveEditingProjectAsync()
    {
        if (string.IsNullOrWhiteSpace(EditingProject.Name))
        {
            _toasts.Show("Project needs a name", "Name is the only required field.", ToastKind.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(EditingProject.FolderRoot))
        {
            if (!PathRootValidator.TryValidate(EditingProject.FolderRoot, out var normalized, out var error))
            {
                FolderRootError = error;
                return;
            }

            EditingProject.FolderRoot = normalized;
        }

        EditingProject.Name = EditingProject.Name.Trim();
        await _store.SaveAsync(EditingProject);

        if (_stateStore is not null && (EditingState.Revision > 0 || !ProjectStateIsEmpty(EditingState)))
        {
            EditingState.ProjectId = EditingProject.Id;
            EditingState.UpdatedByOrigin = EvidenceOrigin.UserProvided;
            try
            {
                EditingState = await _stateStore.SaveStateAsync(EditingState, EditingState.Revision);
            }
            catch (ProjectStateRevisionConflictException ex)
            {
                await RefreshProjectStateAsync(EditingProject.Id);
                _toasts.Show("Project State changed", ex.Message, ToastKind.Warning);
                return;
            }
        }

        if (IsNewProject && AdoptWorkspaceNotes && !string.IsNullOrWhiteSpace(_pendingAdoptionWorkspaceRoot))
            await AdoptWorkspaceNotesAsync(_pendingAdoptionWorkspaceRoot, EditingProject.Id);

        IsEditorOpen = false;
        RequestCloseEditor?.Invoke();
        await ReloadAsync();
        if (ActiveProject?.Id == EditingProject.Id)
            ActiveProject = EditingProject;
        _toasts.Show("Project saved", EditingProject.Name, ToastKind.Success);
    }

    [RelayCommand]
    private void AddStateItem()
    {
        if (string.IsNullOrWhiteSpace(NewStateItemText)) return;
        EditingState.Items.Add(new ProjectStateItem
        {
            Kind = NewStateItemKind,
            Text = NewStateItemText.Trim(),
            Order = EditingState.Items.Count,
            Origin = EvidenceOrigin.UserProvided
        });
        NewStateItemText = string.Empty;
        OnPropertyChanged(nameof(EditingState));
    }

    [RelayCommand]
    private void RemoveStateItem(ProjectStateItem? item)
    {
        if (item is null) return;
        EditingState.Items.Remove(item);
        for (var index = 0; index < EditingState.Items.Count; index++) EditingState.Items[index].Order = index;
        OnPropertyChanged(nameof(EditingState));
    }

    [RelayCommand]
    private void RemoveProposalStateItem(ProjectStateItem? item)
    {
        if (item is null) return;
        EditingProposalState.Items.Remove(item);
        for (var index = 0; index < EditingProposalState.Items.Count; index++) EditingProposalState.Items[index].Order = index;
        OnPropertyChanged(nameof(EditingProposalState));
    }

    partial void OnSelectedStateProposalChanged(ProjectStateProposal? value)
    {
        EditingProposalState = value?.ProposedState.Clone() ?? new ProjectState { ProjectId = EditingProject.Id };
        ProposalRejectionReason = string.Empty;
    }

    [RelayCommand]
    private async Task AcceptStateProposalAsync()
    {
        if (_stateStore is null || SelectedStateProposal is null) return;
        try
        {
            EditingState = await _stateStore.AcceptProposalAsync(SelectedStateProposal.Id, EditingProposalState);
            await RefreshProjectStateAsync(EditingProject.Id);
            _toasts.Show("Project State proposal accepted", $"Revision {EditingState.Revision}", ToastKind.Success);
        }
        catch (ProjectStateRevisionConflictException ex)
        {
            await RefreshProjectStateAsync(EditingProject.Id);
            _toasts.Show("Proposal is stale", ex.Message, ToastKind.Warning);
        }
    }

    [RelayCommand]
    private async Task RejectStateProposalAsync()
    {
        if (_stateStore is null || SelectedStateProposal is null) return;
        await _stateStore.RejectProposalAsync(SelectedStateProposal.Id, ProposalRejectionReason);
        await RefreshProjectStateAsync(EditingProject.Id);
        _toasts.Show("Project State proposal rejected", "Accepted state was not changed.", ToastKind.Info);
    }

    private async Task RefreshProjectStateAsync(string projectId)
    {
        if (_stateStore is null) return;
        EditingState = await _stateStore.GetStateAsync(projectId);
        var proposals = await _stateStore.GetProposalsAsync(projectId);
        RunOnUi(() =>
        {
            StateProposals.Clear();
            foreach (var proposal in proposals) StateProposals.Add(proposal);
            SelectedStateProposal = StateProposals.FirstOrDefault();
        });
    }

    private static bool ProjectStateIsEmpty(ProjectState state) =>
        string.IsNullOrWhiteSpace(state.CurrentObjective)
        && string.IsNullOrWhiteSpace(state.Milestone)
        && string.IsNullOrWhiteSpace(state.Status)
        && state.Items.Count == 0;

    /// <summary>Reassigns every Workspace-scoped memory note under this root into the new
    /// project's scope. A copy of the note's meaning, not the workspace itself; the
    /// original workspace-scoped notes are gone once adopted, matching a move, not a
    /// duplicate (doc 01 1.5's checkbox is presented as "adopt", not "copy").</summary>
    private async Task<int> AdoptWorkspaceNotesAsync(string workspaceRoot, string projectId)
    {
        var normalizedRoot = Path.GetFullPath(workspaceRoot);
        var notes = await _memories.GetByScopeAsync(MemoryScope.Workspace, normalizedRoot);
        foreach (var note in notes)
        {
            note.Scope = MemoryScope.Project;
            note.ScopeId = projectId;
            var revision = await _knowledge.GetCurrentRevisionAsync(note.Id)
                ?? throw new InvalidOperationException($"Memory '{note.Id}' has no current revision.");
            await _knowledge.MutatePresentationAsync(note.Id, revision.RevisionId,
                KnowledgePresentationMutation.FromMemory(note));
        }

        return notes.Count;
    }

    /// <summary>Deleting a project never deletes its contents: it clears the binding on
    /// everything that pointed at it and removes the project row (doc 01 1.4).</summary>
    [RelayCommand]
    private async Task DeleteEditingProjectAsync()
    {
        var confirmed = RequestConfirmDelete is null || await RequestConfirmDelete(EditingProject.Name);
        if (!confirmed) return;

        await ClearProjectBindingsAsync(EditingProject.Id);
        await _store.DeleteAsync(EditingProject.Id);
        if (ActiveProject?.Id == EditingProject.Id)
        {
            ActiveProject = null;
            _settings.Settings.Ui.ActiveProjectId = string.Empty;
            await _settings.SaveAsync();
            ProjectSwitched?.Invoke(null);
        }

        IsEditorOpen = false;
        RequestCloseEditor?.Invoke();
        await ReloadAsync();
        _toasts.Show("Project deleted", "Its conversations, tasks, datasets and memories were kept.", ToastKind.Info);
    }

    private async Task ClearProjectBindingsAsync(string projectId)
    {
        foreach (var conv in (await _conversations.GetAllAsync()).Where(c => c.ProjectId == projectId))
        {
            conv.ProjectId = string.Empty;
            await _conversations.SaveAsync(conv);
        }

        if (_rag is not null)
        {
            foreach (var ds in (await _rag.GetDatasetsAsync()).Where(d => d.ProjectId == projectId))
            {
                ds.ProjectId = string.Empty;
                await _rag.SaveDatasetAsync(ds);
            }
        }

        foreach (var memory in await _memories.GetByScopeAsync(MemoryScope.Project, projectId))
        {
            memory.Scope = MemoryScope.Global;
            memory.ScopeId = string.Empty;
            var revision = await _knowledge.GetCurrentRevisionAsync(memory.Id)
                ?? throw new InvalidOperationException($"Memory '{memory.Id}' has no current revision.");
            await _knowledge.MutatePresentationAsync(memory.Id, revision.RevisionId,
                KnowledgePresentationMutation.FromMemory(memory));
        }

        // Agent tasks: task_state.json is the source of truth and is
        // rebuildable-index-backed (CLAUDE.md); a task's ProjectId is a
        // point-in-time record of what it was created under; a deleted
        // project does not go back and edit historical task files.
    }

    [RelayCommand]
    private async Task ToggleEditingArchiveAsync()
    {
        EditingProject.IsArchived = !EditingProject.IsArchived;
        OnPropertyChanged(nameof(ArchiveToggleLabel));
        await _store.SaveAsync(EditingProject);
        await ReloadAsync();
    }

    private async Task RefreshEditingCountsAsync(Project project)
    {
        EditingConversationCount = (await _conversations.GetAllAsync()).Count(c => c.ProjectId == project.Id);
        EditingMemoryCount = (await _memories.GetByScopeAsync(MemoryScope.Project, project.Id)).Count;
        try
        {
            EditingAgentTaskCount = (await _agentTasks.ListRecentAsync(limit: 5000))
                .Count(t => t.ProjectId == project.Id);
        }
        catch (Exception)
        {
            EditingAgentTaskCount = 0;
        }

        EditingDatasetChunkCount = 0;
        if (_rag is not null && !string.IsNullOrWhiteSpace(project.DatasetId))
        {
            var dataset = (await _rag.GetDatasetsAsync()).FirstOrDefault(d => d.Id == project.DatasetId);
            EditingDatasetChunkCount = dataset?.ChunkCount ?? 0;
        }
    }
}
