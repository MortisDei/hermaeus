using Aether.Agent.Services;
using Aether.Core.Models;
using Aether.Services;
using Aether.Services.ProcessManagement;
using Aether.ViewModels;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

public sealed class VoiceSettingsTests
{
    [Fact]
    public async Task LoadAsync_with_pre_r5_settings_json_defaults_chat_enabled_and_other_channels_disabled()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("settings/settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """{"Tts":{"Enabled":true,"VoiceProvider":"KokoroNative","Speaker":"af_heart"}}""");

        var service = new SettingsService(path);
        await service.LoadAsync();

        Assert.True(service.Settings.Tts.Enabled);
        Assert.False(service.Settings.Tts.AutoSpeakChatReplies);
        Assert.False(service.Settings.Tts.StreamingChatSpeech);
        Assert.Empty(service.Settings.Tts.Profiles);
        Assert.Empty(service.Settings.Tts.Channels);
    }

    [Fact]
    public void ReloadFrom_defaults_chat_channel_enabled_when_no_channel_config_is_stored()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = NewTtsVm(settings);

        vm.ReloadFrom(settings.Settings);

        Assert.True(vm.VoiceChannels.Single(c => c.Channel == VoiceChannel.Chat).Enabled);
        Assert.False(vm.VoiceChannels.Single(c => c.Channel == VoiceChannel.Agent).Enabled);
    }

    [Fact]
    public void ReloadFrom_and_ApplyVoiceOrchestrationTo_round_trip_profiles_and_channels()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Tts.Profiles.Add(new VoiceProfile { Name = "narrator", VoiceId = "voice-x", Speed = 1.2 });
        settings.Settings.Tts.Channels["Agent"] = new VoiceChannelConfig { Enabled = true, ProfileName = "narrator" };
        settings.Settings.Tts.AutoSpeakChatReplies = true;

        var vm = NewTtsVm(settings);
        vm.ReloadFrom(settings.Settings);

        Assert.True(vm.AutoSpeakChatReplies);
        var profile = Assert.Single(vm.VoiceProfiles);
        Assert.Equal("narrator", profile.Name);
        var agentChannel = vm.VoiceChannels.Single(c => c.Channel == VoiceChannel.Agent);
        Assert.True(agentChannel.Enabled);
        Assert.Equal("narrator", agentChannel.ProfileName);

        var target = new TtsSettings();
        vm.ApplyVoiceOrchestrationTo(target);

        Assert.True(target.AutoSpeakChatReplies);
        Assert.Single(target.Profiles);
        Assert.True(target.Channels["Agent"].Enabled);
        Assert.Equal("narrator", target.Channels["Agent"].ProfileName);
    }

    [Fact]
    public void RemoveVoiceProfile_removes_the_selected_profile()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = NewTtsVm(settings);
        vm.AddVoiceProfileCommand.Execute(null);
        var profile = vm.VoiceProfiles.Single();

        vm.RemoveVoiceProfileCommand.Execute(profile);

        Assert.Empty(vm.VoiceProfiles);
    }

    [Fact]
    public void IsVoiceMuted_setter_updates_the_wired_orchestrator()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var voice = new FakeVoiceOrchestrator();
        var vm = new TtsSettingsViewModel(new FakeTts(), new FakeVoiceProviderRegistry(settings), new FakeToasts(),
            new XttsProcessManager(), new KokoroProcessManager(), new FakeSecretStore(), settings, voice);

        vm.IsVoiceMuted = true;

        Assert.True(voice.IsMuted);
    }

    [Fact]
    public async Task WorkspaceManifest_voice_profile_name_defaults_empty_for_legacy_files_and_round_trips()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var manifestPath = Path.Combine(workspace, ".aether", "workspace.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(manifestPath, """{"schema_version":1,"preferred_model_id":"m1"}""");

        var service = new WorkspaceManifestService();
        var loaded = await service.LoadAsync(workspace);

        Assert.NotNull(loaded);
        Assert.Equal(string.Empty, loaded!.VoiceProfileName);

        loaded.VoiceProfileName = "narrator";
        await service.SaveAsync(workspace, loaded);

        var reloaded = await service.LoadAsync(workspace);
        Assert.Equal("narrator", reloaded!.VoiceProfileName);
    }

    private static TtsSettingsViewModel NewTtsVm(SettingsService settings) =>
        new(new FakeTts(), new FakeVoiceProviderRegistry(settings), new FakeToasts(),
            new XttsProcessManager(), new KokoroProcessManager(), new FakeSecretStore(), settings);
}
