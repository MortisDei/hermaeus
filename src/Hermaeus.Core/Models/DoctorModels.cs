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

public sealed record DoctorCheck(
    string Key,
    string Title,
    DoctorCheckStatus Status,
    string Summary,
    string Detail,
    string FixLabel,
    bool CanFix,
    string Diagnostics,
    string Category)
{
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
