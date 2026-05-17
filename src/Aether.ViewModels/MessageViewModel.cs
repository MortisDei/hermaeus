using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.ViewModels;

public partial class MessageViewModel : ObservableObject
{
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private string _originalContent = string.Empty;
    [ObservableProperty] private bool   _isStreaming;
    [ObservableProperty] private bool   _isError;
    [ObservableProperty] private string _modelId = string.Empty;
    [ObservableProperty] private long   _durationMs;

    public required string Role { get; init; }
    public bool IsUser      => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// Stores attachment file paths when this is a user message with context attachments.
    /// Populated when attachments are added; used during regeneration to recover attachments.
    /// </summary>
    public ObservableCollection<string> AttachedFilePaths { get; } = [];

    public string MetaDisplay
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ModelId)) parts.Add(ModelId);
            if (DurationMs > 0) parts.Add(FormatDuration(DurationMs));
            return string.Join(" · ", parts);
        }
    }

    partial void OnModelIdChanged(string value) => OnPropertyChanged(nameof(MetaDisplay));
    partial void OnDurationMsChanged(long value) => OnPropertyChanged(nameof(MetaDisplay));

    private static string FormatDuration(long ms)
    {
        if (ms < 1_000) return $"{ms} ms";
        var seconds = ms / 1_000.0;
        return seconds < 60 ? $"{seconds:F1}s" : $"{seconds / 60:F1}m";
    }
}
