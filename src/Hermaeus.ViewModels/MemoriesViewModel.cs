using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

/// <summary>
/// ViewModel for managing and displaying memories in the UI.
/// </summary>
public partial class MemoriesViewModel : ViewModelBase
{
    private readonly IMemoryStore _store;
    private readonly IConversationStore _conversations;
    private readonly ISettingsService _settings;
    private readonly IToastService _toasts;
    private readonly IActivityRecorder? _activity;
    private CancellationTokenSource? _searchTextCts;

    public UiBoundCollection<MemoryItemViewModel> Memories { get; } = [];

    /// <summary>Per-conversation memory counts, for triaging where memory sprawl is
    /// coming from. Replaces the standalone Session Usage panel (Feature Audit: Merge).</summary>
    public UiBoundCollection<ConversationFilterItemViewModel> ConversationFilters { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedCategory = string.Empty;  // Filter by category
    [ObservableProperty] private ConversationFilterItemViewModel? _selectedConversationFilter;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _embeddingMismatchCount;
    [ObservableProperty] private bool _isReembedding;

    public List<string> AvailableCategories { get; } = ["All", "facts", "preferences", "learned_behaviors", "interests"];
    public Func<MemoryItemViewModel, Task<bool>>? RequestDeleteConfirmation { get; set; }

    public MemoriesViewModel(IMemoryStore store, IConversationStore conversations, ISettingsService settings, IToastService toasts,
        IActivityRecorder? activity = null)
    {
        _activity = activity;
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
        // r28 doc 03 3.3: a sweep that archives nothing still records, because
        // "it ran and found nothing" and "it never ran" are the two states
        // this panel exists to separate.
        try
        {
            var archived = await _store.ArchiveStaleMemoriesAsync();
            _activity.RecordSafe("memory.auto-archive", string.Empty, ActivityOutcome.Succeeded,
                archived == 1 ? "Archived 1 stale memory" : $"Archived {archived} stale memories");
        }
        catch (Exception ex)
        {
            _activity.RecordSafe("memory.auto-archive", string.Empty, ActivityOutcome.Failed,
                "Memory archive sweep failed", ex.Message);
        }

        await LoadMemoriesAsync();
        await RefreshConversationFiltersAsync();
        await RefreshEmbeddingMismatchAsync();
    }

    /// <summary>doc 04 4.1: registered next to the ViewModel that owns the action.</summary>
    public void RegisterCommands(ICommandRegistry registry)
    {
        registry.Register(new AppCommand(
            Id: "memories.search", Title: "Search memories", Area: "Memory",
            Description: "Search stored memories by text.",
            Keywords: ["memory", "search", "find"], Shortcut: "",
            CanExecute: () => true,
            Execute: () => SearchCommand.ExecuteAsync(null)));

        registry.Register(new AppCommand(
            Id: "memories.reembed-mismatched", Title: "Re-embed mismatched memories", Area: "Memory",
            Description: "Re-embed memories whose vectors came from a different embedding model.",
            Keywords: ["memory", "embedding", "reembed", "mismatch"], Shortcut: "",
            CanExecute: () => EmbeddingMismatchCount > 0,
            DisabledReason: () => "No mismatched embeddings.",
            Execute: () => ReembedMismatchedCommand.ExecuteAsync(null)));
    }

    /// <summary>
    /// Surfaces the "old vectors after an embedding model switch" gap (r16
    /// 02-memory-integrity.md 2.4): recall degrades silently to FTS-only
    /// otherwise, indistinguishable from working. Best-effort; a failed
    /// probe (no embedding service reachable) just shows no banner.
    /// </summary>
    [RelayCommand]
    public async Task RefreshEmbeddingMismatchAsync()
    {
        try { EmbeddingMismatchCount = await _store.GetEmbeddingMismatchCountAsync(); }
        catch { EmbeddingMismatchCount = 0; }
    }

    public bool HasEmbeddingMismatch => EmbeddingMismatchCount > 0;

    public string EmbeddingMismatchLabel =>
        $"{EmbeddingMismatchCount} memor{(EmbeddingMismatchCount == 1 ? "y was" : "ies were")} embedded with a different model.";

    partial void OnEmbeddingMismatchCountChanged(int value) => OnPropertyChanged(nameof(HasEmbeddingMismatch));

    /// <summary>
    /// User-clicked only (r16 02-memory-integrity.md 2.4 explicit rejection:
    /// no automatic re-embed on a model switch). Clears the stale vectors
    /// and kicks off a background backfill; the mismatch count is refreshed
    /// immediately (it drops to 0 as soon as the clear completes) rather
    /// than waiting for the backfill to finish.
    /// </summary>
    [RelayCommand]
    public async Task ReembedMismatchedAsync()
    {
        IsReembedding = true;
        try
        {
            var cleared = await _store.ClearMismatchedEmbeddingsAsync();
            await RefreshEmbeddingMismatchAsync();
            _toasts.Show("Re-embedding started", $"Cleared {cleared} stale embedding(s); they will be re-embedded in the background.", ToastKind.Info);
        }
        catch (Exception ex)
        {
            _toasts.Show("Error", $"Failed to re-embed memories: {ex.Message}", ToastKind.Error);
        }
        finally
        {
            IsReembedding = false;
        }
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

        var outDir = Path.Combine(Hermaeus.Services.SettingsService.ResolveDataRoot(_settings.Settings), "exports");
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
            var item = Memories.FirstOrDefault(m => m.Id == memoryId);
            if (item is null || RequestDeleteConfirmation is null || !await RequestDeleteConfirmation(item))
                return;
            await _store.DeleteAsync(memoryId);
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

    /// <summary>
    /// r12 02-async-and-threading.md 2.3: debounces per-keystroke search
    /// (one DB search per character otherwise, with unordered completion
    /// interleaving Clear/Add on the bound <see cref="Memories"/>
    /// collection) using the same 300 ms + CTS shape as
    /// <see cref="MainWindowViewModel.OnSearchQueryChanged"/>.
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        _searchTextCts?.Cancel();
        _searchTextCts?.Dispose();
        _searchTextCts = new CancellationTokenSource();
        var token = _searchTextCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token);
                if (token.IsCancellationRequested) return;
                await RunOnUiAsync(SearchAsync);
            }
            catch (OperationCanceledException) { }
        }, token);
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
    public string PinStateLabel => IsPinned ? "Pinned" : "Not pinned";

    partial void OnIsPinnedChanged(bool value)
    {
        OnPropertyChanged(nameof(PinButtonLabel));
        OnPropertyChanged(nameof(PinStateLabel));
    }
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
