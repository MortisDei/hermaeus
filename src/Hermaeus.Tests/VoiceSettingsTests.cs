using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

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
        var manifestPath = Path.Combine(workspace, ".hermaeus", "workspace.json");
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

    // ── r19 4.3: dropdowns instead of free-text for channel profile / voice id ──

    [Fact]
    public void ProfileNameOptions_starts_with_the_default_entry_and_gains_added_profiles()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = NewTtsVm(settings);

        Assert.Equal([VoiceChannelSettingViewModel.DefaultVoiceLabel], vm.ProfileNameOptions);

        vm.AddVoiceProfileCommand.Execute(null);
        var profile = vm.VoiceProfiles.Single();
        profile.Name = "narrator";

        Assert.Equal([VoiceChannelSettingViewModel.DefaultVoiceLabel, "narrator"], vm.ProfileNameOptions);
    }

    [Fact]
    public void ProfileNameOptions_updates_on_rename_and_removal()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = NewTtsVm(settings);
        vm.AddVoiceProfileCommand.Execute(null);
        var profile = vm.VoiceProfiles.Single();
        profile.Name = "narrator";
        Assert.Contains("narrator", vm.ProfileNameOptions);

        profile.Name = "renamed";
        Assert.DoesNotContain("narrator", vm.ProfileNameOptions);
        Assert.Contains("renamed", vm.ProfileNameOptions);

        vm.RemoveVoiceProfileCommand.Execute(profile);
        Assert.DoesNotContain("renamed", vm.ProfileNameOptions);
        Assert.Equal([VoiceChannelSettingViewModel.DefaultVoiceLabel], vm.ProfileNameOptions);
    }

    [Fact]
    public void ProfileNameDisplay_shows_the_default_label_for_an_empty_profile_name_and_round_trips()
    {
        var channel = new VoiceChannelSettingViewModel(VoiceChannel.Chat, "Chat");
        Assert.Equal(VoiceChannelSettingViewModel.DefaultVoiceLabel, channel.ProfileNameDisplay);

        channel.ProfileNameDisplay = "narrator";
        Assert.Equal("narrator", channel.ProfileName);

        channel.ProfileNameDisplay = VoiceChannelSettingViewModel.DefaultVoiceLabel;
        Assert.Equal(string.Empty, channel.ProfileName);
    }

    [Fact]
    public async Task RefreshTtsVoices_populates_TtsVoices_from_the_active_providers_list()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = new TtsSettingsViewModel(new ScriptedTts(["voice-a", "voice-b", "voice-c"]), new FakeVoiceProviderRegistry(settings), new FakeToasts(),
            new XttsProcessManager(), new KokoroProcessManager(), new FakeSecretStore(), settings);

        await vm.RefreshTtsVoicesCommand.ExecuteAsync(null);

        Assert.Equal(["voice-a", "voice-b", "voice-c"], vm.TtsVoices);
    }

    [Fact]
    public async Task A_provider_that_cannot_enumerate_voices_leaves_manual_entry_working()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = new TtsSettingsViewModel(new ScriptedTts([]), new FakeVoiceProviderRegistry(settings), new FakeToasts(),
            new XttsProcessManager(), new KokoroProcessManager(), new FakeSecretStore(), settings);

        await vm.RefreshTtsVoicesCommand.ExecuteAsync(null);
        Assert.Empty(vm.TtsVoices);

        vm.AddVoiceProfileCommand.Execute(null);
        var profile = vm.VoiceProfiles.Single();
        // AutoCompleteBox.Text still binds VoiceId directly regardless of ItemsSource being empty.
        profile.VoiceId = "hand-typed-voice-id";
        Assert.Equal("hand-typed-voice-id", profile.VoiceId);
    }

    private sealed class ScriptedTts(IReadOnlyList<string> voices) : ITtsService
    {
        public Task SpeakAsync(string text, CancellationToken ct = default) => Task.CompletedTask;
        public Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> ImportVoiceSampleAsync(string sourcePath, string displayName, CancellationToken ct = default) =>
            Task.FromResult(displayName);
        public Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default) => Task.FromResult(voices);
    }

    private static TtsSettingsViewModel NewTtsVm(SettingsService settings) =>
        new(new FakeTts(), new FakeVoiceProviderRegistry(settings), new FakeToasts(),
            new XttsProcessManager(), new KokoroProcessManager(), new FakeSecretStore(), settings);
}
