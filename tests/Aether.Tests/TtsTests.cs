using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using Aether.Services.ProcessManagement;
using Aether.ViewModels;
using CommunityToolkit.Mvvm.Input;

static async Task VoiceProviderCapabilityGating()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    var toasts = new FakeToasts();
    var vm = new TtsSettingsViewModel(new FakeTts(), new FakeVoiceProviderRegistry(settings), toasts, new XttsProcessManager(), new FakeSecretStore(), settings);

    var limited = new VoiceProviderInfo(VoiceProvider.Kokoro, "Kokoro-Limited", "No TTS", VoiceProviderCategory.Recommended, true, VoiceCapability.Local);
    var asyncCmd = vm.SetActiveVoiceProviderCommand as IAsyncRelayCommand;
    if (asyncCmd is not null)
        await asyncCmd.ExecuteAsync(limited);
    else
        vm.SetActiveVoiceProviderCommand.Execute(limited);

    Equal("Kokoro", vm.SelectedVoiceProvider, "provider without TTS should not become active");
}

static async Task VoiceProviderLegacyRequiresLocalAndTts()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    var toasts = new FakeToasts();
    settings.Settings.Tts.VoiceProvider = "XTTS v2";
    var limitedRegistry = new FakeVoiceProviderRegistryLimited(settings);
    var vm = new TtsSettingsViewModel(new FakeTts(), limitedRegistry, toasts, new XttsProcessManager(), new FakeSecretStore(), settings);

    vm.ReloadFrom(settings.Settings);

    False(vm.IsLegacyVoiceBackend, "XTTS without Local+TTS should not be considered legacy backend");
    False(vm.StartTtsCommand.CanExecute(null), "StartTts should not be executable for non-legacy provider");
    False(vm.StopTtsCommand.CanExecute(null), "StopTts should not be executable for non-legacy provider");

    await Task.CompletedTask;
}

sealed class FakeVoiceProviderRegistryLimited : IVoiceProviderRegistry
{
    private readonly ISettingsService _settings;

    public FakeVoiceProviderRegistryLimited(ISettingsService settings) => _settings = settings;

    public IReadOnlyList<VoiceProviderInfo> GetAvailableProviders() =>
        new List<VoiceProviderInfo>
        {
            // XTTS v2 present but intentionally missing TextToSpeech and Local capabilities
            new VoiceProviderInfo(VoiceProvider.XttsV2, "XTTS v2", "Legacy cloning but missing flags.", VoiceProviderCategory.Legacy, true, VoiceCapability.VoiceCloning)
        };

    public VoiceProvider GetActiveProvider() => Enum.TryParse<VoiceProvider>(_settings.Settings.Tts.VoiceProvider, out var provider)
        ? provider
        : VoiceProvider.Kokoro;

    public IVoiceProvider GetActiveVoiceProvider() => new FakeVoiceProvider();

    public IVoiceProvider GetVoiceProvider(VoiceProvider provider) => new FakeVoiceProvider();

    public Task SetActiveProviderAsync(VoiceProvider provider)
    {
        _settings.Settings.Tts.VoiceProvider = provider.ToString();
        return Task.CompletedTask;
    }

    public VoiceProviderConfig? GetProviderConfig(VoiceProvider provider) => new(provider.ToString());

    public Task SetProviderConfigAsync(VoiceProvider provider, VoiceProviderConfig config) => Task.CompletedTask;

    public ITtsService GetActiveTtsService() => new FakeTts();
}
