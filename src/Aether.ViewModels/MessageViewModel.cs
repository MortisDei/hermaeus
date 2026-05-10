using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.ViewModels;

public partial class MessageViewModel : ObservableObject
{
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private bool   _isStreaming;
    [ObservableProperty] private bool   _isError;

    public required string Role { get; init; }
    public bool IsUser      => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = DateTime.Now;
}
