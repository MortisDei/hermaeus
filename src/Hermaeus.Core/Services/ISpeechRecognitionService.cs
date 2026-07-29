using Hermaeus.Core.Models;

namespace Hermaeus.Core.Services;

/// <summary>r24 doc 05 5.1: one speech-to-text provider, alongside <see cref="ITtsService"/>
/// and <see cref="IVoiceProvider"/>. Audio in, transcript out - captured audio is never
/// persisted by any implementation of this interface.</summary>
public interface ISpeechRecognitionService
{
    string ProviderName { get; }
    bool IsAvailable { get; }

    Task<SpeechTranscript> TranscribeAsync(Stream wavPcm16Mono16k, SpeechTranscribeOptions options, CancellationToken ct = default);
}

/// <summary>Registry mirroring <see cref="IVoiceProviderRegistry"/>: selects between the
/// local (in-process ONNX) and remote (OpenAI-compatible) speech recognition providers.</summary>
public interface ISpeechRecognitionProviderRegistry
{
    SttProvider GetActiveProvider();
    ISpeechRecognitionService GetActiveService();
    Task SetActiveProviderAsync(SttProvider provider);
}
