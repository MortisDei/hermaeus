using System.Collections.ObjectModel;
using Aether.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IConversationStore _store;
    private readonly IToastService _toasts;
    private readonly SynchronizationContext? _sync;
    private bool _refreshingFolderFilters;

    public ChatViewModel            Chat     { get; }
    public SettingsViewModel        Settings { get; }
    public ModelManagementViewModel Models   { get; }
    public RagViewModel             Rag      { get; }
    public ServicesViewModel        Services { get; }
    public TasksViewModel           Tasks    { get; }
    public BenchmarkViewModel       Benchmarks { get; }
    public SystemOverviewViewModel  SystemOverview { get; }

    public ObservableCollection<ConversationItemViewModel> Conversations { get; } = [];
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];
    public ObservableCollection<string> FolderFilters { get; } = ["All"];

    [ObservableProperty] private bool   _isSidebarOpen = true;
    [ObservableProperty] private string _searchQuery   = string.Empty;
    [ObservableProperty] private string _activePanel   = "chat";
    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _selectedFolderFilter = "All";
    [ObservableProperty] private bool   _showArchivedConversations;
    [ObservableProperty] private bool   _showQuickChat;

    public bool ShowChat     => ActivePanel == "chat";
    public bool ShowSettings => ActivePanel == "settings";
    public bool ShowModels   => ActivePanel == "models";
    public bool ShowRag      => ActivePanel == "rag";
    public bool ShowServices => ActivePanel == "services";
    public bool ShowTasks    => ActivePanel == "tasks";
    public bool ShowBenchmarks => ActivePanel == "benchmarks";
    public bool ShowSystem => ActivePanel == "system";
    public object ActiveViewModel => ActivePanel switch
    {
        "settings" => Settings,
        "models"   => Models,
        "rag"      => Rag,
        "services" => Services,
        "tasks"    => Tasks,
        "benchmarks" => Benchmarks,
        "system"   => SystemOverview,
        _          => Chat
    };

    public MainWindowViewModel(
        IConversationStore store,
        ChatViewModel chat,
        SettingsViewModel settings,
        ModelManagementViewModel models,
        RagViewModel rag,
        ServicesViewModel services,
        TasksViewModel tasks,
        BenchmarkViewModel benchmarks,
        SystemOverviewViewModel systemOverview,
        IToastService toasts)
    {
        _sync = SynchronizationContext.Current;
        _toasts = toasts;
        _store = store; Chat = chat; Settings = settings;
        Models = models; Rag = rag; Services = services; Tasks = tasks;
        Benchmarks = benchmarks; SystemOverview = systemOverview;
        Chat.ConversationSaved += OnConversationSaved;
        Services.ServerAvailabilityChanged += (_, _) => _ = RefreshModelsAfterServerChangeAsync();
        _toasts.ToastRaised += OnToastRaised;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            await LoadConversationsAsync();
            ShowQuickChat = Settings.ShowQuickChat;
            await Rag.LoadDatasetsAsync();
            await Services.AutoStartAllAsync();
            await Chat.LoadModelsAsync();
        }
        finally { IsLoading = false; }
    }

    public void Shutdown()
    {
        Services.StopAll();
        Settings.Shutdown();
    }

    private async Task LoadConversationsAsync()
    {
        var convs = string.IsNullOrWhiteSpace(SearchQuery)
            ? await _store.GetAllAsync()
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
        _refreshingFolderFilters = false;
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

    [RelayCommand] private void ToggleSidebar()       => IsSidebarOpen = !IsSidebarOpen;
    [RelayCommand] private void ToggleQuickChat()     => ShowQuickChat = !ShowQuickChat;
    [RelayCommand] private async Task ShowChatPanelAsync()
    {
        ActivePanel = "chat";
        await Chat.LoadModelsAsync();
    }
    [RelayCommand] private void ShowRagPanel()         => ActivePanel = "rag";
    [RelayCommand] private void ShowModelsPanel()      { ActivePanel = "models"; _ = Models.RefreshCommand.ExecuteAsync(null); }
    [RelayCommand] private void ShowServicesPanel()    => ActivePanel = "services";
    [RelayCommand] private void ShowTasksPanel()       { Tasks.Reload(); ActivePanel = "tasks"; }
    [RelayCommand] private void ShowBenchmarksPanel()  { ActivePanel = "benchmarks"; _ = Benchmarks.LoadCommand.ExecuteAsync(null); }
    [RelayCommand] private void ShowSystemPanel()      { ActivePanel = "system"; _ = SystemOverview.RefreshCommand.ExecuteAsync(null); }
    [RelayCommand] private void ShowSettingsPanel()    { ActivePanel = "settings"; Settings.Reload(); }

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
            existing.UpdatedAt = DateTime.Now;
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
                UpdatedAt = DateTime.Now, IsSelected = true,
                Folder = SelectedFolderFilter == "All" ? string.Empty : SelectedFolderFilter
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
        OnPropertyChanged(nameof(ShowSettings));
        OnPropertyChanged(nameof(ShowModels));
        OnPropertyChanged(nameof(ShowRag));
        OnPropertyChanged(nameof(ShowServices));
        OnPropertyChanged(nameof(ShowTasks));
        OnPropertyChanged(nameof(ShowBenchmarks));
        OnPropertyChanged(nameof(ShowSystem));
        OnPropertyChanged(nameof(ActiveViewModel));
    }

    partial void OnSearchQueryChanged(string value)
    {
        _ = LoadConversationsAsync();
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
            Kind = toast.Kind,
            DurationMs = toast.DurationMs
        };
        RunOnUi(() => Toasts.Add(vm));
        _ = RemoveToastLaterAsync(vm);
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
}
