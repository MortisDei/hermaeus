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
        var dataRoot = temp.PathFor("data");
        var legacyJson = "{\"DataManagement\":{\"DataRootDirectory\":"
            + System.Text.Json.JsonSerializer.Serialize(dataRoot)
            + "},\"Tts\":{\"Enabled\":true,\"VoiceProvider\":\"KokoroNative\",\"Speaker\":\"af_heart\"}}";
        await File.WriteAllTextAsync(path, legacyJson);

        var service = NewSettings(temp);
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

    /// <summary>r24: profiles removed - a channel now stores its voice id directly, with
    /// no separate named-profile entity to create first.</summary>
    [Fact]
    public void ReloadFrom_and_ApplyVoiceOrchestrationTo_round_trip_channel_voice_ids()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Tts.Channels["Agent"] = new VoiceChannelConfig { Enabled = true, VoiceId = "voice-x" };
        settings.Settings.Tts.AutoSpeakChatReplies = true;

        var vm = NewTtsVm(settings);
        vm.ReloadFrom(settings.Settings);

        Assert.True(vm.AutoSpeakChatReplies);
        var agentChannel = vm.VoiceChannels.Single(c => c.Channel == VoiceChannel.Agent);
        Assert.True(agentChannel.Enabled);
        Assert.Equal("voice-x", agentChannel.VoiceId);
        Assert.Equal("voice-x", agentChannel.VoiceDisplay);

        var target = new TtsSettings();
        vm.ApplyVoiceOrchestrationTo(target);

        Assert.True(target.AutoSpeakChatReplies);
        Assert.True(target.Channels["Agent"].Enabled);
        Assert.Equal("voice-x", target.Channels["Agent"].VoiceId);
    }

    /// <summary>r24: a channel voice chosen before profiles were removed (VoiceId empty,
    /// only a legacy ProfileName pointing into the read-only Profiles list) must still
    /// resolve to the right voice on load, not silently reset to the default.</summary>
    [Fact]
    public void ReloadFrom_resolves_a_legacy_profile_name_into_the_channels_voice_id()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Tts.Profiles.Add(new VoiceProfile { Name = "narrator", VoiceId = "narrator-voice" });
        settings.Settings.Tts.Channels["Agent"] = new VoiceChannelConfig { Enabled = true, ProfileName = "narrator" };

        var vm = NewTtsVm(settings);
        vm.ReloadFrom(settings.Settings);

        var agentChannel = vm.VoiceChannels.Single(c => c.Channel == VoiceChannel.Agent);
        Assert.Equal("narrator-voice", agentChannel.VoiceId);
        Assert.Equal("narrator-voice", agentChannel.VoiceDisplay);
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

    // ── r24: channel voice picker lists available voices directly, no profile step ──

    [Fact]
    public void ChannelVoiceOptions_starts_with_the_default_entry_plus_the_initial_voice_and_gains_added_voices()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = NewTtsVm(settings);

        // TtsVoices seeds with "default" before any provider refresh runs.
        Assert.Equal([VoiceChannelSettingViewModel.DefaultVoiceLabel, "default"], vm.ChannelVoiceOptions);

        vm.TtsVoices.Add("voice-a");
        vm.TtsVoices.Add("voice-b");

        Assert.Equal([VoiceChannelSettingViewModel.DefaultVoiceLabel, "default", "voice-a", "voice-b"], vm.ChannelVoiceOptions);
    }

    [Fact]
    public void VoiceDisplay_shows_the_default_label_for_an_empty_voice_id_and_round_trips()
    {
        var channel = new VoiceChannelSettingViewModel(VoiceChannel.Chat, "Chat");
        Assert.Equal(VoiceChannelSettingViewModel.DefaultVoiceLabel, channel.VoiceDisplay);

        channel.VoiceDisplay = "voice-x";
        Assert.Equal("voice-x", channel.VoiceId);

        channel.VoiceDisplay = VoiceChannelSettingViewModel.DefaultVoiceLabel;
        Assert.Equal(string.Empty, channel.VoiceId);
    }

    [Fact]
    public void Empty_channel_voice_edit_does_not_clear_the_previous_selection()
    {
        var channel = new VoiceChannelSettingViewModel(VoiceChannel.Chat, "Chat")
        {
            VoiceId = "af_heart"
        };

        channel.VoiceDisplay = string.Empty;

        Assert.Equal("af_heart", channel.VoiceId);
        Assert.Equal("af_heart", channel.VoiceDisplay);
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
        Assert.Equal(
            [VoiceChannelSettingViewModel.DefaultVoiceLabel, "voice-a", "voice-b", "voice-c"],
            vm.ChannelVoiceOptions);
    }

    [Fact]
    public async Task Reload_keeps_a_persisted_channel_voice_represented_after_provider_discovery()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Tts.Channels["Agent"] = new VoiceChannelConfig { Enabled = true, VoiceId = "provider-voice-2" };
        var vm = new TtsSettingsViewModel(
            new ScriptedTts(["provider-voice-1", "provider-voice-2", "provider-voice-3"]),
            new FakeVoiceProviderRegistry(settings), new FakeToasts(),
            new XttsProcessManager(), new KokoroProcessManager(), new FakeSecretStore(), settings);

        vm.ReloadFrom(settings.Settings);
        await WaitForAsync(() => vm.ChannelVoiceOptions.Contains("provider-voice-2"), "provider voice discovery");

        var channel = vm.VoiceChannels.Single(item => item.Channel == VoiceChannel.Agent);
        Assert.Equal("provider-voice-2", channel.VoiceDisplay);
        Assert.Contains("provider-voice-2", vm.ChannelVoiceOptions);
    }

    /// <summary>Avalonia's ComboBox has no free-text entry mode; the channel voice picker
    /// uses AutoCompleteBox instead, so a hand-typed voice id still works for providers
    /// that cannot enumerate voices (TtsVoices then holds nothing) - the channel row's
    /// VoiceDisplay setter accepts any text regardless of ChannelVoiceOptions membership.</summary>
    [Fact]
    public async Task A_provider_that_cannot_enumerate_voices_leaves_manual_entry_working()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = new TtsSettingsViewModel(new ScriptedTts([]), new FakeVoiceProviderRegistry(settings), new FakeToasts(),
            new XttsProcessManager(), new KokoroProcessManager(), new FakeSecretStore(), settings);

        await vm.RefreshTtsVoicesCommand.ExecuteAsync(null);
        Assert.Empty(vm.TtsVoices);

        var channel = new VoiceChannelSettingViewModel(VoiceChannel.Chat, "Chat");
        channel.VoiceDisplay = "hand-typed-voice-id";
        Assert.Equal("hand-typed-voice-id", channel.VoiceId);
    }

    [Fact]
    public void Selected_voice_provider_id_keeps_the_two_Kokoro_display_labels_distinct()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = NewTtsVm(settings);
        vm.VoiceProviders.Add(new VoiceProviderInfo(
            VoiceProvider.Kokoro, "Kokoro (Python)", "Python backend.",
            VoiceProviderCategory.Advanced, false, VoiceCapability.TextToSpeech | VoiceCapability.Local));
        vm.VoiceProviders.Add(new VoiceProviderInfo(
            VoiceProvider.KokoroNative, "Kokoro (native)", "Native backend.",
            VoiceProviderCategory.Recommended, false, VoiceCapability.TextToSpeech | VoiceCapability.Local));

        vm.SelectedVoiceProvider = "Kokoro (Python)";
        Assert.Equal(VoiceProvider.Kokoro, vm.SelectedVoiceProviderId);

        vm.SelectedVoiceProvider = "Kokoro (native)";
        Assert.Equal(VoiceProvider.KokoroNative, vm.SelectedVoiceProviderId);
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
