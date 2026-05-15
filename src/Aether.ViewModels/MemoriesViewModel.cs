using System.Collections.ObjectModel;
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
    private readonly IToastService _toasts;

    public ObservableCollection<MemoryItemViewModel> Memories { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedCategory = string.Empty;  // Filter by category
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _totalCount;

    public List<string> AvailableCategories { get; } = ["All", "facts", "preferences", "learned_behaviors", "interests"];

    public MemoriesViewModel(IMemoryStore store, IToastService toasts)
    {
        _store = store;
        _toasts = toasts;
        _selectedCategory = "All";
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        await LoadMemoriesAsync();
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

            Memories.Clear();
            foreach (var memory in results.OrderByDescending(m => m.IsPinned).ThenByDescending(m => m.ImportanceScore))
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
            foreach (var memory in memories.OrderByDescending(m => m.IsPinned).ThenByDescending(m => m.ImportanceScore))
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

    public string CreatedDisplay => CreatedAt.Date == DateTime.Today
        ? CreatedAt.ToString("HH:mm")
        : CreatedAt.Date >= DateTime.Today.AddDays(-7)
            ? CreatedAt.ToString("ddd")
            : CreatedAt.ToString("d MMM");

    public string ImportanceDisplay => ImportanceScore switch
    {
        >= 0.8 => "Very Important",
        >= 0.6 => "Important",
        >= 0.4 => "Medium",
        _ => "Low"
    };

    public string PinButtonLabel => IsPinned ? "Unpin" : "Pin";
}
