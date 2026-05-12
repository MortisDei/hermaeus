using Aether.Core.Models;

namespace Aether.Core.Services;

public interface ILocalAiSetupService
{
    Task<LocalAiReadinessReport> ScanAsync(AppSettings settings, CancellationToken ct = default);

    Task<LocalAiSetupResult> RunActionAsync(
        LocalAiSetupAction action,
        AppSettings settings,
        bool allowOverwrite = false,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    string BuildXttsApiScript(string? modelDirectory = null, string? outputDirectory = null);
}
