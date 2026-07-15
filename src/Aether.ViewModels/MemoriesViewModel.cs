using Aether.Core.Models;
using Aether.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

/// <summary>
/// ViewModel for managing and displaying memories in the UI.
/// </summary>
public partial class MemoriesViewModel : ObservableObject
{
    private readonly IMemoryStore _store;
    private readonly IConversationStore _conversations;
    private readonly ISettingsService _settings;
    private readonly IToastService _toasts;

    public UiBoundCollection<MemoryItemViewModel> Memories { get; } = [];

    /// <summary>Per-conversation memory counts, for triaging where memory sprawl is
    /// coming from. Replaces the standalone Session Usage panel (Feature Audit: Merge).</summary>
    public UiBoundCollection<ConversationFilterItemViewModel> ConversationFilters { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedCategory = string.Empty;  // Filter by category
    [ObservableProperty] private ConversationFilterItemViewModel? _selectedConversationFilter;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _totalCount;

    public List<string> AvailableCategories { get; } = ["All", "facts", "preferences", "learned_behaviors", "interests"];

    public MemoriesViewModel(IMemoryStore store, IConversationStore conversations, ISettingsService settings, IToastService toasts)
    {
        _store = store;
        _conversations = conversations;
        _settings = settings;
        _toasts = toasts;
        _selectedCategory = "All";
        Memories.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoMemories));
    }

    public bool HasNoMemories => Memories.Count == 0;

    [RelayCommand]
    public async Task InitializeAsync()
    {
        // Best-effort: sweep memories that have decayed below the floor and
        // gone unrecalled long enough into the archive before loading the
        // list, so the panel reflects lifecycle state without needing a
        // separate background job.
        try { await _store.ArchiveStaleMemoriesAsync(); }
        catch { }

        await LoadMemoriesAsync();
        await RefreshConversationFiltersAsync();
    }

    [RelayCommand]
    public async Task RefreshConversationFiltersAsync()
    {
        try
        {
            var convs = await _conversations.GetAllAsync(includeArchived: true);
            var ids = convs.Select(c => c.Id).ToList();
            var counts = ids.Count > 0
                ? await _store.GetCountsByConversationAsync(ids, includeArchived: true)
                : new Dictionary<string, int>();

            var previouslySelected = SelectedConversationFilter?.ConversationId;
            ConversationFilters.Clear();
            foreach (var c in convs.OrderByDescending(c => c.UpdatedAt))
            {
                counts.TryGetValue(c.Id, out var count);
                ConversationFilters.Add(new ConversationFilterItemViewModel
                {
                    ConversationId = c.Id,
                    Title = string.IsNullOrWhiteSpace(c.Title) ? "(untitled)" : c.Title,
                    MemoryCount = count
                });
            }

            SelectedConversationFilter = previouslySelected is null
                ? null
                : ConversationFilters.FirstOrDefault(f => f.ConversationId == previouslySelected);
        }
        catch (Exception ex)
        {
            _toasts.Show("Error", $"Failed to load conversation list: {ex.Message}", ToastKind.Error);
        }
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        IsLoading = true;
        try
        {
            List<Memory> results;

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                results = SelectedCategory == "All" || SelectedCategory == string.Empty
                    ? await _store.GetAllAsync(includeArchived: false)
                    : await _store.GetByCategoryAsync(SelectedCategory);
            }
            else
            {
                results = await _store.SearchAsync(SearchText);
                if (SelectedCategory != "All" && !string.IsNullOrWhiteSpace(SelectedCategory))
                    results = results.Where(m => m.Category == SelectedCategory).ToList();
            }

            if (SelectedConversationFilter is not null)
                results = results.Where(m => string.Equals(m.SourceConversationId, SelectedConversationFilter.ConversationId, StringComparison.Ordinal)).ToList();

            Memories.Clear();
            foreach (var memory in results.OrderByDescending(m => m.IsPinned).ThenByDescending(m => MemoryLifecycle.ComputeEffectiveImportance(m)))
            {
                Memories.Add(ToViewModel(memory));
            }

            TotalCount = Memories.Count;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportConversationCsv))]
    public async Task ExportConversationCsvAsync()
    {
        if (SelectedConversationFilter is null) return;

        var outDir = Path.Combine(Aether.Services.SettingsService.ResolveDataRoot(_settings.Settings), "exports");
        var file = Path.Combine(outDir, $"memories-{SelectedConversationFilter.ConversationId}-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");

        var lines = new List<string> { "Id,Category,CreatedAt,Importance,Content" };
        foreach (var m in Memories)
        {
            var safe = m.Content.Replace("\r", " ").Replace("\n", " ").Replace(",", " ");
            lines.Add($"{m.Id},{m.Category},{m.CreatedAt:o},{m.ImportanceScore},{safe}");
        }

        await AtomicFile.WriteAllTextAsync(file, string.Join(Environment.NewLine, lines));
        _toasts.Show("Exported", $"Wrote {Memories.Count} memories to {file}", ToastKind.Success);
    }

    private bool CanExportConversationCsv() => SelectedConversationFilter is not null;

    [RelayCommand]
    public void ClearConversationFilter()
    {
        SelectedConversationFilter = null;
    }

    [RelayCommand]
    public async Task DeleteMemoryAsync(string memoryId)
    {
        try
        {
            await _store.DeleteAsync(memoryId);
            var item = Memories.FirstOrDefault(m => m.Id == memoryId);
            if (item is not null)
                Memories.Remove(item);
            _toasts.Show("Memory deleted", "The memory has been removed.", ToastKind.Info);
        }
        catch (Exception ex)
        {
            _toasts.Show("Error", $"Failed to delete memory: {ex.Message}", ToastKind.Error);
        }
    }

    [RelayCommand]
    public async Task TogglePinAsync(string memoryId)
    {
        try
        {
            var memory = await _store.GetByIdAsync(memoryId);
            if (memory is null) return;

            memory.IsPinned = !memory.IsPinned;
            await _store.SaveAsync(memory);

            var item = Memories.FirstOrDefault(m => m.Id == memoryId);
            if (item is not null)
                item.IsPinned = memory.IsPinned;

            _toasts.Show(memory.IsPinned ? "Memory pinned" : "Memory unpinned", "", ToastKind.Info);
        }
        catch (Exception ex)
        {
            _toasts.Show("Error", $"Failed to update memory: {ex.Message}", ToastKind.Error);
        }
    }

    [RelayCommand]
    public async Task ToggleArchiveAsync(string memoryId)
    {
        try
        {
            var memory = await _store.GetByIdAsync(memoryId);
            if (memory is null) return;

            memory.IsArchived = !memory.IsArchived;
            await _store.SaveAsync(memory);

            var item = Memories.FirstOrDefault(m => m.Id == memoryId);
            if (item is not null)
                Memories.Remove(item);  // Remove from view if archiving

            _toasts.Show(memory.IsArchived ? "Memory archived" : "Memory restored", "", ToastKind.Info);
        }
        catch (Exception ex)
        {
            _toasts.Show("Error", $"Failed to update memory: {ex.Message}", ToastKind.Error);
        }
    }

    private async Task LoadMemoriesAsync()
    {
        IsLoading = true;
        try
        {
            var memories = await _store.GetAllAsync(includeArchived: false);
            Memories.Clear();
            foreach (var memory in memories.OrderByDescending(m => m.IsPinned).ThenByDescending(m => MemoryLifecycle.ComputeEffectiveImportance(m)))
            {
                Memories.Add(ToViewModel(memory));
            }
            TotalCount = Memories.Count;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value) 
    {
        SearchCommand.Execute(null);
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        SearchCommand.Execute(null);
    }

    partial void OnSelectedConversationFilterChanged(ConversationFilterItemViewModel? value)
    {
        ExportConversationCsvCommand.NotifyCanExecuteChanged();
        SearchCommand.Execute(null);
    }

    private static MemoryItemViewModel ToViewModel(Memory memory) => new()
    {
        Id = memory.Id,
        Category = memory.Category,
        Content = memory.Content,
        CreatedAt = memory.CreatedAt,
        UpdatedAt = memory.UpdatedAt,
        IsPinned = memory.IsPinned,
        IsArchived = memory.IsArchived,
        ImportanceScore = memory.ImportanceScore,
        EffectiveImportance = MemoryLifecycle.ComputeEffectiveImportance(memory),
        RecallCount = memory.RecallCount,
        LastRecalledAt = memory.LastRecalledAt,
        Tags = string.Join(", ", memory.Tags),
        FrequencyCount = memory.FrequencyCount
    };
}

/// <summary>
/// ViewModel for a single memory item in the UI.
/// </summary>
public partial class MemoryItemViewModel : ObservableObject
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Content { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
    public required double ImportanceScore { get; init; }
    public double EffectiveImportance { get; init; }
    public int RecallCount { get; init; }
    public DateTime? LastRecalledAt { get; init; }
    public string Tags { get; init; } = string.Empty;
    public int FrequencyCount { get; init; } = 1;

    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private bool _isArchived;

    public string CategoryDisplay => Category switch
    {
        "facts" => "📌 Fact",
        "preferences" => "❤️ Preference",
        "learned_behaviors" => "🧠 Learned",
        "interests" => "✨ Interest",
        _ => Category
    };

    public string CreatedDisplay
    {
        get
        {
            var local = CreatedAt.Kind == DateTimeKind.Utc ? CreatedAt.ToLocalTime() : CreatedAt;
            var today = DateTime.Today;
            return local.Date == today
                ? local.ToString("HH:mm")
                : local.Date >= today.AddDays(-7)
                    ? local.ToString("ddd")
                    : local.ToString("d MMM");
        }
    }

    public string RecallDisplay => LastRecalledAt is null
        ? "Never recalled"
        : $"Recalled {RecallCount}x, last {LastRecalledAt.Value.ToLocalTime():d MMM}";

    public string ImportanceDisplay => ImportanceScore switch
    {
        >= 0.8 => "Very Important",
        >= 0.6 => "Important",
        >= 0.4 => "Medium",
        _ => "Low"
    };

    public string PinButtonLabel => IsPinned ? "Unpin" : "Pin";
}

/// <summary>
/// One entry in the conversation filter list: a conversation and how many
/// memories it has contributed, used to triage memory sprawl per-conversation.
/// </summary>
public partial class ConversationFilterItemViewModel : ObservableObject
{
    public required string ConversationId { get; init; }
    public required string Title { get; init; }
    [ObservableProperty] private int _memoryCount;

    public string Display => $"{Title} ({MemoryCount})";
}
