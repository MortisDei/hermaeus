using Aether.Core.Models;

namespace Aether.Core.Services;

public interface IVoiceProvider
{
    VoiceProvider Id { get; }
    string DisplayName { get; }
    VoiceCapability Capabilities { get; }
    (int Major, int Minor) RequiredPythonVersion { get; }

    VoiceProviderDetection Detect();
    VoiceInstallPlan InstallPlan();

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task<VoiceHealth> HealthCheckAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VoiceDefinition>> ListVoicesAsync(CancellationToken ct = default);
    Task<VoiceSynthesisResult> GenerateSpeechAsync(VoiceSynthesisRequest request, CancellationToken ct = default);
}
