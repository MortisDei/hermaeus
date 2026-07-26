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
