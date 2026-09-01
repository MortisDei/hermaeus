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
    private async Task<DoctorCheck> CheckVoiceBackendAsync(CancellationToken ct)
    {
        var provider = _voice.GetActiveVoiceProvider();
        var health = await provider.HealthCheckAsync(ct);
        var isNativeKokoro = provider.Id == VoiceProvider.KokoroNative;
        var nativeKokoro = provider as NativeKokoroVoiceProvider;
        var status = health.Status switch
        {
            VoiceHealthStatus.Healthy => DoctorCheckStatus.Ready,
            VoiceHealthStatus.Warning => DoctorCheckStatus.Warning,
            _ => DoctorCheckStatus.Error
        };

        return BuildCheck(
            isNativeKokoro ? "kokoro-native" : "voice-backend",
            isNativeKokoro ? "Kokoro (native) voice health" : "Voice backend health",
            status,
            health.Summary,
            health.Detail,
            isNativeKokoro
                ? health.Status == VoiceHealthStatus.Healthy
                    ? "Installed"
                    : nativeKokoro?.IsInstalled == true
                        ? "Retry Kokoro (native) health"
                        : "Install Kokoro (native)"
                : "Open Settings",
            !isNativeKokoro || health.Status != VoiceHealthStatus.Healthy,
            $"Provider: {provider.DisplayName}\n{health.Detail}",
            "Voice");
    }

    private async Task<DoctorCheck> CheckPythonAsync(CancellationToken ct)
    {
        var provider = _voice.GetActiveVoiceProvider();
        if (provider.RequiredPythonVersion is null)
        {
            return BuildCheck(
                "python",
                $"Python for {provider.DisplayName}",
                DoctorCheckStatus.Ready,
                "No Python interpreter required",
                $"{provider.DisplayName} does not use a Python subprocess.",
                "Open Settings",
                false,
                string.Empty,
                "Voice");
        }

        var python = _settings.Settings.Tts.PythonPath.Trim();
        var validator = PythonHealthValidator.ForProvider(provider);
        var report = await validator.ValidateAsync(python, ct);
        var status = report.IsHealthy ? DoctorCheckStatus.Ready : DoctorCheckStatus.Error;
        if (!report.IsHealthy && report.Issues.Any(i => i.Code == "version"))
            status = DoctorCheckStatus.Warning;

        var required = provider.RequiredPythonVersion.Value;
        var title = provider.MaxExclusivePythonVersion is { } maxExclusive && maxExclusive.Major == required.Major
            ? $"Python {required.Major}.{required.Minor}-{maxExclusive.Minor - 1} for {provider.DisplayName}"
            : $"Python {required.Major}.{required.Minor} for {provider.DisplayName}";

        return BuildCheck(
            "python",
            title,
            status,
            report.Summary,
            report.Detail,
            "Open Settings",
            true,
            report.Diagnostics,
            "Voice");
    }

    public async Task<bool> InstallNativeKokoroAssetsAsync(CancellationToken ct = default)
    {
        var provider = GetNativeKokoroProvider();
        if (provider is null)
            return false;

        return await provider.InstallAssetsAsync(null, ct);
    }

    public async Task<bool> InstallNativeKokoroAssetsAsync(IProgress<string> progress, CancellationToken ct = default)
    {
        var provider = GetNativeKokoroProvider();
        if (provider is null)
        {
            _runtimeLogs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Warning,
                RuntimeLogCategory.Service,
                "Kokoro native installation skipped: provider not available"));
            return false;
        }

        _runtimeLogs?.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            RuntimeLogLevel.Info,
            RuntimeLogCategory.Service,
            $"Kokoro native installation starting; assets root: {NativeKokoroVoiceProvider.ResolveAssetsDirectory(_settings.Settings)}"));

        var result = await provider.InstallAssetsAsync(progress, ct);
        _runtimeLogs?.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            result ? RuntimeLogLevel.Info : RuntimeLogLevel.Error,
            RuntimeLogCategory.Service,
            result ? "Kokoro native assets installed successfully" : "Kokoro native asset installation failed"));
        return result;
    }

    private NativeKokoroVoiceProvider? GetNativeKokoroProvider() =>
        _voice.GetVoiceProvider(VoiceProvider.KokoroNative) as NativeKokoroVoiceProvider;
}
