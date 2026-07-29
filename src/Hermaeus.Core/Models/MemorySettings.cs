namespace Hermaeus.Core.Models;

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

    /// <summary>
    /// Whether chat also injects Global-scope agent lessons (the per-machine
    /// self-learning store, normally Agent-only) alongside stored memories.
    /// Off by default: the lesson store is workspace/agent-focused and this
    /// is opt-in until it has more mileage. Lessons still only ever inform;
    /// they are read-only in chat, never editable via memory markers.
    /// </summary>
    public bool ConsumeAgentLessonsInChat { get; set; } = false;

    /// <summary>
    /// r24 doc 02 2.0: keeps a searchable copy of message and task text in
    /// recall.db, included in backups. Default on (a flagship search feature
    /// that ships off is one nobody finds), but visible: Settings > Memory
    /// states this plainly next to the switch. Turning it off stops indexing
    /// immediately and disables the recall half of the palette; the command
    /// half keeps working.
    /// </summary>
    public bool RecallIndexingEnabled { get; set; } = true;

    /// <summary>
    /// r24 doc 02 2.6: whether a chat send may retrieve from Recall and
    /// inject a bounded, citation-pilled block. Off by default, matching
    /// <see cref="ConsumeAgentLessonsInChat"/>'s precedent - consuming one
    /// subsystem's knowledge inside another, read-only, opt-in.
    /// </summary>
    public bool RecallInjectionEnabled { get; set; } = false;

    /// <summary>Token budget for Recall injection, separate from <see cref="InjectionTokenBudget"/>.</summary>
    public int RecallInjectionTokenBudget { get; set; } = 400;
}
