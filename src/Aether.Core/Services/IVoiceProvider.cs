using Aether.Core.Models;

namespace Aether.Core.Services;

public interface IVoiceProvider
{
    VoiceProvider Id { get; }
    string DisplayName { get; }
    VoiceCapability Capabilities { get; }
    /// <summary>Null when the provider needs no Python interpreter (e.g. native ONNX or a remote API).</summary>
    (int Major, int Minor)? RequiredPythonVersion { get; }

    /// <summary>
    /// Exclusive upper bound on the minor version, for providers whose
    /// Python dependency stack breaks above a known ceiling (r11 1.7: XTTS
    /// v2 requires 3.9-3.11; coqui TTS does not support 3.12+). Null means
    /// no known ceiling - any minor at or above <see cref="RequiredPythonVersion"/>
    /// is accepted, same as before this existed.
    /// </summary>
    (int Major, int Minor)? MaxExclusivePythonVersion => null;

    bool IsInstalled { get; }

    VoiceProviderDetection Detect();
    VoiceInstallPlan InstallPlan();

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task<VoiceHealth> HealthCheckAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VoiceDefinition>> ListVoicesAsync(CancellationToken ct = default);
    Task<VoiceSynthesisResult> GenerateSpeechAsync(VoiceSynthesisRequest request, CancellationToken ct = default);
}
