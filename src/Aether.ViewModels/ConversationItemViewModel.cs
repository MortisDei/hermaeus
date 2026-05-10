using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.ViewModels;

public partial class ConversationItemViewModel : ObservableObject
{
    [ObservableProperty] private string   _title = "New Conversation";
    [ObservableProperty] private bool     _isSelected;
    [ObservableProperty] private DateTime _updatedAt;

    public required string Id  { get; init; }
    public string ModelId      { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;

    public string TimeDisplay => UpdatedAt.Date == DateTime.Today
        ? UpdatedAt.ToString("HH:mm")
        : UpdatedAt.Date >= DateTime.Today.AddDays(-7)
            ? UpdatedAt.ToString("ddd")
            : UpdatedAt.ToString("d MMM");
}
