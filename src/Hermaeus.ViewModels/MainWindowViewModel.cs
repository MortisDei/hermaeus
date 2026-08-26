using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;

namespace Hermaeus.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IConversationStore _store;
    private readonly Hermaeus.Services.Recall.RecallIndexingService? _recallIndexing;
    private readonly IToastService _toasts;
    private readonly IRuntimeLogService _logs;
    private readonly ConversationExportService _exports;
    private CancellationTokenSource? _searchCts;
    private readonly ISettingsService _settingsService;
    private bool _refreshingFolderFilters;
    private bool _postSetupInitialized;
    private CancellationTokenSource? _watchedRefreshCts;
    private readonly Dictionary<string, CancellationTokenSource> _pendingMetadataSaves = new();
    private static readonly TimeSpan MetadataSaveDebounce = TimeSpan.FromMilliseconds(500);

    public ChatViewModel            Chat     { get; }
    public AgentViewModel           Agent    { get; }
    public SettingsViewModel        Settings { get; }
    public ModelManagementViewModel Models   { get; }
    public RagViewModel             Rag      { get; }
    public ServicesViewModel        Services { get; }
    public BenchmarkViewModel       Benchmarks { get; }
    public LabViewModel             Lab { get; }
    public SystemOverviewViewModel  SystemOverview { get; }
    public DoctorViewModel          Doctor { get; }
    public MemoriesViewModel        Memories { get; }
    public LogsViewModel            Logs { get; }
    public SetupWizardViewModel     Wizard { get; }
    public ProjectViewModel         Projects { get; }
    public PaletteViewModel         Palette { get; }
    public ActivityViewModel        Activity { get; }

    public UiBoundCollection<ConversationItemViewModel> Conversations { get; } = [];
    public UiBoundCollection<ToastViewModel> Toasts { get; } = [];
    public UiBoundCollection<ToastViewModel> ToastHistory { get; } = [];
    public UiBoundCollection<string> FolderFilters { get; } = ["All"];
    public Action<string>? RequestCopyToastDetails { get; set; }

    [ObservableProperty] private bool   _isSidebarOpen = true;
    [ObservableProperty] private string _searchQuery   = string.Empty;
    [ObservableProperty] private string _activePanel   = "chat";
    [ObservableProperty] private string _selectedFolderFilter = "All";
    [ObservableProperty] private bool   _showArchivedConversations;
    [ObservableProperty] private bool   _showQuickChat;
    [ObservableProperty] private bool   _showToastHistory;
    [ObservableProperty] private bool   _doctorHasErrors;
    [ObservableProperty] private bool   _doctorHasWarnings;
    [ObservableProperty] private bool   _doctorIsOk;

    public bool ShowChat     => ActivePanel == "chat";
    public bool ShowAgent    => ActivePanel == "agent";
    public bool ShowSettings => ActivePanel == "settings";
    public bool ShowModels   => ActivePanel == "models";
    public bool ShowRag      => ActivePanel == "rag";
    public bool ShowServices => ActivePanel == "services";
    public bool ShowBenchmarks => ActivePanel == "benchmarks";
    public bool ShowLab => ActivePanel == "lab";
    public bool ShowSystem => ActivePanel == "system";
    public bool ShowDoctor => ActivePanel == "doctor";
    public bool ShowMemories => ActivePanel == "memories";
    public bool ShowLogs => ActivePanel == "logs";
    public bool ShowActivity => ActivePanel == "activity";
    public bool ShowWizard => ActivePanel == "wizard";
    public bool SetupIncomplete => !_settingsService.Settings.SetupWizardCompleted;
    public bool ShowSetupResume => SetupIncomplete && !ShowWizard;
    public object ActiveViewModel => ActivePanel switch
    {
        "settings" => Settings,
        "agent"    => Agent,
        "models"   => Models,
        "rag"      => Rag,
        "services" => Services,
        "benchmarks" => Benchmarks,
        "lab"       => Lab,
        "system"   => SystemOverview,
        "doctor"   => Doctor,
        "memories" => Memories,
        "logs"     => Logs,
        "activity" => Activity,
        "wizard"   => Wizard,
        _          => Chat
    };

    public string WindowTitle
        => ShowChat ? $"Hermaeus - {Chat.ConversationTitle}" : $"Hermaeus - {ActivePanel?.ToUpperInvariant() ?? string.Empty}";

    public MainWindowViewModel(
        IConversationStore store,
        ChatViewModel chat,
        AgentViewModel agent,
        SettingsViewModel settings,
        ModelManagementViewModel models,
        RagViewModel rag,
        ServicesViewModel services,
        BenchmarkViewModel benchmarks,
        LabViewModel lab,
        SystemOverviewViewModel systemOverview,
        DoctorViewModel doctor,
        MemoriesViewModel memories,
        LogsViewModel logs,
        SetupWizardViewModel wizard,
        ProjectViewModel projects,
        ICommandRegistry commands,
        PaletteViewModel palette,
        ActivityViewModel activity,
        ISettingsService settingsService,
        IToastService toasts,
        IRuntimeLogService runtimeLogs,
        ConversationExportService exports,
        Hermaeus.Services.Recall.RecallIndexingService? recallIndexing = null)
    {
        _recallIndexing = recallIndexing;
        Palette = palette;
        Activity = activity;
        _toasts = toasts;
        _logs = runtimeLogs;
        _exports = exports;
        _settingsService = settingsService;
        _store = store; Chat = chat; Agent = agent; Settings = settings;
        Models = models; Rag = rag; Services = services;
        Benchmarks = benchmarks; Lab = lab; SystemOverview = systemOverview; Doctor = doctor; Memories = memories; Logs = logs; Wizard = wizard;
        Projects = projects;
        // r24 doc 01 1.6: switching a project only ever changes what NEW work
        // inherits. Existing conversations/tasks/datasets are never rewritten.
        Chat.ActiveProjectProvider = () => Projects.ActiveProject;
        Projects.ChatContextProvider = () => (Chat.ConversationTitle, Chat.RagDatasetId, Chat.SelectedModel?.Id ?? string.Empty);
        Projects.AgentWorkspaceProvider = () => Agent.WorkspaceRoot;
        Projects.ProjectSwitched += project =>
        {
            Agent.ActiveProjectId = project?.Id ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(project?.FolderRoot))
                Agent.PrefillWorkspaceRootFromProject(project.FolderRoot);
            Rag.SetDefaultDatasetFromProject(project?.DatasetId ?? string.Empty);
            Palette.SetActiveProject(project?.Id ?? string.Empty, project?.Name ?? string.Empty);
        };
        Palette.RequestNavigate = NavigateToRecallHitAsync;
        // r28 doc 03 3.2: Activity rows reuse the same navigator the palette
        // uses, so there is one answer to "where does a task live".
        Activity.RequestNavigate = NavigateToRecallHitAsync;
        Doctor.RequestNavigate = panel => ActivePanel = panel;
        Doctor.RequestOpenUrl = url =>
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _toasts.Show("Could not open browser", ex.Message, ToastKind.Error, 5000);
            }
        };
        Chat.RequestNavigate = panel => ActivePanel = panel;
        // r25 follow-up: Services reports whether the speech model is installed and
        // sends the user to Doctor to install it, rather than carrying a second,
        // independent install button that never learned it had succeeded.
        if (Services.Stt is { } stt)
            stt.RequestNavigate = panel => ActivePanel = panel;
        // r19 6.1: memory pill flyout's "Open in Memories" navigates and prefills search.
        Chat.RequestNavigateToMemory = title =>
        {
            ActivePanel = "memories";
            Memories.SearchText = title;
        };
        // r21 3.3: Dataset Manager's "Open in chat" - reuses the exact NewConversation
        // path (Ctrl+N), then attaches and persists the dataset.
        Rag.RequestOpenInChat = dataset => RunBackgroundTaskAsync("open dataset in chat", () => OpenDatasetInChatAsync(dataset));
        // r19 2.2: Doctor has no server-process knowledge of its own; bridge
        // the llama.cpp update flow's stop-before/restart-after to Services.
        Doctor.RequestStopRunningLlamaServersForUpdate = Services.StopRunningLlamaServersForUpdate;
        Doctor.RequestRestartServers = Services.RestartServersAsync;
        Doctor.RequestSyncServerExecutablePaths = Services.SyncAllExecutablePathsFromConfig;
        // Keep toolbar doctor badge in sync with doctor checks
        Doctor.Checks.CollectionChanged += (_, _) => UpdateDoctorStatus();
        UpdateDoctorStatus();
        Wizard.WizardCompleted += () =>
        {
            ActivePanel = "chat";
            Chat.RefreshSetupState();
            OnPropertyChanged(nameof(SetupIncomplete));
            OnPropertyChanged(nameof(ShowSetupResume));
            Settings.Reload();
            Services.RefreshAllDetectedModels();
            // r12 03-runtime-vm-correctness.md 3.1: finishing (or skipping)
            // the wizard on first run used to leave the app on a dead chat
            // panel - InitializeAsync had already returned early to show the
            // wizard, so nothing here ever auto-started servers, listed
            // models, or loaded datasets/agent/benchmarks until a restart.
            RunBackgroundTaskAsync("complete post-setup initialization", CompletePostSetupInitializationAsync);
            RunBackgroundTaskAsync("refresh models after setup", async () =>
            {
                Models.ForceRefresh = true;
                await RunOnUiAsync(() => Models.RefreshAsync());
            });
        };
        // allow settings view to request re-running the setup wizard
        Settings.RequestShowSetupWizard = () =>
        {
            Wizard.LoadFromSettings(resetStep: true);
            ActivePanel = "wizard";
        };
        // r29 doc 01 1.1: the Services page hosts the Voice and STT cards, which
        // are the same DI singletons Settings edits. Nothing on Services wrote
        // them to disk, so every edit made there was lost on restart. Route its
        // Save through the one existing save flow rather than adding a second.
        Services.SaveAllSettings = Settings.SaveAsync;
        Chat.PropertyChanged += (s, e) => { if (e.PropertyName == "ConversationTitle") OnPropertyChanged(nameof(WindowTitle)); };
        Chat.ConversationSaved += OnConversationSaved;
        // r27 01 1.3: Chat asks Services what it is waiting on, through a
        // delegate, so the chat view model keeps knowing nothing about Services.
        Chat.WarmingServerProvider = Services.GetWarmingChatServer;
        Chat.ManagedTelemetryRequestFactory = Services.CreateManagedTelemetryRequestAsync;
        // The RAG panel's Ask has no model picker of its own; it uses whatever
        // Chat has selected rather than an often-unset settings default.
        Rag.ChatModelProvider = () => Chat.SelectedModel?.Id ?? string.Empty;
        Services.ServerAvailabilityChanged += (_, _) =>
        {
            RunOnUi(Chat.RefreshWarmingState);
            RunBackgroundTaskAsync("refresh models after server availability change", RefreshModelsAfterServerChangeAsync);
        };
        _toasts.ToastRaised += OnToastRaised;

        Commands = commands;
        RegisterNavigationCommands(commands);
        Chat.RegisterCommands(commands);
        Agent.RegisterCommands(commands);
        Rag.RegisterCommands(commands);
        Services.RegisterCommands(commands);
        Doctor.RegisterCommands(commands);
        Memories.RegisterCommands(commands);
        Models.RegisterCommands(commands);
        Benchmarks.RegisterCommands(commands);
        SystemOverview.RegisterCommands(commands);
        Logs.RegisterCommands(commands);
        Projects.RegisterCommands(commands);
        Activity.RegisterCommands(commands);
    }

    public ICommandRegistry Commands { get; }

    /// <summary>doc 04 4.1: navigation is a user action like any other, so it lives in
    /// the registry too. MainWindowViewModel owns ActivePanel, so these register here
    /// rather than on each panel's own ViewModel.</summary>
    private void RegisterNavigationCommands(ICommandRegistry registry)
    {
        void Nav(string id, string title, string area, string shortcut, string panel) => registry.Register(new AppCommand(
            Id: id, Title: title, Area: area, Description: $"Open {title}.",
            Keywords: [area.ToLowerInvariant(), panel],
            Shortcut: shortcut,
            CanExecute: () => true,
            Execute: () => { ActivePanel = panel; return Task.CompletedTask; }));

        Nav("nav.chat", "Chat", "Chat", "Ctrl+1", "chat");
        Nav("nav.agent", "Agent", "Agent", "Ctrl+2", "agent");
        Nav("nav.rag", "RAG", "RAG", "Ctrl+3", "rag");
        Nav("nav.models", "Models", "Models", "Ctrl+4", "models");
        Nav("nav.services", "Services", "Services", "Ctrl+5", "services");
        Nav("nav.benchmarks", "Benchmarks", "Benchmarks", "", "benchmarks");
        Nav("nav.lab", "Lab", "Lab", "", "lab");
        Nav("nav.system", "System overview", "System", "", "system");
        Nav("nav.doctor", "Doctor", "Doctor", "", "doctor");
        Nav("nav.memories", "Memories", "Memory", "", "memories");
        Nav("nav.logs", "Logs", "System", "", "logs");
        Nav("nav.activity", "Activity", "Activity", "", "activity");
        Nav("nav.settings", "Settings", "Settings", "", "settings");

        registry.Register(new AppCommand(
            Id: "chat.new-conversation", Title: "New conversation", Area: "Chat",
            Description: "Start a new chat conversation.",
            Keywords: ["new", "chat", "clear"], Shortcut: "Ctrl+N",
            CanExecute: () => true,
            Execute: () => { ActivePanel = "chat"; Chat.NewConversation(); return Task.CompletedTask; }));

        registry.Register(new AppCommand(
            Id: "system.toggle-sidebar", Title: "Toggle sidebar", Area: "Chat",
            Description: "Show or hide the conversation list.",
            Keywords: ["sidebar", "conversations", "toggle"], Shortcut: "",
            CanExecute: () => true,
            Execute: () => { IsSidebarOpen = !IsSidebarOpen; return Task.CompletedTask; }));
    }

    /// <summary>doc 02 2.4: every RecallHit.Target kind must land somewhere real.</summary>
    private async Task NavigateToRecallHitAsync(RecallHit hit)
    {
        var target = hit.Target;
        if (!string.IsNullOrWhiteSpace(target.ConversationId))
        {
            ActivePanel = "chat";
            await Chat.LoadConversationAsync(target.ConversationId);
        }
        else if (!string.IsNullOrWhiteSpace(target.TaskId))
        {
            ActivePanel = "agent";
            await Agent.LoadTaskCommand.ExecuteAsync(target.TaskId);
        }
        else if (!string.IsNullOrWhiteSpace(target.MemoryId))
        {
            ActivePanel = "memories";
            Memories.SearchText = hit.Title;
            await Memories.SearchAsync();
        }
        else if (!string.IsNullOrWhiteSpace(target.DatasetId))
        {
            ActivePanel = "rag";
            var dataset = Rag.Datasets.FirstOrDefault(d => d.Id == target.DatasetId);
            if (dataset is not null) Rag.SelectedDataset = dataset;
        }
    }

    private void UpdateDoctorStatus()
    {
        var errs = Doctor.Checks.Count(c => c.Status == Hermaeus.Core.Models.DoctorCheckStatus.Error);
        var warns = Doctor.Checks.Count(c => c.Status == Hermaeus.Core.Models.DoctorCheckStatus.Warning);
        DoctorHasErrors = errs > 0;
        DoctorHasWarnings = errs == 0 && warns > 0;
        DoctorIsOk = errs == 0 && warns == 0 && Doctor.Checks.Count > 0;
    }

    /// <summary>
    /// r27 01 1.5: the phase breakdown of this view model's own startup work,
    /// so "viewmodels" stops being one opaque number that used to absorb a
    /// five-minute-capable server wait. Empty until <see cref="InitializeAsync"/>
    /// has run.
    /// </summary>
    public IReadOnlyList<StartupPhase> StartupPhases => _startupPhases;
    private readonly List<StartupPhase> _startupPhases = [];

    public async Task InitializeAsync()
    {
        _startupPhases.Clear();
        var phase = Stopwatch.StartNew();

        await LoadConversationsAsync();
        Settings.Reload();
        OnPropertyChanged(nameof(SetupIncomplete));
        OnPropertyChanged(nameof(ShowSetupResume));
        ShowQuickChat = Settings.ShowQuickChat;
        await LoadToastHistoryAsync();
        await Projects.EnsureLoadedAsync();
        Agent.ActiveProjectId = Projects.ActiveProject?.Id ?? string.Empty;
        Palette.SetActiveProject(Projects.ActiveProject?.Id ?? string.Empty, Projects.ActiveProject?.Name ?? string.Empty);
        _startupPhases.Add(new StartupPhase("conversations and projects", phase.ElapsedMilliseconds));

        if (!_settingsService.Settings.SetupWizardCompleted)
        {
            Wizard.LoadFromSettings(resetStep: true);
            ActivePanel = "wizard";
            return;
        }

        await CompletePostSetupInitializationAsync();
    }

    /// <summary>
    /// Everything that only makes sense once setup is complete: RAG/agent/
    /// benchmark data, auto-starting managed servers, and listing chat
    /// models. Called from the normal <see cref="InitializeAsync"/> path and
    /// from the <see cref="SetupWizardViewModel.WizardCompleted"/> handler
    /// (first-run path, r12 03-runtime-vm-correctness.md 3.1); the guard
    /// keeps a startup race between the two from double-running it.
    /// Each step is isolated (r12 3.2) so one failing store (a locked or
    /// corrupt RAG/benchmark database) cannot silently skip every later
    /// step.
    /// r27 01-startup-that-never-waits.md 1.1: the three store loads are
    /// independent and run concurrently, and auto-starting managed servers
    /// has left the awaited chain entirely. Listing models never depended on
    /// auto-start finishing: a server reaching Running raises
    /// <see cref="ServicesViewModel.ServerAvailabilityChanged"/>, which
    /// re-lists with force: true. The one direct call below covers a server
    /// that is ALREADY running (started externally, or while the app was
    /// closed) and will therefore never raise a status transition.
    /// </summary>
    private async Task CompletePostSetupInitializationAsync()
    {
        if (_postSetupInitialized) return;
        _postSetupInitialized = true;

        // Each concurrent step keeps its own RunBackgroundTaskCoreAsync wrapper
        // so every failure still names its own operation in the log, instead of
        // three steps collapsing into one WhenAll exception (r12 3.2).
        // r27 05 5.3: the block is timed as one wall-clock number with its parts
        // underneath, because three overlapping durations printed as a sequence
        // would not add up to anything.
        var block = Stopwatch.StartNew();
        var children = new List<StartupPhase>();
        await Task.WhenAll(
            TimedStepAsync(children, "RAG datasets", () => Rag.LoadDatasetsAsync()),
            TimedStepAsync(children, "agent", () => Agent.LoadAsync()),
            TimedStepAsync(children, "benchmarks", () => Benchmarks.LoadAsync()));
        _startupPhases.Add(new StartupPhase("stores", block.ElapsedMilliseconds,
            children.OrderBy(c => c.Name, StringComparer.Ordinal).ToList(), ChildrenRanConcurrently: true));

        var step = Stopwatch.StartNew();
        await RunBackgroundTaskCoreAsync("ensure Local API running state", () => Settings.EnsureLocalApiRunningStateAsync());
        _startupPhases.Add(new StartupPhase("local API state", step.ElapsedMilliseconds));

        step.Restart();
        await RunBackgroundTaskCoreAsync("load chat models", () => Chat.LoadModelsAsync());
        _startupPhases.Add(new StartupPhase("chat models", step.ElapsedMilliseconds));
        // Fire and forget: the same isolation and the same log line, without the
        // await. A model load behind a five-minute health deadline is no longer
        // between the user and the model dropdown.
        RunBackgroundTaskAsync("auto-start managed servers", () => Services.AutoStartAllAsync());
        RunBackgroundTaskAsync("run startup doctor scan", () => Doctor.RunStartupScanAsync());
        // r24 doc 02 2.1: shortly after startup, bounded, never on the send path.
        if (_recallIndexing is not null)
            RunBackgroundTaskAsync("recall startup backfill", RunRecallStartupBackfillAsync);
        // r24 doc 03 3.4: only if the user opted in; well after startup, never on the send path.
        if (_settingsService.Settings.Rag.RefreshWatchedSourcesOnStart || _settingsService.Settings.Rag.RefreshWatchedSourcesEveryHours > 0)
            RunBackgroundTaskAsync("watched sources automatic refresh", RunWatchedSourceAutomationAsync);
    }

    private async Task RunRecallStartupBackfillAsync()
    {
        // r27 05 5.1: the backfill takes the projection and reads full
        // conversations only for the ids it is actually going to index, rather
        // than deserialising every message of every conversation to discover
        // that most of them are already indexed.
        var summaries = await _store.GetSummariesAsync(includeArchived: true);
        var tasks = await Agent.BuildRecallTaskInputsAsync();
        await _recallIndexing!.RunStartupBackfillAsync(summaries, tasks, _store.GetByIdAsync);
    }

    /// <summary>doc 03 3.4: optional, off by default. Runs one on-start pass after a
    /// delay (well after startup, never on the send path) and then, if configured,
    /// repeats on an interval until shutdown. Never deletes - see
    /// RagViewModel.RunAutomaticWatchedRefreshAsync.</summary>
    private async Task RunWatchedSourceAutomationAsync()
    {
        _watchedRefreshCts = new CancellationTokenSource();
        var ct = _watchedRefreshCts.Token;

        if (_settingsService.Settings.Rag.RefreshWatchedSourcesOnStart)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
            catch (OperationCanceledException) { return; }
            await Rag.RunAutomaticWatchedRefreshAsync(ct);
        }

        var hours = _settingsService.Settings.Rag.RefreshWatchedSourcesEveryHours;
        while (hours > 0 && !ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromHours(hours), ct); }
            catch (OperationCanceledException) { return; }
            await Rag.RunAutomaticWatchedRefreshAsync(ct);
            hours = _settingsService.Settings.Rag.RefreshWatchedSourcesEveryHours;
        }
    }

    public void Shutdown()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        _watchedRefreshCts?.Cancel();
        _watchedRefreshCts?.Dispose();
        _watchedRefreshCts = null;
        Services.StopAll();
        Settings.Shutdown();
    }

    private async Task LoadConversationsAsync()
    {
        // r27 05 5.1: the sidebar shows titles, folders, tags and flags. It has
        // no business deserialising every message of every conversation, nor
        // walking them again to backfill parent links, to draw that.
        var convs = string.IsNullOrWhiteSpace(SearchQuery)
            ? await _store.GetSummariesAsync(ShowArchivedConversations)
            : await _store.SearchSummariesAsync(SearchQuery);

        RefreshFolderFilters(convs);
        if (SelectedFolderFilter != "All")
        {
            convs = convs
                .Where(c => string.Equals(c.Folder, SelectedFolderFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!ShowArchivedConversations)
            convs = convs.Where(c => !c.IsArchived).ToList();

        Conversations.Clear();
        foreach (var c in convs)
        {
            var item = ToItem(c);
            item.MetadataChanged += OnConversationMetadataChanged;
            Conversations.Add(item);
        }
    }

    /// <summary>
    /// Debounces per-keystroke edits in the conversation details flyout (title/folder/tags
    /// text boxes are TwoWay/PropertyChanged) so the store is written once after a pause in
    /// typing, not on every character (r18 01-finish-the-open-work.md 1.1).
    /// </summary>
    private void OnConversationMetadataChanged(ConversationItemViewModel item)
    {
        if (_pendingMetadataSaves.Remove(item.Id, out var existing))
            existing.Cancel();

        var cts = new CancellationTokenSource();
        _pendingMetadataSaves[item.Id] = cts;
        _ = DebouncedSaveMetadataAsync(item, cts.Token);
    }

    private async Task DebouncedSaveMetadataAsync(ConversationItemViewModel item, CancellationToken token)
    {
        try
        {
            await Task.Delay(MetadataSaveDebounce, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
            return;

        await SaveConversationMetadataAsync(item, showToast: false);
    }

    private void RefreshFolderFilters(IEnumerable<Hermaeus.Core.Models.ConversationSummary> convs)
    {
        var selected = SelectedFolderFilter;
        _refreshingFolderFilters = true;
        try
        {
            FolderFilters.Clear();
            FolderFilters.Add("All");
            foreach (var folder in convs.Select(c => c.Folder.Trim())
                         .Where(f => f.Length > 0)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                FolderFilters.Add(folder);
            }

            SelectedFolderFilter = FolderFilters.Contains(selected) ? selected : "All";
        }
        finally
        {
            _refreshingFolderFilters = false;
        }
    }

    private static ConversationItemViewModel ToItem(Hermaeus.Core.Models.ConversationSummary c) => new()
    {
        Id = c.Id,
        Title = c.Title,
        ModelId = c.ModelId,
        UpdatedAt = c.UpdatedAt,
        Folder = c.Folder,
        TagsText = string.Join(", ", c.Tags),
        IsPinned = c.IsPinned,
        IsArchived = c.IsArchived,
        IsRecallExcluded = c.RecallExcluded
    };

    [RelayCommand]
    private void NewConversation()
    {
        foreach (var c in Conversations) c.IsSelected = false;
        Chat.NewConversation();
        ActivePanel = "chat";
    }

    /// <summary>r21 3.3: reuses NewConversation (same path as Ctrl+N) so any unsent draft in
    /// the input box is handled exactly as that path already handles it, then attaches and
    /// immediately persists the dataset - a brand-new conversation has no messages yet, so
    /// nothing else would save the attachment until the user actually sends something.</summary>
    private async Task OpenDatasetInChatAsync(Hermaeus.Rag.Models.RagDataset dataset)
    {
        NewConversation();
        await Chat.AttachRagDatasetAndPersistAsync(dataset);
    }

    [RelayCommand]
    private async Task SelectConversationAsync(ConversationItemViewModel item)
    {
        foreach (var c in Conversations) c.IsSelected = false;
        item.IsSelected = true;
        ActivePanel = "chat";
        await Chat.LoadConversationAsync(item.Id);
    }

    /// <summary>
    /// Set by the view: shows <c>ConfirmActionDialog</c> with the
    /// conversation's title (r16 03-workbench-and-desktop.md 3.3). Every
    /// other destructive action of this weight (RAG dataset delete, reindex,
    /// benchmark history clear, backup restore) is confirm-gated; a raw
    /// context-menu click was the one exception.
    /// </summary>
    public Func<ConversationItemViewModel, Task<bool>>? RequestDeleteConversationConfirmation { get; set; }

    [RelayCommand]
    private async Task DeleteConversationAsync(ConversationItemViewModel item)
    {
        var confirmed = RequestDeleteConversationConfirmation is not null
            && await RequestDeleteConversationConfirmation(item);
        if (!confirmed)
            return;

        await _store.DeleteAsync(item.Id);
        Conversations.Remove(item);
        if (Chat.CurrentConversationId == item.Id) Chat.NewConversation();
        // r24 doc 02 2.0: deletion propagates, always - a record that survives
        // its own source is treated as a bug of the highest severity in that doc.
        if (_recallIndexing is not null)
            _ = Task.Run(() => _recallIndexing.RemoveConversationAsync(item.Id));
        _toasts.Show("Conversation deleted", $"\"{item.Title}\" was removed.", ToastKind.Info);
    }

    [RelayCommand]
    private async Task ToggleRecallExclusionAsync(ConversationItemViewModel item)
    {
        item.IsRecallExcluded = !item.IsRecallExcluded;
        await SaveConversationMetadataAsync(item, showToast: false);
        _toasts.Show(item.IsRecallExcluded ? "Excluded from Recall" : "Included in Recall",
            $"\"{item.Title}\" was {(item.IsRecallExcluded ? "excluded from" : "re-included in")} Recall.",
            ToastKind.Info);
    }

    [RelayCommand]
    private async Task RenameConversationAsync(ConversationItemViewModel item)
    {
        var title = string.IsNullOrWhiteSpace(item.Title)
            ? "New Conversation"
            : item.Title.Trim();
        item.Title = title;

        var conv = await _store.GetByIdAsync(item.Id);
        if (conv is null) return;

        conv.Title = title;
        await _store.SaveAsync(conv);
        item.UpdatedAt = conv.UpdatedAt;

        if (Chat.CurrentConversationId == item.Id)
            Chat.ConversationTitle = title;

        _toasts.Show("Conversation renamed", $"Saved as \"{title}\".", ToastKind.Success);
    }

    [RelayCommand]
    private async Task SaveConversationMetadataAsync(ConversationItemViewModel item)
    {
        await SaveConversationMetadataAsync(item, showToast: true);
    }

    private async Task SaveConversationMetadataAsync(ConversationItemViewModel item, bool showToast)
    {
        if (_pendingMetadataSaves.Remove(item.Id, out var pending))
            pending.Cancel();

        var conv = await _store.GetByIdAsync(item.Id);
        if (conv is null) return;

        conv.Title = string.IsNullOrWhiteSpace(item.Title) ? "New Conversation" : item.Title.Trim();
        conv.Folder = item.Folder.Trim();
        conv.Tags = item.Tags;
        conv.IsPinned = item.IsPinned;
        conv.IsArchived = item.IsArchived;
        conv.RecallExcluded = item.IsRecallExcluded;
        await _store.SaveAsync(conv);
        if (_recallIndexing is not null)
            _ = Task.Run(() => _recallIndexing.IndexConversationAsync(conv));

        // In-place update only: a full LoadConversationsAsync() reload here would replace every
        // ConversationItemViewModel instance out from under the open details flyout on each save
        // (r18 01-finish-the-open-work.md 1.1). Reserve the full reload for actions that change
        // list membership or ordering (delete, new conversation, search/filter changes).
        item.Title = conv.Title;
        item.Folder = conv.Folder;
        item.TagsText = string.Join(", ", conv.Tags);
        item.IsPinned = conv.IsPinned;
        item.IsArchived = conv.IsArchived;
        item.UpdatedAt = conv.UpdatedAt;

        if (showToast)
            _toasts.Show("Conversation details saved", $"Updated \"{conv.Title}\".", ToastKind.Success);
    }

    [RelayCommand]
    private async Task TogglePinConversationAsync(ConversationItemViewModel item)
    {
        item.IsPinned = !item.IsPinned;
        await SaveConversationMetadataAsync(item, showToast: false);
        // Pinning changes ordering, so this is one of the membership/ordering actions that
        // still warrants a full reload (r18 01-finish-the-open-work.md 1.1).
        await LoadConversationsAsync();
        _toasts.Show(item.IsPinned ? "Conversation pinned" : "Conversation unpinned",
            $"\"{item.Title}\" was {(item.IsPinned ? "pinned" : "unpinned")}.",
            ToastKind.Info);
    }

    [RelayCommand]
    private async Task ToggleArchiveConversationAsync(ConversationItemViewModel item)
    {
        item.IsArchived = !item.IsArchived;
        if (item.IsArchived)
            item.IsPinned = false;
        await SaveConversationMetadataAsync(item, showToast: false);
        // Archiving changes list membership when archived conversations are hidden, so this
        // still warrants a full reload (r18 01-finish-the-open-work.md 1.1).
        await LoadConversationsAsync();
        _toasts.Show(item.IsArchived ? "Conversation archived" : "Conversation restored",
            $"\"{item.Title}\" was {(item.IsArchived ? "archived" : "restored")}.",
            ToastKind.Info);
    }

    [RelayCommand]
    private async Task ExportConversationMarkdownAsync(ConversationItemViewModel? item) =>
        await ExportConversationAsync(item, ConversationExportFormat.Markdown);

    [RelayCommand]
    private async Task ExportConversationJsonAsync(ConversationItemViewModel? item) =>
        await ExportConversationAsync(item, ConversationExportFormat.Json);

    private async Task ExportConversationAsync(ConversationItemViewModel? item, ConversationExportFormat format)
    {
        if (item is null) return;
        try
        {
            var conversation = await _store.GetByIdAsync(item.Id);
            if (conversation is null)
            {
                _toasts.Show("Export failed", "Conversation was not found.", ToastKind.Error);
                return;
            }

            var ext = format == ConversationExportFormat.Json ? "json" : "md";
            var dir = Path.Combine(SettingsService.ResolveDataRoot(_settingsService.Settings), "exports", "conversations");
            var path = Path.Combine(dir, $"conversation-{SanitizeFileName(conversation.Title)}-{DateTime.UtcNow:yyyyMMddHHmmss}.{ext}");
            await _exports.ExportAsync(conversation, path, format);
            _toasts.Show("Conversation exported", path, ToastKind.Success, 7000);
        }
        catch (Exception ex)
        {
            _toasts.Show("Export failed", ex.Message, ToastKind.Error, 7000);
        }
    }

    private static string SanitizeFileName(string value)
    {
        var clean = string.Join("-", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim('-', ' ');
        return string.IsNullOrWhiteSpace(clean) ? "conversation" : clean;
    }

    [RelayCommand] private void ToggleSidebar()       => IsSidebarOpen = !IsSidebarOpen;
    [RelayCommand] private void ToggleQuickChat()     => ShowQuickChat = !ShowQuickChat;
    public void HideQuickChat() => ShowQuickChat = false;
    public void ToggleQuickChatSurface() => ShowQuickChat = !ShowQuickChat;
    public void OpenNewConversation() => NewConversation();
    public void OpenServicesPanel() => ShowServicesPanel();
    [RelayCommand] private void ShowChatPanel()
    {
        ActivePanel = "chat";
        RunBackgroundTaskAsync("load chat models", () => Chat.LoadModelsAsync());
    }
    [RelayCommand] private void ShowAgentPanel()
    {
        ActivePanel = "agent";
        RunBackgroundTaskAsync("load agent", () => Agent.LoadAsync());
    }
    [RelayCommand] private void ShowRagPanel()         => ActivePanel = "rag";
    [RelayCommand] private void ShowModelsPanel()      { ActivePanel = "models"; RunBackgroundTaskAsync("refresh models panel", () => Models.RefreshCommand.ExecuteAsync(null)); }
    // r29 doc 01 1.1: Services can now persist the whole settings object, so it
    // needs the same guarantee the Settings page has - every section view model
    // holds current state when Save is pressed. Without this, a section left
    // stale by another panel's direct save (model profiles, projects, the setup
    // wizard) would be written back over the real value.
    [RelayCommand] private void ShowServicesPanel()    { ActivePanel = "services"; Settings.Reload(); }
    [RelayCommand] private void ShowBenchmarksPanel()  { ActivePanel = "benchmarks"; RunBackgroundTaskAsync("load benchmarks panel", () => Benchmarks.LoadCommand.ExecuteAsync(null)); }
    [RelayCommand] private void ShowLabPanel()         { ActivePanel = "lab"; RunBackgroundTaskAsync("load lab evidence", Lab.RefreshAsync); }
    [RelayCommand] private void ShowSystemPanel()      { ActivePanel = "system"; RunBackgroundTaskAsync("refresh system panel", () => SystemOverview.RefreshCommand.ExecuteAsync(null)); }
    [RelayCommand] private void ShowDoctorPanel()
    {
        ActivePanel = "doctor";
        RunBackgroundTaskAsync("run doctor scan", () => Doctor.ScanCommand.ExecuteAsync(null));
    }
    [RelayCommand] private void ShowMemoriesPanel()
    {
        ActivePanel = "memories";
        RunBackgroundTaskAsync("load memories", () => Memories.InitializeCommand.ExecuteAsync(null));
    }
    [RelayCommand] private void ShowLogsPanel()        => ActivePanel = "logs";
    [RelayCommand] private void ShowActivityPanel()    { ActivePanel = "activity"; RunBackgroundTaskAsync("refresh activity", Activity.RefreshAsync); }
    [RelayCommand] private void ResumeSetup()           => ActivePanel = "wizard";
    [RelayCommand]
    private void ShowWizardPanel()
    {
        if (!SetupIncomplete)
            Wizard.LoadFromSettings(resetStep: true);
        ActivePanel = "wizard";
    }
    [RelayCommand] private void ShowSettingsPanel()    { ActivePanel = "settings"; Settings.Reload(); }

    [RelayCommand]
    private void ToggleToastHistory()
    {
        ShowToastHistory = !ShowToastHistory;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        await LoadConversationsAsync();
    }

    private void OnConversationSaved(object? sender, string convId)
    {
        var existing = Conversations.FirstOrDefault(c => c.Id == convId);
        if (existing is not null)
        {
            existing.Title = Chat.ConversationTitle;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.ModelId = Chat.SelectedModel?.Id ?? existing.ModelId;
            var idx = Conversations.IndexOf(existing);
            if (idx > 0) Conversations.Move(idx, 0);
        }
        else
        {
            var item = new ConversationItemViewModel
            {
                Id = convId, Title = Chat.ConversationTitle,
                ModelId = Chat.SelectedModel?.Id ?? string.Empty,
                UpdatedAt = DateTime.UtcNow, IsSelected = true,
                Folder = string.Empty
            };
            Conversations.Insert(0, item);
            foreach (var c in Conversations.Skip(1)) c.IsSelected = false;
        }
    }

    partial void OnActivePanelChanged(string value)
    {
        OnPropertyChanged(nameof(ShowChat));
        OnPropertyChanged(nameof(ShowAgent));
        OnPropertyChanged(nameof(ShowSettings));
        OnPropertyChanged(nameof(ShowModels));
        OnPropertyChanged(nameof(ShowRag));
        OnPropertyChanged(nameof(ShowServices));
        OnPropertyChanged(nameof(ShowBenchmarks));
        OnPropertyChanged(nameof(ShowLab));
        OnPropertyChanged(nameof(ShowSystem));
        OnPropertyChanged(nameof(ShowDoctor));
        OnPropertyChanged(nameof(ShowMemories));
        OnPropertyChanged(nameof(ShowLogs));
        // ShowActivity was missing from this list since the property was added,
        // so anything binding to it never refreshed on navigation.
        OnPropertyChanged(nameof(ShowActivity));
        OnPropertyChanged(nameof(ShowWizard));
        OnPropertyChanged(nameof(ShowSetupResume));
        OnPropertyChanged(nameof(ActiveViewModel));
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnSearchQueryChanged(string value)
    {
        // Debounce search to reduce DB hits when typing
        try
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(300, token);
                    if (token.IsCancellationRequested) return;
                    await RunOnUiAsync(() => LoadConversationsAsync());
                }
                catch (TaskCanceledException) { }
            }, token);
        }
        catch
        {
            _ = LoadConversationsAsync();
        }
    }

    partial void OnSelectedFolderFilterChanged(string value)
    {
        if (!_refreshingFolderFilters)
            _ = LoadConversationsAsync();
    }

    partial void OnShowArchivedConversationsChanged(bool value)
    {
        _ = LoadConversationsAsync();
    }

    private void OnToastRaised(ToastMessage toast)
    {
        if (toast.Kind == ToastKind.Error)
        {
            _logs.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Error,
                RuntimeLogCategory.Service,
                $"{toast.Title}: {toast.Message}"));
        }

        var vm = new ToastViewModel
        {
            Title = toast.Title,
            Message = toast.Message,
            Timestamp = DateTime.UtcNow,
            Kind = toast.Kind,
            DurationMs = toast.DurationMs,
            CanCopyDetails = toast.CanCopyDetails
        };
        const int MaxVisibleToasts = 5;
        RunOnUi(() =>
        {
            if (Toasts.Count >= MaxVisibleToasts)
            {
                // drop the oldest visible toast to keep the UI capped
                Toasts.RemoveAt(0);
            }
            Toasts.Add(vm);
        });
        _ = RemoveToastLaterAsync(vm);
        // Add to persistent history and save from inside the same posted
        // block (r12 02-async-and-threading.md 2.1): a toast raised from a
        // background thread must not let the save's enumeration of
        // ToastHistory race the posted Insert, and the save must see this
        // toast, not the pre-mutation list.
        _ = MutateAndSaveHistoryAsync(() => ToastHistory.Insert(0, vm));
    }

    /// <summary>
    /// Runs <paramref name="mutate"/> and the resulting history save as one
    /// unit on the UI thread (inline if already there, otherwise posted): the
    /// mutation and the snapshot the save serializes can never be split
    /// across a scheduling gap (r12 02-async-and-threading.md 2.1).
    /// </summary>
    private Task MutateAndSaveHistoryAsync(Action mutate) => RunOnUiAsync(() =>
    {
        mutate();
        return SaveToastHistoryAsync();
    });

    private async Task LoadToastHistoryAsync()
    {
        try
        {
            var root = Hermaeus.Services.SettingsService.ResolveDataRoot(_settingsService.Settings);
            var path = Path.Combine(root, "toasts.json");
            if (!File.Exists(path)) return;
            var json = await File.ReadAllTextAsync(path);
            var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var list = System.Text.Json.JsonSerializer.Deserialize<List<ToastHistoryEntry>>(json, opts);
            if (list is null) return;
            RunOnUi(() =>
            {
                ToastHistory.Clear();
                foreach (var e in list.OrderByDescending(t => t.Timestamp))
                    ToastHistory.Add(new ToastViewModel { Title = e.Title, Message = e.Message, Kind = e.Kind, DurationMs = e.DurationMs, Timestamp = e.Timestamp, CanCopyDetails = e.CanCopyDetails });
                // cap size
                TrimToastHistory();
            });
        }
        catch (Exception ex)
        {
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Service,
                $"Could not load toast history: {ex.Message}"));
        }
    }

    private async Task SaveToastHistoryAsync()
    {
        try
        {
            var root = Hermaeus.Services.SettingsService.ResolveDataRoot(_settingsService.Settings);
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "toasts.json");
            var list = ToastHistory.Select(t => new ToastHistoryEntry { Title = t.Title, Message = t.Message, Kind = t.Kind, DurationMs = t.DurationMs, Timestamp = t.Timestamp, CanCopyDetails = t.CanCopyDetails }).ToList();
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            var json = System.Text.Json.JsonSerializer.Serialize(list, opts);
            await WriteTextAtomicAsync(path, json);
        }
        catch (Exception ex)
        {
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Service,
                $"Could not save toast history: {ex.Message}"));
        }
    }

    private void TrimToastHistory()
    {
        const int MaxHistory = 200;
        while (ToastHistory.Count > MaxHistory)
            ToastHistory.RemoveAt(ToastHistory.Count - 1);
    }

    private static async Task WriteTextAtomicAsync(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temp, content);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch
            {
            }
        }
    }

    [RelayCommand]
    private async Task DismissToastAsync(ToastViewModel? item)
    {
        if (item is null) return;
        await MutateAndSaveHistoryAsync(() =>
        {
            ToastHistory.Remove(item);
            TrimToastHistory();
        });
    }

    [RelayCommand]
    private async Task ClearToastHistoryAsync() => await MutateAndSaveHistoryAsync(() => ToastHistory.Clear());

    [RelayCommand]
    private void CopyToastDetails(ToastViewModel? item)
    {
        if (item is { CanCopyDetails: true })
            RequestCopyToastDetails?.Invoke(item.Message);
    }

    private sealed class ToastHistoryEntry
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public ToastKind Kind { get; set; }
        public int DurationMs { get; set; }
        public DateTime Timestamp { get; set; }
        public bool CanCopyDetails { get; set; }
    }

    private async Task RemoveToastLaterAsync(ToastViewModel toast)
    {
        await Task.Delay(toast.DurationMs);
        RunOnUi(() => Toasts.Remove(toast));
    }

    private async Task RefreshModelsAfterServerChangeAsync()
    {
        try
        {
            await RunOnUiAsync(() => Chat.LoadModelsAsync(force: true));
        }
        catch (Exception ex)
        {
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Error, RuntimeLogCategory.Service,
                $"Model refresh failed: {ex.Message}"));
            _toasts.Show("Model refresh failed", ex.Message, ToastKind.Warning, 7000);
        }
    }

    /// <summary>
    /// One concurrent startup step: same isolation and same log line as every
    /// other step (r12 3.2), with its own wall-clock duration recorded for 1.5.
    /// </summary>
    private async Task TimedStepAsync(List<StartupPhase> into, string name, Func<Task> action)
    {
        var sw = Stopwatch.StartNew();
        await RunBackgroundTaskCoreAsync($"load {name}", action);
        var elapsed = sw.ElapsedMilliseconds;
        lock (into)
            into.Add(new StartupPhase(name, elapsed));
    }

    private void RunBackgroundTaskAsync(string operation, Func<Task> action)
    {
        _ = RunBackgroundTaskCoreAsync(operation, action);
    }

    private async Task RunBackgroundTaskCoreAsync(string operation, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logs.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Error, RuntimeLogCategory.Service,
                $"{operation} failed: {ex.Message}"));
            _toasts.Show("Background task failed", $"{operation}: {ex.Message}", ToastKind.Warning, 7000);
        }
    }
}
