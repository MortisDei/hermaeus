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
    [ObservableProperty] private bool     _isArchived;

    public event Action<ConversationItemViewModel>? MetadataChanged;

    public required string Id  { get; init; }
    public string ModelId      { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string FolderDisplay => string.IsNullOrWhiteSpace(Folder) ? "Unfiled" : Folder.Trim();
    public string TagsDisplay => string.Join("  ", Tags);
    public string ArchiveActionLabel => IsArchived ? "Unarchive" : "Archive";
    public string PinActionLabel => IsPinned ? "Unpin" : "Pin";
    public List<string> Tags => TagsText
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(t => t.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public string TimeDisplay
    {
        get
        {
            var local = UpdatedAt.Kind == DateTimeKind.Utc ? UpdatedAt.ToLocalTime() : UpdatedAt;
            var today = DateTime.Today;
            return local.Date == today
                ? local.ToString("HH:mm")
                : local.Date >= today.AddDays(-7)
                    ? local.ToString("ddd")
                    : local.ToString("d MMM");
        }
    }

    partial void OnUpdatedAtChanged(DateTime value) => OnPropertyChanged(nameof(TimeDisplay));
    partial void OnTitleChanged(string value) => MetadataChanged?.Invoke(this);
    partial void OnFolderChanged(string value)
    {
        OnPropertyChanged(nameof(FolderDisplay));
        MetadataChanged?.Invoke(this);
    }
    partial void OnTagsTextChanged(string value)
    {
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(TagsDisplay));
        MetadataChanged?.Invoke(this);
    }
    partial void OnIsPinnedChanged(bool value)
    {
        OnPropertyChanged(nameof(PinActionLabel));
        MetadataChanged?.Invoke(this);
    }
    partial void OnIsArchivedChanged(bool value)
    {
        OnPropertyChanged(nameof(ArchiveActionLabel));
        MetadataChanged?.Invoke(this);
    }
}
