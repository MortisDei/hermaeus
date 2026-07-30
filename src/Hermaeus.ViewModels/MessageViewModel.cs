using System.Collections.Specialized;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Hermaeus.ViewModels;

public partial class MessageViewModel : ObservableObject, IConversationNode
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

    /// <summary>
    /// r25 doc 01: settable (was init-only) because identity now has to survive
    /// a save/load round trip. Before r25 a reloaded message got a fresh Guid,
    /// which was harmless when nothing referred to a message by id and is not
    /// harmless once <see cref="ParentId"/> does.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>r25 doc 01: the message this one replies to. Empty for the first message.</summary>
    public string ParentId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

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

    // ── r25 doc 01: branch switcher state ───────────────────────────────────
    // Recomputed by ChatViewModel whenever the tree changes, rather than derived
    // here, because a message cannot see its own siblings.

    /// <summary>1-based position among this message's siblings.</summary>
    [ObservableProperty] private int _branchIndex = 1;

    /// <summary>How many siblings this message has, itself included.</summary>
    [ObservableProperty] private int _branchCount = 1;

    /// <summary>
    /// Hidden for every message in a conversation that has never been branched,
    /// which is every message in every conversation written before r25.
    /// </summary>
    public bool HasSiblings => BranchCount > 1;
    public string BranchLabel => $"{BranchIndex}/{BranchCount}";

    partial void OnBranchIndexChanged(int value) => OnPropertyChanged(nameof(BranchLabel));

    partial void OnBranchCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSiblings));
        OnPropertyChanged(nameof(BranchLabel));
    }

    /// <summary>r25 doc 01 1.4: user messages can be edited into a sibling; assistant
    /// messages cannot, because a transcript you can rewrite is not a transcript.</summary>
    [ObservableProperty] private bool _isEditing;

    [ObservableProperty] private string _editText = string.Empty;

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
