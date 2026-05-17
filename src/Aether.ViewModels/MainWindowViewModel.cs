using System.Collections.ObjectModel;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IConversationStore _store;
    private readonly IToastService _toasts;
    private readonly IRuntimeLogService _logs;
    private readonly IConversationExportService _exports;
    private readonly SynchronizationContext? _sync;
    private CancellationTokenSource? _searchCts;
    private readonly ISettingsService _settingsService;
    private bool _refreshingFolderFilters;

    public ChatViewModel            Chat     { get; }
    public AgentViewModel           Agent    { get; }
    public SettingsViewModel        Settings { get; }
    public ModelManagementViewModel Models   { get; }
    public RagViewModel             Rag      { get; }
    public ServicesViewModel        Services { get; }
    public TasksViewModel           Tasks    { get; }
    public BenchmarkViewModel       Benchmarks { get; }
    public SystemOverviewViewModel  SystemOverview { get; }
    public DoctorViewModel          Doctor { get; }
    public MemoriesViewModel        Memories { get; }
    public LogsViewModel            Logs { get; }
    public SessionUsageViewModel    SessionUsage { get; }
    public SessionUsageDetailViewModel SessionUsageDetail { get; }
    public SetupWizardViewModel     Wizard { get; }

    public ObservableCollection<ConversationItemViewModel> Conversations { get; } = [];
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];
    public ObservableCollection<ToastViewModel> ToastHistory { get; } = [];
    public ObservableCollection<string> FolderFilters { get; } = ["All"];

    [ObservableProperty] private bool   _isSidebarOpen = true;
    [ObservableProperty] private string _searchQuery   = string.Empty;
    [ObservableProperty] private string _activePanel   = "chat";
    [ObservableProperty] private bool   _isLoading;
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
    public bool ShowTasks    => ActivePanel == "tasks";
    public bool ShowBenchmarks => ActivePanel == "benchmarks";
    public bool ShowSystem => ActivePanel == "system";
    public bool ShowDoctor => ActivePanel == "doctor";
    public bool ShowMemories => ActivePanel == "memories";
    public bool ShowSessionUsage => ActivePanel == "session-usage";
    public bool ShowLogs => ActivePanel == "logs";
    public bool ShowWizard => ActivePanel == "wizard";
    public object ActiveViewModel => ActivePanel switch
    {
        "settings" => Settings,
        "agent"    => Agent,
        "models"   => Models,
        "rag"      => Rag,
        "services" => Services,
        "tasks"    => Tasks,
        "benchmarks" => Benchmarks,
        "system"   => SystemOverview,
        "doctor"   => Doctor,
        "memories" => Memories,
        "session-usage" => SessionUsage,
        "logs"     => Logs,
        "wizard"   => Wizard,
        _          => Chat
    };

    public string WindowTitle
        => ShowChat ? $"Aether - {Chat.ConversationTitle}" : $"Aether - {ActivePanel?.ToUpperInvariant() ?? string.Empty}";

    public MainWindowViewModel(
        IConversationStore store,
        ChatViewModel chat,
        AgentViewModel agent,
        SettingsViewModel settings,
        ModelManagementViewModel models,
        RagViewModel rag,
        ServicesViewModel services,
        TasksViewModel tasks,
        BenchmarkViewModel benchmarks,
        SystemOverviewViewModel systemOverview,
        DoctorViewModel doctor,
        MemoriesViewModel memories,
        LogsViewModel logs,
        SessionUsageViewModel sessionUsage,
        SessionUsageDetailViewModel sessionUsageDetail,
        SetupWizardViewModel wizard,
        ISettingsService settingsService,
        IToastService toasts,
        IRuntimeLogService runtimeLogs,
        IConversationExportService exports)
    {
        _sync = SynchronizationContext.Current;
        _toasts = toasts;
        _logs = runtimeLogs;
        _exports = exports;
        _settingsService = settingsService;
        _store = store; Chat = chat; Agent = agent; Settings = settings;
        Models = models; Rag = rag; Services = services; Tasks = tasks;
        Benchmarks = benchmarks; SystemOverview = systemOverview; Doctor = doctor; Memories = memories; Logs = logs; SessionUsage = sessionUsage; Wizard = wizard;
        SessionUsageDetail = sessionUsageDetail;
        SessionUsage.RequestOpenDetail += (id, title) => ShowSessionUsageDetailPanel(id, title);
        Doctor.RequestNavigate = panel => ActivePanel = panel;
        // Keep toolbar doctor badge in sync with doctor checks
        Doctor.Checks.CollectionChanged += (_, _) => UpdateDoctorStatus();
        UpdateDoctorStatus();
        Wizard.WizardCompleted += () => ActivePanel = "chat";
        // allow settings view to request re-running the setup wizard
        Settings.RequestShowSetupWizard = () => ActivePanel = "wizard";
        Chat.PropertyChanged += (s, e) => { if (e.PropertyName == "ConversationTitle") OnPropertyChanged(nameof(WindowTitle)); };
        Chat.ConversationSaved += OnConversationSaved;
        Services.ServerAvailabilityChanged += (_, _) => RunBackgroundTaskAsync("refresh models after server availability change", RefreshModelsAfterServerChangeAsync);
        _toasts.ToastRaised += OnToastRaised;
        
    }

    private void UpdateDoctorStatus()
    {
        var errs = Doctor.Checks.Count(c => c.Status == Aether.Core.Models.DoctorCheckStatus.Error);
        var warns = Doctor.Checks.Count(c => c.Status == Aether.Core.Models.DoctorCheckStatus.Warning);
        DoctorHasErrors = errs > 0;
        DoctorHasWarnings = errs == 0 && warns > 0;
        DoctorIsOk = errs == 0 && warns == 0 && Doctor.Checks.Count > 0;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            await LoadConversationsAsync();
            Settings.Reload();
            ShowQuickChat = Settings.ShowQuickChat;
            await LoadToastHistoryAsync();
            if (!_settingsService.Settings.SetupWizardCompleted)
            {
                ActivePanel = "wizard";
                return;
            }

            await Rag.LoadDatasetsAsync();
            await Agent.LoadAsync();
            await Services.AutoStartAllAsync();
            await Chat.LoadModelsAsync();
        }
        finally { IsLoading = false; }
    }

    public void Shutdown()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        Services.StopAll();
        Settings.Shutdown();
    }

    private async Task LoadConversationsAsync()
    {
        var convs = string.IsNullOrWhiteSpace(SearchQuery)
            ? await _store.GetAllAsync(ShowArchivedConversations)
            : await _store.SearchAsync(SearchQuery);

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
            Conversations.Add(ToItem(c));
    }

    private void RefreshFolderFilters(IEnumerable<Aether.Core.Models.Conversation> convs)
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

    private static ConversationItemViewModel ToItem(Aether.Core.Models.Conversation c) => new()
    {
        Id = c.Id,
        Title = c.Title,
        ModelId = c.ModelId,
        UpdatedAt = c.UpdatedAt,
        SystemPrompt = c.SystemPrompt,
        Folder = c.Folder,
        TagsText = string.Join(", ", c.Tags),
        IsPinned = c.IsPinned,
        IsArchived = c.IsArchived
    };

    [RelayCommand]
    private void NewConversation()
    {
        foreach (var c in Conversations) c.IsSelected = false;
        Chat.NewConversation();
        ActivePanel = "chat";
    }

    [RelayCommand]
    private async Task SelectConversationAsync(ConversationItemViewModel item)
    {
        foreach (var c in Conversations) c.IsSelected = false;
        item.IsSelected = true;
        ActivePanel = "chat";
        await Chat.LoadConversationAsync(item.Id);
    }

    [RelayCommand]
    private async Task DeleteConversationAsync(ConversationItemViewModel item)
    {
        await _store.DeleteAsync(item.Id);
        Conversations.Remove(item);
        if (Chat.CurrentConversationId == item.Id) Chat.NewConversation();
        _toasts.Show("Conversation deleted", $"\"{item.Title}\" was removed.", ToastKind.Info);
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
        var conv = await _store.GetByIdAsync(item.Id);
        if (conv is null) return;

        conv.Title = string.IsNullOrWhiteSpace(item.Title) ? "New Conversation" : item.Title.Trim();
        conv.Folder = item.Folder.Trim();
        conv.Tags = item.Tags;
        conv.IsPinned = item.IsPinned;
        conv.IsArchived = item.IsArchived;
        await _store.SaveAsync(conv);

        item.Title = conv.Title;
        item.UpdatedAt = conv.UpdatedAt;
        await LoadConversationsAsync();
        if (showToast)
            _toasts.Show("Conversation details saved", $"Updated \"{conv.Title}\".", ToastKind.Success);
    }

    [RelayCommand]
    private async Task TogglePinConversationAsync(ConversationItemViewModel item)
    {
        item.IsPinned = !item.IsPinned;
        await SaveConversationMetadataAsync(item, showToast: false);
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
    [RelayCommand] private void ShowServicesPanel()    => ActivePanel = "services";
    [RelayCommand] private void ShowTasksPanel()       { Tasks.Reload(); ActivePanel = "tasks"; }
    [RelayCommand] private void ShowBenchmarksPanel()  { ActivePanel = "benchmarks"; RunBackgroundTaskAsync("load benchmarks panel", () => Benchmarks.LoadCommand.ExecuteAsync(null)); }
    [RelayCommand] private void ShowSystemPanel()      { ActivePanel = "system"; RunBackgroundTaskAsync("refresh system panel", () => SystemOverview.RefreshCommand.ExecuteAsync(null)); }
    [RelayCommand] private void ShowDoctorPanel()
    {
        ActivePanel = "doctor";
        RunBackgroundTaskAsync("run doctor scan", () => Doctor.ScanCommand.ExecuteAsync(null));
    }
    [RelayCommand] private void ShowMemoriesPanel()    => ActivePanel = "memories";
    [RelayCommand] private void ShowSessionUsagePanel()
    {
        ActivePanel = "session-usage";
        RunBackgroundTaskAsync("load session usage", () => SessionUsage.RefreshCommand.ExecuteAsync(null));
    }

    private void ShowSessionUsageDetailPanel(string conversationId, string? title = null)
    {
        ActivePanel = "session-usage-detail";
        RunBackgroundTaskAsync("load session usage detail", () => SessionUsageDetail.LoadForConversationAsync(conversationId, title ?? "(untitled)"));
    }
    [RelayCommand] private void ShowLogsPanel()        => ActivePanel = "logs";
    [RelayCommand] private void ShowWizardPanel()      => ActivePanel = "wizard";
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
            if (!string.IsNullOrWhiteSpace(item.Folder))
                _ = SaveConversationMetadataAsync(item);
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
        OnPropertyChanged(nameof(ShowTasks));
        OnPropertyChanged(nameof(ShowBenchmarks));
        OnPropertyChanged(nameof(ShowSystem));
        OnPropertyChanged(nameof(ShowDoctor));
        OnPropertyChanged(nameof(ShowMemories));
        OnPropertyChanged(nameof(ShowLogs));
        OnPropertyChanged(nameof(ShowWizard));
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
        var vm = new ToastViewModel
        {
            Title = toast.Title,
            Message = toast.Message,
            Timestamp = DateTime.UtcNow,
            Kind = toast.Kind,
            DurationMs = toast.DurationMs
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
        // add to persistent history and save
        RunOnUi(() => ToastHistory.Insert(0, vm));
        _ = SaveToastHistoryAsync();
    }

    private async Task LoadToastHistoryAsync()
    {
        try
        {
            var root = Aether.Services.SettingsService.ResolveDataRoot(_settingsService.Settings);
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
                    ToastHistory.Add(new ToastViewModel { Title = e.Title, Message = e.Message, Kind = e.Kind, DurationMs = e.DurationMs, Timestamp = e.Timestamp });
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
            var root = Aether.Services.SettingsService.ResolveDataRoot(_settingsService.Settings);
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "toasts.json");
            var list = ToastHistory.Select(t => new ToastHistoryEntry { Title = t.Title, Message = t.Message, Kind = t.Kind, DurationMs = t.DurationMs, Timestamp = t.Timestamp }).ToList();
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
        RunOnUi(() => ToastHistory.Remove(item));
        TrimToastHistory();
        await SaveToastHistoryAsync();
    }

    [RelayCommand]
    private async Task ClearToastHistoryAsync()
    {
        RunOnUi(() => ToastHistory.Clear());
        await SaveToastHistoryAsync();
    }

    private sealed class ToastHistoryEntry
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public ToastKind Kind { get; set; }
        public int DurationMs { get; set; }
        public DateTime Timestamp { get; set; }
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

    private void RunOnUi(Action action)
    {
        if (_sync is null)
            action();
        else
            _sync.Post(_ => action(), null);
    }

    private Task RunOnUiAsync(Func<Task> action)
    {
        if (_sync is null)
            return action();

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sync.Post(async _ =>
        {
            try
            {
                await action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, null);
        return tcs.Task;
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
