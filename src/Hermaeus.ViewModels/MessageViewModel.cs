using System.Collections.Specialized;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Hermaeus.ViewModels;

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

    /// <summary>
    /// r19 1.2: generation stopped because the max-token cap was hit, not
    /// because the model finished naturally. Drives a truncation notice and
    /// Continue affordance on the message.
    /// </summary>
    [ObservableProperty] private bool _wasTruncated;

    /// <summary>The configured max-token cap in effect when this message was generated, for the truncation notice. 0 when unknown.</summary>
    [ObservableProperty] private int _truncatedAtTokens;

    public string TruncationNotice => TruncatedAtTokens > 0
        ? $"Stopped at the response token limit ({TruncatedAtTokens} tokens)."
        : "Stopped at the response token limit.";

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
    /// r25 doc 02: one receipt of everything injected into this turn, grouped by
    /// <see cref="ProvenanceKind"/> in a fixed order, replacing the three separate
    /// strips this used to have (an always-visible citation strip, a collapsed
    /// memory pill, and after r24 2.6 a Recall strip that leaked into the first
    /// one and made collapsing the second hide nothing).
    /// </summary>
    public UiBoundCollection<ChatContextReceiptSection> ContextSections { get; } = [];

    /// <summary>Collapsed by default, and collapsed means no source item is visible at all.</summary>
    [ObservableProperty] private bool _isContextExpanded;

    public bool HasContext => ContextSections.Count > 0;
    public string ContextSummary => ChatContextReceipt.Summarize(ContextSections);

    public MessageViewModel() => Sources.CollectionChanged += OnSourcesChanged;

    private void OnSourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasSources = Sources.Count > 0;

        ContextSections.Clear();
        foreach (var section in ChatContextReceipt.Build(Sources))
            ContextSections.Add(section);

        OnPropertyChanged(nameof(HasContext));
        OnPropertyChanged(nameof(ContextSummary));
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
