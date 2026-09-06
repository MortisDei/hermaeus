using Hermaeus.Core.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r29 doc 01 1.2: the per-channel voice picker's usual content is the sentinel
/// plus the placeholder "default", which looks like a populated list and is not.
/// The view says so when the provider has not listed its voices.
/// </summary>
public sealed class VoiceChannelPickerTests
{
    [Fact]
    public void Channel_voice_options_are_the_sentinel_followed_by_the_provider_voices_in_order()
    {
        using var temp = new TempDir();
        var vm = NewTtsSettingsViewModel(NewSettings(temp));

        vm.TtsVoices.Clear();
        vm.TtsVoices.Add("af_heart");
        vm.TtsVoices.Add("am_michael");
        vm.TtsVoices.Add("bf_emma");

        Assert.Equal(
            [VoiceChannelSettingViewModel.DefaultVoiceLabel, "af_heart", "am_michael", "bf_emma"],
            vm.ChannelVoiceOptions);
        Assert.Single(vm.ChannelVoiceOptions, o => o == VoiceChannelSettingViewModel.DefaultVoiceLabel);
    }

    [Fact]
    public void The_options_are_not_provider_supplied_until_a_refresh_lists_real_voices()
    {
        using var temp = new TempDir();
        var vm = NewTtsSettingsViewModel(NewSettings(temp));

        // The initial state: the sentinel plus the "default" placeholder.
        Assert.False(vm.ChannelVoiceOptionsAreProviderSupplied);

        vm.TtsVoices.Add("af_heart");

        Assert.True(vm.ChannelVoiceOptionsAreProviderSupplied);
        Assert.Equal("1 named voice(s) reported by Kokoro (native).", vm.ChannelVoiceDiscoveryStatus);
    }

    [Fact]
    public void Discovery_status_names_the_selected_provider_when_voices_are_unavailable()
    {
        using var temp = new TempDir();
        var vm = NewTtsSettingsViewModel(NewSettings(temp));

        Assert.Equal("Kokoro (native) has not reported named voices yet. Retrying is safe; you can also enter a verified voice id.", vm.ChannelVoiceDiscoveryStatus);
    }

    [Fact]
    public async Task A_late_superseded_provider_refresh_cannot_replace_the_current_provider_catalogue()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var voices = new DelayedVoiceService();
        var vm = NewTtsSettingsViewModel(settings, voices);

        vm.ReloadFrom(settings.Settings);
        await voices.WaitForCallAsync(0);

        vm.SelectedVoiceProvider = "F5-TTS";
        await voices.WaitForCallAsync(1);

        voices.Complete(1, ["b_voice"]);
        await WaitForAsync(() => vm.TtsVoices.Contains("b_voice"), "provider B voice catalogue");

        voices.Complete(0, ["a_voice"]);
        await Task.Yield();

        Assert.Equal(["b_voice"], vm.TtsVoices);
        Assert.Equal([VoiceChannelSettingViewModel.DefaultVoiceLabel, "b_voice"], vm.ChannelVoiceOptions);
        Assert.Equal("1 named voice(s) reported by F5-TTS.", vm.ChannelVoiceDiscoveryStatus);
    }

    [Fact]
    public void The_sentinel_still_round_trips_to_an_empty_voice_id()
    {
        var channel = new VoiceChannelSettingViewModel(Hermaeus.Core.Models.VoiceChannel.Chat, "Chat");

        Assert.Equal(VoiceChannelSettingViewModel.DefaultVoiceLabel, channel.VoiceDisplay);

        channel.VoiceDisplay = "af_heart";
        Assert.Equal("af_heart", channel.VoiceId);

        channel.VoiceDisplay = VoiceChannelSettingViewModel.DefaultVoiceLabel;
        Assert.Equal(string.Empty, channel.VoiceId);
    }

    [Fact]
    public async Task Every_channel_owns_a_fresh_voice_catalogue_snapshot()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var voices = new DelayedVoiceService();
        using var vm = NewTtsSettingsViewModel(settings, voices);
        vm.ReloadFrom(settings.Settings);
        await voices.WaitForCallAsync(0);
        vm.TtsVoices.Clear();
        vm.TtsVoices.Add("af_heart");

        Assert.Equal(vm.VoiceChannels.Count, vm.VoiceChannels.Select(channel => channel.VoiceOptions).Distinct().Count());
        Assert.All(vm.VoiceChannels, channel =>
            Assert.Equal([VoiceChannelSettingViewModel.DefaultVoiceLabel, "af_heart"], channel.VoiceOptions));
    }

    [Fact]
    public void Channel_picker_uses_an_editable_unfiltered_combo_box_for_catalogue_reopens()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var view = File.ReadAllText(Path.Combine(repoRoot, "src", "Hermaeus.Desktop", "Views", "SettingsVoiceSectionView.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(repoRoot, "src", "Hermaeus.Desktop", "Views", "SettingsVoiceSectionView.axaml.cs"));

        Assert.Contains("<ComboBox", view, StringComparison.Ordinal);
        Assert.Contains("IsEditable=\"True\"", view, StringComparison.Ordinal);
        Assert.Contains("IsTextSearchEnabled=\"False\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoCompleteBox", view, StringComparison.Ordinal);
        Assert.DoesNotContain("VoicePickerState", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("DropDownOpening", view, StringComparison.Ordinal);
        Assert.DoesNotContain("DropDownClosed", view, StringComparison.Ordinal);
    }

    private sealed class DelayedVoiceService : ITtsService
    {
        private readonly List<TaskCompletionSource<IReadOnlyList<string>>> _requests = [];

        public Task SpeakAsync(string text, CancellationToken ct = default) => Task.CompletedTask;
        public Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> ImportVoiceSampleAsync(string sourcePath, string displayName, CancellationToken ct = default) => Task.FromResult(displayName);

        public Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default)
        {
            var request = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _requests.Add(request);
            return request.Task;
        }

        public async Task WaitForCallAsync(int index) =>
            await WaitForAsync(() => _requests.Count > index, $"voice refresh {index}");

        public void Complete(int index, IReadOnlyList<string> voices) => _requests[index].SetResult(voices);
    }
}
