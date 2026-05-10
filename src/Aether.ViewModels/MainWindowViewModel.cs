using System.Collections.ObjectModel;
using Aether.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IConversationStore _store;

    public ChatViewModel            Chat     { get; }
    public SettingsViewModel        Settings { get; }
    public ModelManagementViewModel Models   { get; }
    public RagViewModel             Rag      { get; }
    public ServicesViewModel        Services { get; }

    public ObservableCollection<ConversationItemViewModel> Conversations { get; } = [];

    [ObservableProperty] private bool   _isSidebarOpen = true;
    [ObservableProperty] private string _searchQuery   = string.Empty;
    [ObservableProperty] private string _activePanel   = "chat";
    [ObservableProperty] private bool   _isLoading;

    public bool ShowChat     => ActivePanel == "chat";
    public bool ShowSettings => ActivePanel == "settings";
    public bool ShowModels   => ActivePanel == "models";
    public bool ShowRag      => ActivePanel == "rag";
    public bool ShowServices => ActivePanel == "services";
    public object ActiveViewModel => ActivePanel switch
    {
        "settings" => Settings,
        "models"   => Models,
        "rag"      => Rag,
        "services" => Services,
        _          => Chat
    };

    public MainWindowViewModel(
        IConversationStore store,
        ChatViewModel chat,
        SettingsViewModel settings,
        ModelManagementViewModel models,
        RagViewModel rag,
        ServicesViewModel services)
    {
        _store = store; Chat = chat; Settings = settings;
        Models = models; Rag = rag; Services = services;
        Chat.ConversationSaved += OnConversationSaved;
        Services.ServerAvailabilityChanged += async (_, _) => await Chat.LoadModelsAsync();
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            await LoadConversationsAsync();
            await Rag.LoadDatasetsAsync();
            await Services.AutoStartAllAsync();
            await Chat.LoadModelsAsync();
        }
        finally { IsLoading = false; }
    }

    public void Shutdown() => Services.StopAll();

    private async Task LoadConversationsAsync()
    {
        var convs = await _store.GetAllAsync();
        Conversations.Clear();
        foreach (var c in convs)
            Conversations.Add(new ConversationItemViewModel
            {
                Id = c.Id, Title = c.Title, ModelId = c.ModelId,
                UpdatedAt = c.UpdatedAt, SystemPrompt = c.SystemPrompt
            });
    }

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
    }

    [RelayCommand] private void ToggleSidebar()       => IsSidebarOpen = !IsSidebarOpen;
    [RelayCommand] private async Task ShowChatPanelAsync()
    {
        ActivePanel = "chat";
        await Chat.LoadModelsAsync();
    }
    [RelayCommand] private void ShowRagPanel()         => ActivePanel = "rag";
    [RelayCommand] private void ShowModelsPanel()      { ActivePanel = "models"; _ = Models.RefreshCommand.ExecuteAsync(null); }
    [RelayCommand] private void ShowServicesPanel()    => ActivePanel = "services";
    [RelayCommand] private void ShowSettingsPanel()    { ActivePanel = "settings"; Settings.Reload(); }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) { await LoadConversationsAsync(); return; }
        var results = await _store.SearchAsync(SearchQuery);
        Conversations.Clear();
        foreach (var c in results)
            Conversations.Add(new ConversationItemViewModel
                { Id = c.Id, Title = c.Title, ModelId = c.ModelId, UpdatedAt = c.UpdatedAt });
    }

    private void OnConversationSaved(object? sender, string convId)
    {
        var existing = Conversations.FirstOrDefault(c => c.Id == convId);
        if (existing is not null)
        {
            existing.Title = Chat.ConversationTitle;
            existing.UpdatedAt = DateTime.Now;
            var idx = Conversations.IndexOf(existing);
            if (idx > 0) Conversations.Move(idx, 0);
        }
        else
        {
            Conversations.Insert(0, new ConversationItemViewModel
            {
                Id = convId, Title = Chat.ConversationTitle,
                ModelId = Chat.SelectedModel?.Id ?? string.Empty,
                UpdatedAt = DateTime.Now, IsSelected = true
            });
            foreach (var c in Conversations.Skip(1)) c.IsSelected = false;
        }
    }

    partial void OnActivePanelChanged(string value)
    {
        OnPropertyChanged(nameof(ShowChat));
        OnPropertyChanged(nameof(ShowSettings));
        OnPropertyChanged(nameof(ShowModels));
        OnPropertyChanged(nameof(ShowRag));
        OnPropertyChanged(nameof(ShowServices));
        OnPropertyChanged(nameof(ActiveViewModel));
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) _ = LoadConversationsAsync();
    }
}
