using System;
using System.Linq;

namespace Aether.Core.Models;

/// <summary>
/// Severity of an inspection check, shared across Doctor, Trust, and Privacy
/// Audit views.
/// </summary>
public enum CheckSeverity
{
    Info,
    Ready,
    Warning,
    Error
}

/// <summary>
/// One inspection finding, contributed by a subsystem-specific
/// IInspectionCheckProvider and aggregated by the shared InspectionEngine.
/// View-specific detail that doesn't fit the common shape (e.g. Trust's risk
/// level and resolved path) travels in DetailJson.
/// </summary>
public sealed record InspectionCheck(
    string Id,
    string View,
    string Category,
    string Title,
    CheckSeverity Severity,
    string Summary,
    string Detail,
    string FixLabel,
    bool CanFix,
    string Diagnostics,
    string DetailJson = "{}")
{
    public string SeverityLabel => Severity switch
    {
        CheckSeverity.Ready => "Ready",
        CheckSeverity.Warning => "Warning",
        CheckSeverity.Error => "Error",
        CheckSeverity.Info => "Info",
        _ => Severity.ToString()
    };
}

public sealed record InspectionReport(
    IReadOnlyList<InspectionCheck> Checks,
    DateTime ScannedAt,
    string Summary)
{
    public int ErrorCount => Checks.Count(c => c.Severity == CheckSeverity.Error);
    public int WarningCount => Checks.Count(c => c.Severity == CheckSeverity.Warning);
}

/// <summary>
/// A privacy-relevant fact about the current configuration (remote providers,
/// network exposure, secret storage, backups). Feeds the "privacy" inspection
/// view.
/// </summary>
public sealed record PrivacyAuditItem(
    string Name,
    string Status,
    string Detail);
