using System.Linq;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class InspectionEngine : IInspectionEngine
{
    private readonly IEnumerable<IInspectionCheckProvider> _providers;

    public InspectionEngine(IEnumerable<IInspectionCheckProvider> providers)
    {
        _providers = providers;
    }

    public async Task<InspectionReport> RunAsync(string? view = null, CancellationToken ct = default)
    {
        var scannedAt = DateTime.UtcNow;
        var checks = new List<InspectionCheck>();

        foreach (var provider in _providers)
        {
            if (view is not null && !provider.Views.Contains(view))
                continue;

            try
            {
                checks.AddRange(await provider.GetChecksAsync(ct));
            }
            catch (Exception ex)
            {
                checks.Add(new InspectionCheck(
                    Id: $"provider-error-{provider.GetType().Name}",
                    View: view ?? provider.Views.FirstOrDefault() ?? "unknown",
                    Category: "Inspection",
                    Title: $"{provider.GetType().Name} failed",
                    Severity: CheckSeverity.Error,
                    Summary: "This inspection provider threw an exception.",
                    Detail: ex.Message,
                    FixLabel: string.Empty,
                    CanFix: false,
                    Diagnostics: ex.ToString()));
            }
        }

        var errorCount = checks.Count(c => c.Severity == CheckSeverity.Error);
        var warningCount = checks.Count(c => c.Severity == CheckSeverity.Warning);
        var summary = errorCount == 0 && warningCount == 0
            ? "No issues found."
            : $"Found {errorCount} error(s) and {warningCount} warning(s).";

        return new InspectionReport(checks, scannedAt, summary);
    }
}
