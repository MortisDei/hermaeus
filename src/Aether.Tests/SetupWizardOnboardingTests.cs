using System.Net.Http;
using System.Security.Cryptography;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using Aether.ViewModels;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

/// <summary>
/// docs/review/02-onboarding-and-usability.md items 2.1 (guided starter
/// model download) and 2.2 (voice install from the wizard).
/// </summary>
internal static class SetupWizardOnboardingTests
{
    private static string Sha256Hex(string content) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    // ── 2.1 StarterModelCatalog.Recommend ────────────────────────────────────

    public static Task RecommendReturnsSmallTierWhenNoGpuIsPresent()
    {
        var snapshot = new SystemSnapshot { Gpus = [] };
        Equal(StarterModelCatalog.Small.Id, StarterModelCatalog.Recommend(snapshot).Id, "No GPU must recommend the smallest tier.");
        return Task.CompletedTask;
    }

    public static Task RecommendTreatsUnavailableGpuProbeAsNoGpu()
    {
        var snapshot = new SystemSnapshot
        {
            Gpus = [new GpuInfo { Name = "GPU probe unavailable", Provider = "best-effort", Status = "unavailable", MemoryTotalBytes = null }]
        };
        Equal(StarterModelCatalog.Small.Id, StarterModelCatalog.Recommend(snapshot).Id, "An 'unavailable' GPU probe row must fall back to the smallest tier.");
        return Task.CompletedTask;
    }

    public static Task RecommendReturnsSmallTierForLowVram()
    {
        var snapshot = new SystemSnapshot { Gpus = [new GpuInfo { Name = "Test GPU", Status = "ok", MemoryTotalBytes = 4L * 1024 * 1024 * 1024 }] };
        Equal(StarterModelCatalog.Small.Id, StarterModelCatalog.Recommend(snapshot).Id, "Under 6 GB VRAM must recommend the smallest tier.");
        return Task.CompletedTask;
    }

    public static Task RecommendReturnsMediumTierForMidVram()
    {
        var snapshot = new SystemSnapshot { Gpus = [new GpuInfo { Name = "Test GPU", Status = "ok", MemoryTotalBytes = 8L * 1024 * 1024 * 1024 }] };
        Equal(StarterModelCatalog.Medium.Id, StarterModelCatalog.Recommend(snapshot).Id, "6-12 GB VRAM must recommend the medium tier.");
        return Task.CompletedTask;
    }

    public static Task RecommendReturnsLargeTierForHighVram()
    {
        var snapshot = new SystemSnapshot { Gpus = [new GpuInfo { Name = "Test GPU", Status = "ok", MemoryTotalBytes = 16L * 1024 * 1024 * 1024 }] };
        Equal(StarterModelCatalog.Large.Id, StarterModelCatalog.Recommend(snapshot).Id, "Over 12 GB VRAM must recommend the largest tier.");
        return Task.CompletedTask;
    }

    public static Task CatalogEntriesDeclareHttpsUrlsAndSha256Hashes()
    {
        foreach (var entry in StarterModelCatalog.All)
        {
            True(entry.DownloadUrl.StartsWith("https://", StringComparison.Ordinal), $"{entry.Id} must use an https download URL.");
            Equal(64, entry.Sha256.Length, $"{entry.Id} must declare a full 64-character SHA256 hash.");
            True(entry.SizeBytes > 0, $"{entry.Id} must declare a positive size.");
            True(entry.DisplayName.Contains("GB", StringComparison.Ordinal), $"{entry.Id}'s display name should state its approximate size so the wizard shows it before download.");
        }
        return Task.CompletedTask;
    }

    // ── 2.1 Wizard download flow ─────────────────────────────────────────────

    public static async Task WizardDownloadsStarterModelVerifiesHashAndSetsModelPath()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("ai-assets");

        const string content = "fake-gguf-content-for-testing";
        var downloads = new ModelDownloadService(new HttpClient(new CapturingRangeHttpHandler(content)));
        var wizard = new SetupWizardViewModel(
            settings,
            new RuntimeProfileService(settings),
            new FakeVoiceProviderRegistry(settings),
            new FakeDoctorService(),
            new FakeToasts(),
            new FakeSystemInfo(),
            downloads);

        var entry = new StarterModelEntry("test-model", "Test Model (1.0 GB)", "test-model.gguf", "https://example.test/test-model.gguf", content.Length, Sha256Hex(content));
        wizard.RecommendedStarterModel = entry;

        await wizard.DownloadStarterModelCommand.ExecuteAsync(null);

        True(wizard.StarterModelDownloadCompleted, $"Download should complete successfully. Error: {wizard.StarterModelDownloadError}");
        True(wizard.ModelFolder.EndsWith("test-model.gguf", StringComparison.Ordinal), $"ModelFolder should point at the downloaded file, got: {wizard.ModelFolder}");
        True(File.Exists(wizard.ModelFolder), "Downloaded file should exist on disk.");
        Equal(content, await File.ReadAllTextAsync(wizard.ModelFolder), "Downloaded file content should match what the server sent.");
    }

    public static async Task WizardStarterModelDownloadDeletesFileAndReportsErrorOnHashMismatch()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("ai-assets");
        var originalModelFolder = settings.Settings.ManagedServers.FirstOrDefault()?.ModelPath ?? string.Empty;

        var downloads = new ModelDownloadService(new HttpClient(new CapturingRangeHttpHandler("wrong-content")));
        var wizard = new SetupWizardViewModel(
            settings,
            new RuntimeProfileService(settings),
            new FakeVoiceProviderRegistry(settings),
            new FakeDoctorService(),
            new FakeToasts(),
            new FakeSystemInfo(),
            downloads);

        var entry = new StarterModelEntry("test-model", "Test Model (1.0 GB)", "test-model.gguf", "https://example.test/test-model.gguf", 5, Sha256Hex("correct-content"));
        wizard.RecommendedStarterModel = entry;

        await wizard.DownloadStarterModelCommand.ExecuteAsync(null);

        False(wizard.StarterModelDownloadCompleted, "Download must not be marked complete when the hash check fails.");
        True(wizard.StarterModelDownloadError.Length > 0, "An error message must be set when verification fails.");
        Equal(originalModelFolder, wizard.ModelFolder, "ModelFolder (and therefore persisted settings) must be untouched when verification fails.");

        var expectedPath = Path.Combine(temp.PathFor("ai-assets/Models/chat"), "test-model.gguf");
        False(File.Exists(expectedPath), "The file that failed hash verification must be deleted, not left on disk.");
    }

    // ── 2.2 Voice install from the wizard ────────────────────────────────────

    private static VoiceProviderInfo KokoroNativeInfo() => new(
        VoiceProvider.KokoroNative, "Kokoro (native)", "In-process native voice.",
        VoiceProviderCategory.Recommended, false, VoiceCapability.TextToSpeech | VoiceCapability.Local);

    private static VoiceProviderInfo OpenAiInfo() => new(
        VoiceProvider.OpenAi, "OpenAI", "Remote voice.",
        VoiceProviderCategory.Advanced, false, VoiceCapability.TextToSpeech | VoiceCapability.Remote);

    public static Task OnlyKokoroNativeCanInstallFromWizard()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var wizard = new SetupWizardViewModel(
            settings, new RuntimeProfileService(settings), new FakeVoiceProviderRegistry(settings),
            new FakeDoctorService(), new FakeToasts(), new FakeSystemInfo());

        wizard.SelectedVoiceProvider = OpenAiInfo();
        False(wizard.CanInstallSelectedVoiceProvider, "Only Kokoro (native) should offer an automated in-wizard install.");

        wizard.SelectedVoiceProvider = KokoroNativeInfo();
        True(wizard.CanInstallSelectedVoiceProvider, "Kokoro (native) must offer an automated in-wizard install.");
        return Task.CompletedTask;
    }

    public static async Task WizardVoiceInstallCallsTheSameDoctorEntryPointAsSettings()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var doctor = new FakeDoctorServiceWithKokoroInstallTracking();
        var wizard = new SetupWizardViewModel(
            settings, new RuntimeProfileService(settings), new FakeVoiceProviderRegistry(settings),
            doctor, new FakeToasts(), new FakeSystemInfo());
        wizard.SelectedVoiceProvider = KokoroNativeInfo();

        True(wizard.VoiceReadinessSummary.Contains("not installed", StringComparison.OrdinalIgnoreCase), "Summary should say voice is not installed before an install runs.");

        await wizard.InstallVoiceCommand.ExecuteAsync(null);

        // DoctorViewModel's own "kokoro-native" fix action calls this exact
        // same IDoctorService method; asserting the call here is what makes
        // the wizard's install byte-identical in effect to Settings/Doctor.
        Equal(1, doctor.InstallCallCount, "Voice install must call IDoctorService.InstallNativeKokoroAssetsAsync exactly once.");
        True(doctor.ProgressMessages.Count > 0, "Install progress should be forwarded to the wizard.");
        True(wizard.VoiceInstallCompleted, "Wizard should report the install completed.");
        True(wizard.VoiceReadinessSummary.Contains("ready", StringComparison.OrdinalIgnoreCase), "Finish summary should confirm voice is ready after a successful install.");
    }

    public static async Task WizardVoiceInstallFailureLeavesFinishLaterMessage()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var doctor = new FakeDoctorServiceWithKokoroInstallTracking { InstallResult = false };
        var wizard = new SetupWizardViewModel(
            settings, new RuntimeProfileService(settings), new FakeVoiceProviderRegistry(settings),
            doctor, new FakeToasts(), new FakeSystemInfo());
        wizard.SelectedVoiceProvider = KokoroNativeInfo();

        await wizard.InstallVoiceCommand.ExecuteAsync(null);

        Equal(1, doctor.InstallCallCount, "The install method must still be called even though it will report failure.");
        False(wizard.VoiceInstallCompleted, "Install must not be marked complete on failure.");
        True(wizard.VoiceInstallError.Length > 0, "An error message must be set on failure.");
        True(wizard.VoiceReadinessSummary.Contains("later", StringComparison.OrdinalIgnoreCase), "Finish summary should point to finishing later in Settings > Voice.");
    }
}
