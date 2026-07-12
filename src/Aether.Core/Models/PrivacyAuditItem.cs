namespace Aether.Core.Models;

/// <summary>
/// A privacy-relevant fact about the current configuration (remote providers,
/// network exposure, secret storage, backups). Feeds the System Overview
/// Privacy Audit panel.
/// </summary>
public sealed record PrivacyAuditItem(
    string Name,
    string Status,
    string Detail);
