using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Rag.Storage;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Voice;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hermaeus.Services;

public sealed partial class DoctorService
{
    private async Task<DoctorCheck> CheckDataRootAsync(CancellationToken ct)
    {
        var root = SettingsService.ResolveDataRoot(_settings.Settings);
        var (ok, detail) = await TryWriteAsync(root, ct);
        return BuildCheck(
            "data-root",
            "Data root writable",
            ok ? DoctorCheckStatus.Ready : DoctorCheckStatus.Error,
            ok ? "Data root is writable" : "Data root is not writable",
            detail,
            "Open Settings",
            true,
            detail,
            "Storage");
    }

    private async Task<DoctorCheck> CheckAiAssetsRootAsync(CancellationToken ct)
    {
        var root = _settings.Settings.DataManagement.LocalAiAssetsRoot.Trim();
        if (string.IsNullOrWhiteSpace(root))
        {
            return BuildCheck(
                "ai-root",
                "AI assets root",
                DoctorCheckStatus.Warning,
                "AI assets root not set",
                "Choose a local AI assets folder in Settings.",
                "Open Settings",
                true,
                "AI assets root is empty.",
                "Storage");
        }

        var full = Path.GetFullPath(root);
        var exists = Directory.Exists(full);
        var (ok, detail) = exists ? await TryWriteAsync(full, ct) : (false, "Folder does not exist.");

        return BuildCheck(
            "ai-root",
            "AI assets root writable",
            ok ? DoctorCheckStatus.Ready : DoctorCheckStatus.Warning,
            ok ? "AI assets root is writable" : "AI assets root needs attention",
            exists ? detail : "Folder does not exist.",
            "Open Settings",
            true,
            detail,
            "Storage");
    }
}
