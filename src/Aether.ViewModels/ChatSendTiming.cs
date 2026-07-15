namespace Aether.ViewModels;

/// <summary>
/// Pre-stream timing breakdown for one chat send (r9 01-send-path-latency.md
/// 1.1). Always measured, never Debug/Release conditional, so a slow send is
/// diagnosable from the trace panel without reproducing it under a profiler.
/// </summary>
public readonly record struct ChatSendTiming(
    long RecallMs,
    long SelectMs,
    long LessonMs,
    long PromptBuildMs,
    long FirstTokenMs,
    long TotalMs)
{
    public string Format() =>
        $"recall {RecallMs} ms, select {SelectMs} ms, lesson {LessonMs} ms, " +
        $"prompt build {PromptBuildMs} ms, first token {FirstTokenMs} ms, total {TotalMs} ms";
}
