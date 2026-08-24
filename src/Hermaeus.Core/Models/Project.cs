namespace Hermaeus.Core.Models;

/// <summary>
/// A project is a view over one data root: a label plus a set of defaults
/// that new conversations, agent tasks, RAG queries and memories can inherit
/// (r24 doc 01). It never owns its own data root, secrets, settings file or
/// database, and switching one never rewrites an existing record.
/// </summary>
public class Project
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Absolute folder path, or empty for a rootless (topic) project.</summary>
    public string FolderRoot { get; set; } = string.Empty;

    /// <summary>Attached RAG dataset id, or empty for none.</summary>
    public string DatasetId { get; set; } = string.Empty;
    public string DefaultModelId { get; set; } = string.Empty;
    public string DefaultSystemPrompt { get; set; } = string.Empty;

    /// <summary>Key into <see cref="ProjectColors"/>, never a free hex value.</summary>
    public string Color { get; set; } = ProjectColors.Default;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastOpenedAt { get; set; } = DateTime.UtcNow;
    public bool IsArchived { get; set; }

    /// <summary>A working copy for the detail editor, so edits can be cancelled
    /// without mutating the live, list-bound instance before Save.</summary>
    public Project Clone() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        FolderRoot = FolderRoot,
        DatasetId = DatasetId,
        DefaultModelId = DefaultModelId,
        DefaultSystemPrompt = DefaultSystemPrompt,
        Color = Color,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        LastOpenedAt = LastOpenedAt,
        IsArchived = IsArchived
    };
}

public enum ProjectStateItemKind
{
    AcceptedDecision,
    RejectedApproach,
    Constraint,
    UnresolvedQuestion,
    ImportantArtifact,
    NextAction
}

public enum ProjectStateProposalStatus
{
    Pending,
    Accepted,
    Rejected
}

/// <summary>User-owned continuity data for a project. This is deliberately
/// separate from memories, Recall, RAG, conversations, and Agent task state.</summary>
public sealed class ProjectState
{
    public string ProjectId { get; set; } = string.Empty;
    public long Revision { get; set; }
    public string CurrentObjective { get; set; } = string.Empty;
    public string Milestone { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<ProjectStateItem> Items { get; set; } = [];
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public EvidenceOrigin UpdatedByOrigin { get; set; } = EvidenceOrigin.UserProvided;

    public ProjectState Clone() => new()
    {
        ProjectId = ProjectId,
        Revision = Revision,
        CurrentObjective = CurrentObjective,
        Milestone = Milestone,
        Status = Status,
        Items = Items.Select(item => item.Clone()).ToList(),
        UpdatedAtUtc = UpdatedAtUtc,
        UpdatedByOrigin = UpdatedByOrigin
    };
}

public sealed class ProjectStateItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ProjectStateItemKind Kind { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? ArtifactLocator { get; set; }
    public int Order { get; set; }
    public EvidenceOrigin Origin { get; set; } = EvidenceOrigin.UserProvided;
    public SourceReference? Source { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ProjectStateItem Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        Text = Text,
        ArtifactLocator = ArtifactLocator,
        Order = Order,
        Origin = Origin,
        Source = Source,
        CreatedAtUtc = CreatedAtUtc,
        UpdatedAtUtc = UpdatedAtUtc
    };
}

public sealed class ProjectStateProposal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = string.Empty;
    public long BaseRevision { get; set; }
    public ProjectState ProposedState { get; set; } = new();
    public EvidenceOrigin Origin { get; set; } = EvidenceOrigin.ModelInference;
    public SourceReference? Source { get; set; }
    public ProjectStateProposalStatus Status { get; set; } = ProjectStateProposalStatus.Pending;
    public string RejectionReason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ProjectStateRevisionConflictException(long expected, long actual)
    : InvalidOperationException($"Project State revision changed from {expected} to {actual}.")
{
    public long ExpectedRevision { get; } = expected;
    public long ActualRevision { get; } = actual;
}

/// <summary>
/// The small, fixed set of brand-palette accent keys (docs/mascot.md "Brand
/// colour palette") a project's colour dot may use. Never a raw hex string:
/// a free value could produce unreadable text against either theme.
/// </summary>
public static class ProjectColors
{
    public const string Forest = "Forest";
    public const string Copper = "Copper";
    public const string Amber = "Amber";
    public const string Teal = "Teal";
    public const string Indigo = "Indigo";
    public const string Berry = "Berry";

    public const string Default = Forest;

    public static readonly IReadOnlyList<string> All = [Forest, Copper, Amber, Teal, Indigo, Berry];

    public static bool IsValid(string color) => All.Contains(color, StringComparer.Ordinal);
}
