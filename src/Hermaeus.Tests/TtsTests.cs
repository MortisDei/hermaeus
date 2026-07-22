using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Hermaeus.ViewModels;
using CommunityToolkit.Mvvm.Input;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests
{
    internal static class TtsTests
    {
        public static async Task VoiceProviderCapabilityGating()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            var toasts = new FakeToasts();
            var vm = new TtsSettingsViewModel(new FakeTts(), new FakeVoiceProviderRegistry(settings), toasts, new XttsProcessManager(), new KokoroProcessManager(), new FakeSecretStore(), settings);

            var limited = new VoiceProviderInfo(VoiceProvider.Kokoro, "Kokoro-Limited", "No TTS", VoiceProviderCategory.Recommended, true, VoiceCapability.Local);
            var asyncCmd = vm.SetActiveVoiceProviderCommand as IAsyncRelayCommand;
            if (asyncCmd is not null)
                await asyncCmd.ExecuteAsync(limited);
            else
                vm.SetActiveVoiceProviderCommand.Execute(limited);

            Equal("Kokoro (native)", vm.SelectedVoiceProvider, "provider without TTS should not become active");
        }

        public static async Task VoiceProviderXttsV2RequiresLocalAndTts()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            var toasts = new FakeToasts();
            settings.Settings.Tts.VoiceProvider = "XTTS v2";
            var limitedRegistry = new FakeVoiceProviderRegistryLimited(settings);
            var vm = new TtsSettingsViewModel(new FakeTts(), limitedRegistry, toasts, new XttsProcessManager(), new KokoroProcessManager(), new FakeSecretStore(), settings);

            vm.ReloadFrom(settings.Settings);

            False(vm.IsXttsV2Provider, "XTTS without Local+TTS should not be considered an XTTS v2 provider candidate");
            False(vm.StartTtsCommand.CanExecute(null), "StartTts should not be executable for unsupported XTTS v2 capability flags");
            False(vm.StopTtsCommand.CanExecute(null), "StopTts should not be executable for unsupported XTTS v2 capability flags");

            await Task.CompletedTask;
        }

        public static Task VoiceDeviceOptionsIncludeMps()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            var vm = new TtsSettingsViewModel(new FakeTts(), new FakeVoiceProviderRegistry(settings), new FakeToasts(), new XttsProcessManager(), new KokoroProcessManager(), new FakeSecretStore(), settings);

            True(vm.TtsDevices.Contains("mps"), "voice device options should include Apple Silicon MPS");

            return Task.CompletedTask;
        }

        public static async Task VoicePreviewSkipsBlankText()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            var tts = new CapturingTts();
            var vm = new TtsSettingsViewModel(tts, new FakeVoiceProviderRegistry(settings), new FakeToasts(), new XttsProcessManager(), new KokoroProcessManager(), new FakeSecretStore(), settings);

            vm.TtsPreviewText = "   ";
            await vm.PreviewTtsVoiceCommand.ExecuteAsync(null);

            False(tts.PreviewCalled, "blank preview text should not be sent to TTS");
        }

        private sealed class CapturingTts : ITtsService
        {
            public bool PreviewCalled { get; private set; }

            public Task SpeakAsync(string text, CancellationToken ct = default) => Task.CompletedTask;

            public Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default)
            {
                PreviewCalled = true;
                return Task.CompletedTask;
            }

            public Task<string> ImportVoiceSampleAsync(string sourcePath, string displayName, CancellationToken ct = default) =>
                Task.FromResult(displayName);

            public Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyList<string>>(new List<string> { "default" });
        }
    }
}
