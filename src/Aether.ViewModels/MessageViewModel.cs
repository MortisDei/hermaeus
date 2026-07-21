using System.Collections.Specialized;
using Aether.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.ViewModels;

public partial class MessageViewModel : ObservableObject
{
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private string _originalContent = string.Empty;
    [ObservableProperty] private bool   _isStreaming;
    [ObservableProperty] private bool   _isError;

    /// <summary>
    /// Live phase placeholder while a send has produced no visible content yet
    /// (r14 4.2), e.g. "Reading prompt... 5s". Empty otherwise.
    /// </summary>
    [ObservableProperty] private string _streamingStatus = string.Empty;
    public bool HasStreamingStatus => !string.IsNullOrEmpty(StreamingStatus);
    partial void OnStreamingStatusChanged(string value) => OnPropertyChanged(nameof(HasStreamingStatus));
    [ObservableProperty] private string _modelId = string.Empty;
    [ObservableProperty] private long   _durationMs;
    [ObservableProperty] private bool   _hasSources;

    public required string Role { get; init; }
    public bool IsUser      => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Stores attachment file paths when this is a user message with context attachments.
    /// Populated when attachments are added; used during regeneration to recover attachments.
    /// </summary>
    public UiBoundCollection<string> AttachedFilePaths { get; } = [];

    /// <summary>
    /// Memories (and, in future, RAG/agent citations) actually injected into
    /// this turn's context, for the chat Sources panel
    /// (docs/review/03-next-level-roadmap.md Phase 1). Only ever populated
    /// for the turn just generated; reloaded history has no source
    /// association recorded, so it stays empty.
    /// </summary>
    public UiBoundCollection<SourceReference> Sources { get; } = [];

    /// <summary>
    /// Non-memory citations (RAG chunks today) - shown individually and clickable, unchanged
    /// from before r18. Derived from <see cref="Sources"/>, split by <see cref="ProvenanceKind"/>.
    /// </summary>
    public UiBoundCollection<SourceReference> CitationSources { get; } = [];

    /// <summary>
    /// r18 03-model-catalog-and-memory-ui.md 3.3: memory-sourced entries used to render as one
    /// always-visible pill per recalled memory, indistinguishable from RAG citations, reading as
    /// "all the memories loaded" even though the header line above already collapses this to a
    /// count. Collapsed behind <see cref="MemorySourceSummary"/> and expanded on click instead.
    /// </summary>
    public UiBoundCollection<SourceReference> MemorySources { get; } = [];

    [ObservableProperty] private bool _isMemorySourcesExpanded;

    public bool HasMemorySources => MemorySources.Count > 0;
    public string MemorySourceSummary => $"Memories used: {MemorySources.Count}";

    public MessageViewModel() => Sources.CollectionChanged += OnSourcesChanged;

    private void OnSourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasSources = Sources.Count > 0;

        CitationSources.Clear();
        MemorySources.Clear();
        foreach (var source in Sources)
        {
            if (source.Kind == ProvenanceKind.Memory)
                MemorySources.Add(source);
            else
                CitationSources.Add(source);
        }

        OnPropertyChanged(nameof(HasMemorySources));
        OnPropertyChanged(nameof(MemorySourceSummary));
    }

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
