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
    private async Task<DoctorCheck> CheckGpuAsync(CancellationToken ct)
    {
        var snapshot = await _systemInfo.CaptureAsync(ct);
        var gpu = snapshot.Gpus.FirstOrDefault();
        if (gpu is null)
        {
            return BuildCheck(
                "gpu",
                "GPU visibility",
                DoctorCheckStatus.Warning,
                "No GPU detected",
                "GPU probe returned no devices.",
                "Open System",
                true,
                "No GPU detected.",
                "System");
        }

        return BuildCheck(
            "gpu",
            "GPU visibility",
            DoctorCheckStatus.Ready,
            $"GPU: {gpu.Name}",
            gpu.Status,
            "Open System",
            true,
            $"GPU: {gpu.Name}\n{gpu.Status}",
            "System");
    }

    private async Task<DoctorCheck> CheckSecretsAsync(CancellationToken ct)
    {
        try
        {
            var backend = await _secrets.BackendLabelAsync(ct);
            return BuildCheck(
                "secrets",
                "Secrets backend",
                DoctorCheckStatus.Ready,
                "Secrets backend ready",
                backend,
                "Open Settings",
                true,
                backend,
                "Security");
        }
        catch (Exception ex)
        {
            return BuildCheck(
                "secrets",
                "Secrets backend",
                DoctorCheckStatus.Warning,
                "Secrets backend unavailable",
                ex.Message,
                "Open Settings",
                true,
                ex.ToString(),
                "Security");
        }
    }

    private DoctorCheck CheckTraySupport()
    {
        // Windows (Shell_NotifyIcon) and macOS (NSStatusItem) reliably support tray icons.
        // Linux support depends on the desktop environment/app-indicator availability, so it
        // stays advisory rather than a confirmed pass.
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            return BuildCheck(
                "tray",
                "Tray support",
                DoctorCheckStatus.Ready,
                "Tray supported",
                "Tray icons are supported on this OS.",
                "Details",
                false,
                Environment.OSVersion.ToString(),
                "System");
        }

        var supported = OperatingSystem.IsLinux();
        return BuildCheck(
            "tray",
            "Tray support",
            supported ? DoctorCheckStatus.Info : DoctorCheckStatus.Warning,
            supported ? "Tray likely supported" : "Tray not supported",
            supported ? "Depends on the desktop environment." : "Tray icons are not supported on this OS.",
            "Details",
            false,
            Environment.OSVersion.ToString(),
            "System");
    }

    private DoctorCheck CheckHotkeySupport()
    {
        var supported = OperatingSystem.IsWindows();
        return BuildCheck(
            "hotkeys",
            "Hotkey support",
            supported ? DoctorCheckStatus.Ready : DoctorCheckStatus.Warning,
            supported ? "System-wide hotkeys supported" : "System-wide hotkeys unavailable",
            supported ? "Windows only for now." : "Global hotkeys are disabled on this OS.",
            "Details",
            false,
            Environment.OSVersion.ToString(),
            "System");
    }
}
