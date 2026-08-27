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
    public void The_sentinel_still_round_trips_to_an_empty_voice_id()
    {
        var channel = new VoiceChannelSettingViewModel(Hermaeus.Core.Models.VoiceChannel.Chat, "Chat");

        Assert.Equal(VoiceChannelSettingViewModel.DefaultVoiceLabel, channel.VoiceDisplay);

        channel.VoiceDisplay = "af_heart";
        Assert.Equal("af_heart", channel.VoiceId);

        channel.VoiceDisplay = VoiceChannelSettingViewModel.DefaultVoiceLabel;
        Assert.Equal(string.Empty, channel.VoiceId);
    }
}
