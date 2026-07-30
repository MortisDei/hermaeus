using Hermaeus.Core.Models;
using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed partial class DoctorService
{
    /// <summary>r24 doc 05 5.6: backend reachable, model file present and
    /// hash-verified. The knowledge of what "healthy" means lives in the voice
    /// subsystem (NativeSpeechRecognitionProvider.HealthCheckAsync /
    /// OpenAiSpeechRecognitionProvider.IsAvailable) - Doctor only calls into it,
    /// per the CLAUDE.md hot-spot rule against growing new subsystem knowledge here.</summary>
    private async Task<DoctorCheck> CheckSpeechRecognitionAsync(CancellationToken ct)
    {
        if (_sttProviders is null || !_settings.Settings.Stt.Enabled)
            return BuildCheck(
                "speech-recognition",
                "Speech recognition",
                DoctorCheckStatus.Info,
                "Speech recognition is off",
                "Enable it in Services > Voice to use dictation or hands-free mode.",
                "Open Services",
                false,
                string.Empty,
                "Voice");

        var providerKind = _sttProviders.GetActiveProvider();
        var service = _sttProviders.GetActiveService();

        if (providerKind == SttProvider.OnnxNative && service is Hermaeus.Voice.NativeSpeechRecognitionProvider native)
        {
            var health = await native.HealthCheckAsync(ct);
            var status = health.Status switch
            {
                VoiceHealthStatus.Healthy => DoctorCheckStatus.Ready,
                VoiceHealthStatus.Warning => DoctorCheckStatus.Warning,
                _ => DoctorCheckStatus.Error
            };
            return BuildCheck("speech-recognition", "Speech recognition backend", status, health.Summary, health.Detail,
                status == DoctorCheckStatus.Ready ? "Open Services" : "Install speech recognition model", true, health.Detail, "Voice");
        }

        var available = service.IsAvailable;
        return BuildCheck(
            "speech-recognition",
            "Speech recognition backend",
            available ? DoctorCheckStatus.Ready : DoctorCheckStatus.Warning,
            available ? "Remote speech recognition configured" : "Remote speech recognition not configured",
            available ? $"Provider: {service.ProviderName}." : "Set an OpenAI base URL and API key in Services > Voice.",
            "Open Services",
            true,
            string.Empty,
            "Voice");
    }

    /// <summary>An input device enumerable. Not reported at all on a platform where
    /// this round offers no way to fix it (matches the existing discipline of not
    /// reporting Linux's lack of system-wide hotkeys as a problem) - here, every
    /// platform this round supports has a real fix (plug in / permit a mic, or on
    /// Linux install one of the three recorder tools), so it is always reported.</summary>
    private DoctorCheck CheckMicrophoneAsync()
    {
        if (_audioCapture is null)
            return BuildCheck("microphone", "Microphone", DoctorCheckStatus.Info, "Not checked", string.Empty, "Open Services", false, string.Empty, "Voice");

        var available = _audioCapture.IsAvailable;
        return BuildCheck(
            "microphone",
            "Microphone",
            available ? DoctorCheckStatus.Ready : DoctorCheckStatus.Info,
            available ? "Input device available" : "No input device found",
            available ? "A microphone is available for dictation and hands-free mode." : _audioCapture.UnavailableReason ?? "No input device found.",
            "Open Services",
            false,
            string.Empty,
            "Voice");
    }

    /// <summary>
    /// r25 doc 03 3.6: r24's wav2vec2 model is superseded by Whisper. Its files are
    /// reported as no longer used, with their size and an explicit way to remove
    /// them. They are never deleted automatically: the user chose to download
    /// several hundred megabytes and silently throwing that away is not this app's
    /// posture, even for a model it no longer uses.
    ///
    /// Returns null when there is nothing there, so a normal install shows no row.
    /// </summary>
    private DoctorCheck? CheckSupersededSpeechModel()
    {
        var directory = Hermaeus.Voice.NativeSpeechRecognitionProvider
            .ResolveSupersededAssetsDirectory(_settings.Settings);
        if (!Directory.Exists(directory))
            return null;

        long bytes;
        try
        {
            bytes = new DirectoryInfo(directory)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch
        {
            return null;
        }

        if (bytes <= 0)
            return null;

        return BuildCheck(
            "speech-recognition-superseded",
            "Superseded speech recognition model",
            DoctorCheckStatus.Info,
            $"An older speech recognition model is still on disk ({bytes / 1024 / 1024} MB)",
            $"Speech recognition now uses Whisper, which produces punctuation and casing. " +
            $"The previous wav2vec2 model is no longer used and can be removed: {directory}. " +
            "Nothing has been deleted.",
            "Open Services",
            false,
            directory,
            "Voice");
    }

    public async Task<bool> InstallSpeechRecognitionAssetsAsync(IProgress<string> progress, CancellationToken ct = default)
    {
        if (_sttProviders?.GetActiveService() is not Hermaeus.Voice.NativeSpeechRecognitionProvider native)
        {
            _runtimeLogs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Service,
                "Speech recognition installation skipped: native provider not active"));
            return false;
        }

        _runtimeLogs?.Add(new RuntimeLogEntry(
            DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Service,
            $"Speech recognition installation starting; assets root: {Hermaeus.Voice.NativeSpeechRecognitionProvider.ResolveAssetsDirectory(_settings.Settings)}"));

        var result = await native.InstallAssetsAsync(progress, ct);
        _runtimeLogs?.Add(new RuntimeLogEntry(
            DateTime.UtcNow, result ? RuntimeLogLevel.Info : RuntimeLogLevel.Error, RuntimeLogCategory.Service,
            result ? "Speech recognition assets installed successfully" : "Speech recognition asset installation failed"));
        return result;
    }
}
