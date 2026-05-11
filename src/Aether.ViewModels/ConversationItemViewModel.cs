using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.ViewModels;

public partial class ConversationItemViewModel : ObservableObject
{
    [ObservableProperty] private string   _title = "New Conversation";
    [ObservableProperty] private bool     _isSelected;
    [ObservableProperty] private DateTime _updatedAt;
    [ObservableProperty] private string   _folder = string.Empty;
    [ObservableProperty] private string   _tagsText = string.Empty;
    [ObservableProperty] private bool     _isPinned;

    public required string Id  { get; init; }
    public string ModelId      { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string FolderDisplay => string.IsNullOrWhiteSpace(Folder) ? "Unfiled" : Folder.Trim();
    public string TagsDisplay => string.Join("  ", Tags);
    public List<string> Tags => TagsText
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(t => t.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public string TimeDisplay => UpdatedAt.Date == DateTime.Today
        ? UpdatedAt.ToString("HH:mm")
        : UpdatedAt.Date >= DateTime.Today.AddDays(-7)
            ? UpdatedAt.ToString("ddd")
            : UpdatedAt.ToString("d MMM");

    partial void OnUpdatedAtChanged(DateTime value) => OnPropertyChanged(nameof(TimeDisplay));
    partial void OnFolderChanged(string value) => OnPropertyChanged(nameof(FolderDisplay));
    partial void OnTagsTextChanged(string value)
    {
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(TagsDisplay));
    }
}
