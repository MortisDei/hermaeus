using Aether.Core.Services;

namespace Aether.ViewModels;

/// <summary>
/// Pre-stream timing breakdown for one chat send (r9 01-send-path-latency.md
/// 1.1). Always measured, never Debug/Release conditional, so a slow send is
/// diagnosable from the trace panel without reproducing it under a profiler.
/// <see cref="ServerTimings"/> decomposes <see cref="FirstTokenMs"/> further
/// (r10 03-field-follow-ups.md 3.2) when the provider reports it.
/// </summary>
public readonly record struct ChatSendTiming(
    long RecallMs,
    long SelectMs,
    long LessonMs,
    long PromptBuildMs,
    long FirstTokenMs,
    long TotalMs,
    ChatServerTimings? ServerTimings = null)
{
    /// <summary>A send whose pre-first-token wait exceeds this is worth a WARNING, not just an Info line.</summary>
    public const long SlowSendThresholdMs = 10_000;

    /// <summary>Everything the user experienced as "silence" before the first token: recall through first token.</summary>
    public long PreFirstTokenMs => RecallMs + SelectMs + LessonMs + PromptBuildMs + FirstTokenMs;

    public bool IsSlow => PreFirstTokenMs > SlowSendThresholdMs;

    public string Format()
    {
        var s = $"recall {RecallMs} ms, select {SelectMs} ms, lesson {LessonMs} ms, " +
                $"prompt build {PromptBuildMs} ms, first token {FirstTokenMs} ms, total {TotalMs} ms";

        if (ServerTimings is { PromptTokens: { } promptTokens, PromptMs: { } promptMs })
            s += $", server prompt {promptTokens} tok / {promptMs:0} ms";

        return s;
    }
}
