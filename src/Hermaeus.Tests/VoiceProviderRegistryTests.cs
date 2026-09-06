using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Hermaeus.Voice;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class VoiceProviderRegistryTests
{
    [Theory]
    [InlineData("Kokoro", VoiceProvider.Kokoro)]
    [InlineData("F5-TTS", VoiceProvider.F5Tts)]
    [InlineData("XTTS v2", VoiceProvider.XttsV2)]
    [InlineData("OpenAI", VoiceProvider.OpenAi)]
    [InlineData("KokoroNative", VoiceProvider.KokoroNative)]
    [InlineData("Kokoro (Python)", VoiceProvider.Kokoro)]
    [InlineData("Kokoro (native)", VoiceProvider.KokoroNative)]
    public void Constructor_accepts_persisted_provider_aliases(string configured, VoiceProvider expected)
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.Tts.VoiceProvider = configured;
        var logs = new RuntimeLogService(settings);

        var registry = CreateRegistry(settings, logs);

        Assert.Equal(expected, registry.GetActiveProvider());
        Assert.DoesNotContain(logs.GetEntries(), entry => entry.Level == RuntimeLogLevel.Warning);
    }

    [Fact]
    public void Constructor_defaults_an_unknown_provider_and_records_a_warning()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.Tts.VoiceProvider = "not-a-provider";
        var logs = new RuntimeLogService(settings);

        var registry = CreateRegistry(settings, logs);

        Assert.Equal(VoiceProvider.KokoroNative, registry.GetActiveProvider());
        Assert.Contains(logs.GetEntries(), entry => entry.Message.Contains("not-a-provider", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Setters_persist_active_provider_and_provider_config()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var registry = CreateRegistry(settings, new RuntimeLogService(settings));
        var config = new VoiceProviderConfig("OpenAi", new Dictionary<string, string>
        {
            ["model"] = "gpt-4o-mini-tts",
            ["voice"] = "alloy"
        });

        await registry.SetActiveProviderAsync(VoiceProvider.OpenAi);
        await registry.SetProviderConfigAsync(VoiceProvider.OpenAi, config);

        Assert.Equal(VoiceProvider.OpenAi, registry.GetActiveProvider());
        Assert.Equal("OpenAi", settings.Settings.Tts.VoiceProvider);
        Assert.Same(config, registry.GetProviderConfig(VoiceProvider.OpenAi));
        Assert.True(File.Exists(temp.PathFor("settings/settings.json")));
    }

    [Fact]
    public void Available_providers_expose_the_expected_catalog_and_registered_services()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var registry = CreateRegistry(settings, new RuntimeLogService(settings));

        var providers = registry.GetAvailableProviders();

        Assert.Equal(5, providers.Count);
        Assert.Equal(VoiceProvider.KokoroNative, providers[0].Id);
        Assert.Equal(VoiceProviderCategory.Recommended, providers[0].Category);
        Assert.All(providers.Skip(1), provider => Assert.Equal(VoiceProviderCategory.Advanced, provider.Category));
        Assert.All(providers, provider =>
        {
            var service = registry.GetVoiceProvider(provider.Id);
            Assert.Equal(provider.Id, service.Id);
            Assert.Equal(service.Capabilities, provider.Capabilities);
        });
        Assert.Same(registry.GetActiveVoiceProvider(), registry.GetActiveTtsService());
    }

    [Fact]
    public void Kokoro_install_state_uses_python_and_native_assets_independently()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("ai-assets");
        settings.Settings.Tts.PythonPath = temp.PathFor("python.exe");
        settings.Settings.Tts.Speaker = "af_heart";
        File.WriteAllText(settings.Settings.Tts.PythonPath, "python placeholder");

        var nativeRoot = NativeKokoroVoiceProvider.ResolveAssetsDirectory(settings.Settings);
        Directory.CreateDirectory(Path.Combine(nativeRoot, "voices"));
        File.WriteAllText(Path.Combine(nativeRoot, "model_q8f16.onnx"), "model placeholder");
        File.WriteAllText(Path.Combine(nativeRoot, "voices", "af_heart.bin"), "voice placeholder");

        var registry = CreateRegistry(settings, new RuntimeLogService(settings));
        var providers = registry.GetAvailableProviders();

        Assert.True(providers.Single(p => p.Id == VoiceProvider.Kokoro).IsInstalled,
            "The Python provider must use its Python executable state.");
        Assert.True(providers.Single(p => p.Id == VoiceProvider.KokoroNative).IsInstalled,
            "The native provider must use its own model and voice root.");

        File.Delete(Path.Combine(nativeRoot, "model_q8f16.onnx"));
        File.Delete(Path.Combine(nativeRoot, "voices", "af_heart.bin"));
        providers = registry.GetAvailableProviders();
        Assert.True(providers.Single(p => p.Id == VoiceProvider.Kokoro).IsInstalled,
            "Removing native assets must not alter the Python provider state.");
        Assert.False(providers.Single(p => p.Id == VoiceProvider.KokoroNative).IsInstalled,
            "Native install state must change only with native assets.");
    }

    [Fact]
    public async Task Provider_specific_settings_are_keyed_by_distinct_provider_ids()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var registry = CreateRegistry(settings, new RuntimeLogService(settings));

        var python = new VoiceProviderConfig(VoiceProviderIdentity.CanonicalId(VoiceProvider.Kokoro),
            new Dictionary<string, string> { ["backend"] = "python" });
        var native = new VoiceProviderConfig(VoiceProviderIdentity.CanonicalId(VoiceProvider.KokoroNative),
            new Dictionary<string, string> { ["backend"] = "native" });

        await registry.SetProviderConfigAsync(VoiceProvider.Kokoro, python);
        await registry.SetProviderConfigAsync(VoiceProvider.KokoroNative, native);

        Assert.Same(python, registry.GetProviderConfig(VoiceProvider.Kokoro));
        Assert.Same(native, registry.GetProviderConfig(VoiceProvider.KokoroNative));
        Assert.Equal("python", settings.Settings.VoiceProviderConfigs["Kokoro"].Settings!["backend"]);
        Assert.Equal("native", settings.Settings.VoiceProviderConfigs["KokoroNative"].Settings!["backend"]);
    }

    private static VoiceProviderRegistry CreateRegistry(ISettingsService settings, IRuntimeLogService logs) =>
        new(
            settings,
            new XttsV2VoiceProvider(settings, new XttsProcessManager()),
            new KokoroVoiceProvider(settings, new KokoroProcessManager()),
            new F5TtsVoiceProvider(settings),
            new OpenAiVoiceProvider(settings, new FakeSecretStore()),
            new NativeKokoroVoiceProvider(settings),
            logs);
}
