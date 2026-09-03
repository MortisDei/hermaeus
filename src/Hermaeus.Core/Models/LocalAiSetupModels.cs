namespace Hermaeus.Core.Models;

public enum LocalAiReadinessStatus
{
    Found,
    Missing,
    Optional,
    NeedsAction
}

public enum LocalAiSetupActionKind
{
    CreateVenv,
    InstallXttsDependencies,
    CreateXttsApiScript,
    CreateDirectory,
    DownloadGgufModel,
    DownloadTtsModel,
    DownloadLlamaServer
}

public enum LocalAiSetupRiskLevel
{
    Low,
    Medium,
    High
}

public sealed record LocalAiReadinessItem(
    string Key,
    string Label,
    LocalAiReadinessStatus Status,
    string Detail,
    string Hint,
    bool Required)
{
    public string StatusLabel => Status switch
    {
        LocalAiReadinessStatus.Found => "Ready",
        LocalAiReadinessStatus.Missing => "Missing",
        LocalAiReadinessStatus.Optional => "Optional",
        LocalAiReadinessStatus.NeedsAction => "Needs action",
        _ => Status.ToString()
    };
}

public sealed record LocalAiSetupAction(
    string Id,
    LocalAiSetupActionKind Kind,
    string Title,
    string TargetPath,
    IReadOnlyList<string> CommandPreview,
    LocalAiSetupRiskLevel RiskLevel,
    string ExpectedResult,
    bool RequiresNetwork,
    bool RequiresApproval,
    bool CanRun)
{
    public bool PlanReviewed { get; init; }
    public bool CanApprove => CanRun && (!RequiresApproval || PlanReviewed);
    public string RiskLabel => RiskLevel.ToString();
    public string CommandPreviewText => CommandPreview.Count == 0
        ? "Hermaeus file operation"
        : string.Join(" ", CommandPreview.Select(QuoteIfNeeded));

    private static string QuoteIfNeeded(string value) =>
        value.Contains(" ", StringComparison.Ordinal) ? $"\"{value}\"" : value;
}

public sealed record LocalAiReadinessReport(
    string Root,
    IReadOnlyList<LocalAiReadinessItem> Items,
    IReadOnlyList<LocalAiSetupAction> Actions,
    string Summary,
    string SetupCommands);

public sealed record LocalAiSetupResult(
    bool Success,
    string Log,
    string? UpdatedPath = null,
    LlamaRuntimeVariant? SelectedVariant = null,
    string? VerifiedReleaseTag = null,
    string? VerifiedArtifactSha256 = null);
