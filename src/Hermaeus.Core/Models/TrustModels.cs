namespace Hermaeus.Core.Models;

public enum TrustItemStatus
{
    Ready,
    Missing,
    Warning,
    Info
}

public enum TrustRiskLevel
{
    Low,
    Medium,
    High
}

public sealed record TrustItem(
    string Category,
    string Label,
    string Target,
    string ResolvedTarget,
    TrustItemStatus Status,
    TrustRiskLevel RiskLevel,
    bool? IsInsideAiRoot,
    string Sha256,
    string Recommendation,
    DateTime ScannedAt)
{
    public string StatusLabel => Status switch
    {
        TrustItemStatus.Ready => "Ready",
        TrustItemStatus.Missing => "Missing",
        TrustItemStatus.Warning => "Warning",
        TrustItemStatus.Info => "Info",
        _ => Status.ToString()
    };

    public string RiskLabel => RiskLevel.ToString();

    public string ScopeLabel => IsInsideAiRoot switch
    {
        true => "Inside AI root",
        false => "Outside AI root",
        _ => "AI root unset"
    };

    public string ShaShort => string.IsNullOrWhiteSpace(Sha256)
        ? "No file hash"
        : Sha256[..Math.Min(16, Sha256.Length)];
}

public sealed record TrustScanReport(
    IReadOnlyList<TrustItem> Items,
    string Summary,
    DateTime ScannedAt)
{
    public int WarningCount => Items.Count(i => i.Status == TrustItemStatus.Warning);
    public int MissingCount => Items.Count(i => i.Status == TrustItemStatus.Missing);
}
