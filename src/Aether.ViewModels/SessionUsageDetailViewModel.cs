using System.Collections.ObjectModel;
using Aether.Core.Models;
using Aether.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class SessionUsageDetailItemViewModel : ObservableObject
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public required string Category { get; init; }
    public required DateTime CreatedAt { get; init; }
    [ObservableProperty] private double _importance;
}

public enum SessionDetailSort
{
    CreatedDesc,
    CreatedAsc,
    ImportanceDesc,
    ImportanceAsc
}

public partial class SessionUsageDetailViewModel : ObservableObject
{
    private readonly IMemoryStore _memoryStore;
    private readonly ISettingsService _settings;

    public ObservableCollection<SessionUsageDetailItemViewModel> Items { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _conversationId = string.Empty;
    [ObservableProperty] private string _conversationTitle = string.Empty;
    [ObservableProperty] private SessionDetailSort _sort = SessionDetailSort.CreatedDesc;

    public SessionUsageDetailViewModel(IMemoryStore memoryStore, ISettingsService settings)
    {
        _memoryStore = memoryStore;
        _settings = settings;
    }

    public async Task LoadForConversationAsync(string conversationId, string title = "(untitled)")
    {
        IsLoading = true;
        ConversationId = conversationId;
        ConversationTitle = string.IsNullOrWhiteSpace(title) ? "(untitled)" : title;
        try
        {
            var all = await _memoryStore.GetAllAsync(includeArchived: true);
            var filtered = all.Where(m => string.Equals(m.SourceConversationId, conversationId, StringComparison.Ordinal));
            ApplyItems(filtered);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyItems(IEnumerable<Memory> memories)
    {
        IEnumerable<Memory> ordered = Sort switch
        {
            SessionDetailSort.CreatedAsc => memories.OrderBy(m => m.CreatedAt),
            SessionDetailSort.CreatedDesc => memories.OrderByDescending(m => m.CreatedAt),
            SessionDetailSort.ImportanceAsc => memories.OrderBy(m => m.ImportanceScore),
            SessionDetailSort.ImportanceDesc => memories.OrderByDescending(m => m.ImportanceScore),
            _ => memories.OrderByDescending(m => m.CreatedAt)
        };

        Items.Clear();
        foreach (var m in ordered)
        {
            Items.Add(new SessionUsageDetailItemViewModel
            {
                Id = m.Id,
                Content = m.Content,
                Category = m.Category,
                CreatedAt = m.CreatedAt,
                Importance = m.ImportanceScore
            });
        }
    }

    [RelayCommand]
    public Task ChangeSortAsync(SessionDetailSort sort)
    {
        Sort = sort;
        // reload using existing ConversationId
        return LoadForConversationAsync(ConversationId, ConversationTitle);
    }

    [RelayCommand]
    public async Task ExportCsvAsync(string? path = null)
    {
        var outDir = path is null ? Path.Combine(_settings.Settings.DataManagement.DataRootDirectory, "exports") : Path.GetDirectoryName(path) ?? _settings.Settings.DataManagement.DataRootDirectory;
        Directory.CreateDirectory(outDir);
        var file = path ?? Path.Combine(outDir, $"session-usage-{ConversationId}-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");

        var lines = new List<string> { "Id,Category,CreatedAt,Importance,Content" };
        foreach (var i in Items)
        {
            var safe = i.Content.Replace("\r", " ").Replace("\n", " ").Replace(",", " ");
            lines.Add($"{i.Id},{i.Category},{i.CreatedAt:o},{i.Importance},{safe}");
        }

        await File.WriteAllLinesAsync(file, lines);
    }
}
