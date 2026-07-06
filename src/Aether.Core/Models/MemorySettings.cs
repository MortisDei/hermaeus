namespace Aether.Core.Models;

/// <summary>
/// Configuration for the chat memory feature including toggling, injection budget, and retention.
/// </summary>
public class MemorySettings
{
    /// <summary>
    /// Whether the memory feature is enabled globally.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Per-conversation memory enabled state (conversation ID -> enabled flag).
    /// If conversation ID not in dictionary, inherits global Enabled setting.
    /// </summary>
    public Dictionary<string, bool> EnabledPerConversation { get; set; } = [];

    /// <summary>
    /// Minimum importance score threshold for auto-summarizing important conversations.
    /// Range: 0.0 to 1.0. Memories above this threshold trigger auto-summary.
    /// </summary>
    public double AutoSummarizeImportanceThreshold { get; set; } = 0.7;

    /// <summary>
    /// Maximum number of memories to keep per conversation before old ones are archived.
    /// </summary>
    public int MaxMemoriesPerConversation { get; set; } = 50;

    /// <summary>
    /// Whether to inject stored memories into chat context automatically.
    /// If false, agent must explicitly request memories via special markers.
    /// </summary>
    public bool InjectMemoriesIntoContext { get; set; } = false;

    /// <summary>
    /// Token budget for memory injection (approximate).
    /// When injecting memories, stay under this token limit.
    /// Default 500 tokens.
    /// </summary>
    public int InjectionTokenBudget { get; set; } = 500;

    /// <summary>
    /// Retention policy for memory lifecycle: KeepAll, ArchiveAfter30Days, DeleteAfter90Days.
    /// </summary>
    public string RetentionPolicy { get; set; } = "KeepAll";

    /// <summary>
    /// Number of days before a memory is auto-archived (only if RetentionPolicy includes archival).
    /// </summary>
    public int AutoArchiveAfterDays { get; set; } = 30;
}
