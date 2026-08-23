using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class SetupWizardVoiceReadinessTests
{
    [Fact]
    public void Installed_native_voice_stays_ready_across_navigation_and_view_model_recreation()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var registry = new InstalledNativeVoiceRegistry(settings);

        var wizard = new SetupWizardViewModel(
            settings, new RuntimeProfileService(settings), registry,
            new FakeDoctorService(), new FakeToasts(), new FakeSystemInfo());

        Assert.True(wizard.VoiceInstallCompleted);
        Assert.False(wizard.CanInstallSelectedVoiceProvider);
        wizard.StepIndex = 2;
        wizard.StepIndex = 3;
        Assert.True(wizard.VoiceInstallCompleted);
        Assert.False(wizard.CanInstallSelectedVoiceProvider);

        var recreated = new SetupWizardViewModel(
            settings, new RuntimeProfileService(settings), registry,
            new FakeDoctorService(), new FakeToasts(), new FakeSystemInfo());
        Assert.True(recreated.VoiceInstallCompleted);
        Assert.False(recreated.CanInstallSelectedVoiceProvider);
    }

    private sealed class InstalledNativeVoiceRegistry(ISettingsService settings) : IVoiceProviderRegistry
    {
        private readonly InstalledNativeVoiceProvider _provider = new();

        public IReadOnlyList<VoiceProviderInfo> GetAvailableProviders() =>
        [
            new(VoiceProvider.KokoroNative, "Kokoro (native)", "Installed native voice.",
                VoiceProviderCategory.Recommended, true, VoiceCapability.TextToSpeech | VoiceCapability.Local)
        ];

        public VoiceProvider GetActiveProvider() => VoiceProvider.KokoroNative;
        public IVoiceProvider GetActiveVoiceProvider() => _provider;
        public IVoiceProvider GetVoiceProvider(VoiceProvider provider) => _provider;
        public Task SetActiveProviderAsync(VoiceProvider provider)
        {
            settings.Settings.Tts.VoiceProvider = provider.ToString();
            return Task.CompletedTask;
        }
        public VoiceProviderConfig? GetProviderConfig(VoiceProvider provider) => new(provider.ToString());
        public Task SetProviderConfigAsync(VoiceProvider provider, VoiceProviderConfig config) => Task.CompletedTask;
        public ITtsService GetActiveTtsService() => throw new NotSupportedException();
    }

    private sealed class InstalledNativeVoiceProvider : IVoiceProvider
    {
        public VoiceProvider Id => VoiceProvider.KokoroNative;
        public string DisplayName => "Kokoro (native)";
        public VoiceCapability Capabilities => VoiceCapability.TextToSpeech | VoiceCapability.Local;
        public (int Major, int Minor)? RequiredPythonVersion => null;
        public bool IsInstalled => true;
        public VoiceProviderDetection Detect() => new(true, "Ready", "Installed");
        public VoiceInstallPlan InstallPlan() => new("Already installed", [], "Low");
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<VoiceHealth> HealthCheckAsync(CancellationToken ct = default) =>
            Task.FromResult(new VoiceHealth(VoiceHealthStatus.Healthy, "Ready", "Installed"));
        public Task<IReadOnlyList<VoiceDefinition>> ListVoicesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VoiceDefinition>>([]);
        public Task<VoiceSynthesisResult> GenerateSpeechAsync(VoiceSynthesisRequest request, CancellationToken ct = default) =>
            Task.FromResult(new VoiceSynthesisResult(true, "Done"));
    }
}
