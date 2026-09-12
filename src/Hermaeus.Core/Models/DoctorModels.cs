using System;
using System.Linq;

namespace Hermaeus.Core.Models;

public enum DoctorCheckStatus
{
    Ready,
    Warning,
    Error,
    Info
}

public enum DoctorActionKind
{
    None,
    Fix,
    Navigate,
    OpenExternal
}

/// <summary>
/// Stable remediation destination for a Doctor finding. The area is the
/// navigable page; section, item, and focus identify the concrete control or
/// resource when the page can provide one. Empty values mean the finding only
/// has a page-level destination, never an inferred server or model.
/// </summary>
public sealed record DoctorActionTarget(
    string Area,
    string Section = "",
    string ItemId = "",
    string Focus = "")
{
    public string Route => string.Join('/', new[] { Area, Section, ItemId, Focus }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed record DoctorCheck(
    string Key,
    string Title,
    DoctorCheckStatus Status,
    string Summary,
    string Detail,
    string FixLabel,
    bool CanFix,
    string Diagnostics,
    string Category,
    DoctorActionKind ActionKind = DoctorActionKind.None,
    string ActionTarget = "")
{
    /// <summary>Typed destination for the finding's remediation action.</summary>
    public DoctorActionTarget? Target { get; init; }

    public bool HasAction => CanFix && ActionKind != DoctorActionKind.None;

    public string ActionLabel => FixLabel;

    public string ActionTooltip => ActionKind switch
    {
        DoctorActionKind.Fix => $"Runs the suggested fix for {Title}.",
        DoctorActionKind.Navigate => $"Opens the relevant Hermaeus settings for {Title}.",
        DoctorActionKind.OpenExternal => "Opens the latest Hermaeus release information in your browser.",
        _ => string.Empty
    };

    public string StatusLabel => Status switch
    {
        DoctorCheckStatus.Ready => "Ready",
        DoctorCheckStatus.Warning => "Warning",
        DoctorCheckStatus.Error => "Error",
        DoctorCheckStatus.Info => "Info",
        _ => Status.ToString()
    };
}

public sealed record DoctorReport(
    IReadOnlyList<DoctorCheck> Checks,
    DateTime ScannedAt,
    string Summary)
{
    public int ErrorCount => Checks.Count(c => c.Status == DoctorCheckStatus.Error);
    public int WarningCount => Checks.Count(c => c.Status == DoctorCheckStatus.Warning);
}
