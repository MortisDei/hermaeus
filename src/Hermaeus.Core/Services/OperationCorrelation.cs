namespace Hermaeus.Core.Services;

/// <summary>
/// Short, non-secret identifiers used to join the log entries for one
/// multi-stage operation. The identifier carries no user or machine data.
/// </summary>
public static class OperationCorrelation
{
    public static string NewId() => Guid.NewGuid().ToString("N")[..12];
}
