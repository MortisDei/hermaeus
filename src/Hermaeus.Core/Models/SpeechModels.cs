namespace Hermaeus.Core.Models;

/// <summary>r24 doc 05: result of a speech-to-text pass. Error is set (and Text
/// empty) when the provider could not produce a transcript at all; IsLowConfidence
/// covers the "produced something, but do not trust it" case (e.g. an empty or
/// near-empty transcript from audio that had no clear speech).</summary>
public sealed record SpeechTranscript(
    string Text,
    int DurationMs,
    string Language,
    bool IsLowConfidence,
    string? Error = null);

public sealed record SpeechTranscribeOptions(string? LanguageHint = null);

public sealed record AudioInputDevice(string Id, string Name);

public enum SttProvider
{
    OnnxNative,
    OpenAi
}
