using Hermaeus.Core.Services;

namespace Hermaeus.ViewModels;

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
    ChatServerTimings? ServerTimings = null,
    long FirstEventMs = 0,
    long RagMs = 0)
{
    /// <summary>A send whose pre-first-token wait exceeds this is worth a WARNING, not just an Info line.</summary>
    public const long SlowSendThresholdMs = 10_000;

    /// <summary>
    /// Prompt-eval throughput at or below this (tokens/sec) on a machine with a
    /// real GPU but CPU inference configured is diagnosed as CPU-speed reading
    /// (r14 4.5).
    /// </summary>
    public const double CpuSpeedPromptThreshold = 200;

    /// <summary>Everything the user experienced as "silence" before the first token: recall through first token.</summary>
    public long PreFirstTokenMs => RecallMs + SelectMs + LessonMs + RagMs + PromptBuildMs + FirstTokenMs;

    /// <summary>
    /// Time between the first streamed event of any kind and the first visible
    /// content token (r14 4.1): a non-content stream prefix (reasoning or tool
    /// deltas, buffering) the user saw as a blank bubble.
    /// </summary>
    public long NonContentStreamMs => FirstEventMs > 0 && FirstTokenMs > FirstEventMs ? FirstTokenMs - FirstEventMs : 0;

    public bool IsSlow => PreFirstTokenMs > SlowSendThresholdMs;

    /// <summary>Prompt-eval throughput the server reported, tokens/sec, when available.</summary>
    public double? PromptTokensPerSecond =>
        ServerTimings is { PromptTokens: { } tokens, PromptMs: { } ms } && ms > 0
            ? tokens / (ms / 1000.0)
            : null;

    public string Format()
    {
        var s = $"recall {RecallMs} ms, select {SelectMs} ms, lesson {LessonMs} ms, rag {RagMs} ms, " +
                $"prompt build {PromptBuildMs} ms, first token {FirstTokenMs} ms, total {TotalMs} ms";

        if (NonContentStreamMs > 0)
            s += $", non-content stream {NonContentStreamMs} ms";

        if (ServerTimings is { PromptTokens: { } promptTokens, PromptMs: { } promptMs })
            s += $", server prompt {promptTokens} tok / {promptMs:0} ms";

        return s;
    }

    /// <summary>
    /// Diagnoses a slow prompt read as CPU-speed when a real GPU is present but
    /// inference is configured for the CPU (r14 4.5). Pure so it is testable
    /// without a live send; returns null when there is nothing clear-cut to add.
    /// </summary>
    public static string? SlowSendBottleneckHint(double? promptTokensPerSecond, bool gpuPresentButCpuInference)
    {
        if (!gpuPresentButCpuInference)
            return null;
        if (promptTokensPerSecond is not double tps || tps <= 0 || tps >= CpuSpeedPromptThreshold)
            return null;
        return $"prompt was read at CPU speed ({tps:0} t/s); see Doctor";
    }
}
