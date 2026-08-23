using System.Net.Http;
using System.Security.Cryptography;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

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
        // The low tier stays small enough for CPU-only machines.
        Equal(StarterModelCatalog.Phi4Mini.Id, StarterModelCatalog.Recommend(snapshot).Id, "No GPU must recommend the permissively licensed low tier.");
        return Task.CompletedTask;
    }

    public static Task RecommendTreatsUnavailableGpuProbeAsNoGpu()
    {
        var snapshot = new SystemSnapshot
        {
            Gpus = [new GpuInfo { Name = "GPU probe unavailable", Provider = "best-effort", Status = "unavailable", MemoryTotalBytes = null }]
        };
        Equal(StarterModelCatalog.Phi4Mini.Id, StarterModelCatalog.Recommend(snapshot).Id, "An 'unavailable' GPU probe row must fall back to the low tier.");
        return Task.CompletedTask;
    }

    public static Task RecommendReturnsSmallTierForLowVram()
    {
        var snapshot = new SystemSnapshot { Gpus = [new GpuInfo { Name = "Test GPU", Status = "ok", MemoryTotalBytes = 4L * 1024 * 1024 * 1024 }] };
        Equal(StarterModelCatalog.Phi4Mini.Id, StarterModelCatalog.Recommend(snapshot).Id, "Under 6 GB VRAM must recommend the low tier.");
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
            // An entry with no licence would offer a download with no way for
            // the user to know what they are taking on.
            True(entry.HasLicense, $"{entry.Id} must declare the base model's licence; the wizard shows it before download.");
            True(entry.LicenseUrl.StartsWith("https://", StringComparison.Ordinal), $"{entry.Id} must link its licence over https.");
        }

        // Distinct ids, so the wizard's picker cannot show two entries that
        // select each other, and distinct file names, so two downloads cannot
        // land on the same path.
        Equal(StarterModelCatalog.All.Count, StarterModelCatalog.All.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count(),
            "starter model ids must be unique");
        Equal(StarterModelCatalog.All.Count, StarterModelCatalog.All.Select(e => e.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "starter model file names must be unique");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 0.36.0-alpha: the wizard offers a choice of starter models, not one
    /// recommendation, because they differ in family and size. The hardware probe
    /// finishes after the wizard is constructed, so the selection has to follow
    /// a recommendation that arrives late, and stop following it the moment the
    /// user picks for themselves.
    /// </summary>
    public static async Task WizardStarterModelSelectionFollowsTheRecommendationUntilTheUserChooses()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var wizard = new SetupWizardViewModel(
            settings,
            new RuntimeProfileService(settings),
            new FakeVoiceProviderRegistry(settings),
            new FakeDoctorService(),
            new FakeToasts(),
            new FakeSystemInfo(),
            new ModelDownloadService(new HttpClient(new CapturingRangeHttpHandler("x"))));

        // A recommendation arriving late moves the selection with it.
        wizard.RecommendedStarterModel = StarterModelCatalog.Medium;
        Equal(StarterModelCatalog.Medium.Id, wizard.SelectedStarterModel?.Id ?? string.Empty,
            "an untouched selection must follow the recommendation");
        True(wizard.SelectedStarterModelIsRecommended, "the untouched selection is the recommended one");

        // Once the user picks, a later recommendation must not overrule them.
        wizard.SelectedStarterModel = StarterModelCatalog.Phi4Mini;
        wizard.RecommendedStarterModel = StarterModelCatalog.Large;

        Equal(StarterModelCatalog.Phi4Mini.Id, wizard.SelectedStarterModel?.Id ?? string.Empty,
            "the user's own choice must survive a later recommendation");
        False(wizard.SelectedStarterModelIsRecommended, "the user's choice is not the recommended one");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Every entry is offered with its licence, even though the current small
    /// catalogue contains only permissive licences.
    /// </summary>
    public static Task CatalogOffersModelsUnderMoreThanOneLicence()
    {
        var licences = StarterModelCatalog.All.Select(e => e.License).Distinct(StringComparer.Ordinal).ToList();
        True(licences.Count > 1, "the starter catalog should retain explicit licence metadata from more than one model family");
        True(StarterModelCatalog.All.Any(e => e.License == "MIT" || e.License == "Apache-2.0"),
            "at least one starter model must be permissively licensed");
        False(StarterModelCatalog.All.Any(entry => entry.Id.Contains("qwen2.5", StringComparison.OrdinalIgnoreCase)),
            "the starter catalogue must not regress to Qwen2.5");
        True(StarterModelCatalog.Gemma4_E2B.DisplayName.Contains("QAT", StringComparison.Ordinal),
            "Gemma 4 E2B QAT must be available");
        True(StarterModelCatalog.Medium.Id == StarterModelCatalog.Gemma4_E4B.Id
             && StarterModelCatalog.Medium.DisplayName.Contains("QAT", StringComparison.Ordinal),
            "the 6-12 GB recommendation must be Gemma 4 E4B QAT");
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
