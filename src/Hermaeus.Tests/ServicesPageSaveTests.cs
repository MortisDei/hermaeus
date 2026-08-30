using Xunit;
using Hermaeus.Services;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r29 doc 01 1.1: the Services page hosts the Voice and STT cards, which are
/// the same DI singletons the Settings page edits. Nothing on Services wrote
/// them to disk, so every edit made there was discarded on restart. Save on
/// Services routes through the single existing save flow.
/// </summary>
public sealed class ServicesPageSaveTests
{
    [Fact]
    public async Task Save_on_services_runs_the_settings_save_flow_once_and_confirms()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = NewServicesViewModel(settings);

        var calls = 0;
        vm.SaveAllSettings = () => { calls++; return Task.CompletedTask; };

        await vm.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal(1, calls);
        Assert.True(vm.IsSaved);
    }

    [Fact]
    public async Task Save_on_services_without_a_wired_save_flow_does_nothing_rather_than_throwing()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = NewServicesViewModel(settings);

        await vm.SaveSettingsCommand.ExecuteAsync(null);

        Assert.False(vm.IsSaved);
    }

    /// <summary>
    /// The regression that would have caught the defect: assert on the value
    /// that reached ISettingsService, not on the view model property. A voice
    /// field edited through the Services page's shared Tts view model must
    /// survive the save the Services page now performs.
    /// </summary>
    [Fact]
    public async Task A_voice_field_edited_on_services_reaches_persisted_settings()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var tts = NewTtsSettingsViewModel(settings);
        var settingsVm = NewSettingsViewModel(settings, new FakeSecretStore(), tts);
        var servicesVm = NewServicesViewModel(settings, tts);
        servicesVm.SaveAllSettings = settingsVm.SaveAsync;

        servicesVm.Tts.TtsServiceUrl = "http://127.0.0.1:9911";
        servicesVm.Tts.TtsSpeaker = "narrator";
        servicesVm.Tts.TtsSpeed = 1.25;
        servicesVm.Tts.TtsDevice = "cuda";

        await servicesVm.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal("http://127.0.0.1:9911", settings.Settings.Tts.ServiceUrl);
        Assert.Equal("narrator", settings.Settings.Tts.Speaker);
        Assert.Equal(1.25, settings.Settings.Tts.Speed);
        Assert.Equal("cuda", settings.Settings.Tts.Device);

        var reloaded = NewSettings(temp);
        await reloaded.LoadAsync();
        Assert.Equal("cuda", reloaded.Settings.Tts.Device);
    }
}
