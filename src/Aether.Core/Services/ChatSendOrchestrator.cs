using System.Diagnostics;

namespace Aether.Core.Services;

public sealed record ChatSendResult(
    long FirstTokenMs,
    long TotalLatencyMs,
    ChatTokenUsage? Usage,
    bool Cancelled,
    string? Error,
    ChatServerTimings? ServerTimings = null,
    // r14 4.1: the first streamed event of any kind (reasoning/tool deltas,
    // buffering) vs the first visible content token. The gap is time the user
    // spent staring at a blank bubble that "before first token" used to hide.
    long FirstEventMs = 0,
    // r19 1.2: "length" means the provider cut generation off at the
    // configured token cap, not that the model finished naturally.
    string? FinishReason = null);

/// <summary>
/// Drives one streamed chat completion and reports timing/usage, leaving all
/// UI-facing message-state mutation to the caller.
/// </summary>
public static class ChatSendOrchestrator
{
    public static async Task<ChatSendResult> StreamAsync(
        ILlmService llm,
        string modelId,
        IReadOnlyList<ChatMessage> history,
        LlmChatOptions options,
        Action<string> onToken,
        Action<ChatTokenUsage> onUsage,
        CancellationToken ct,
        // r14 4.2: fired once when the first stream event of any kind arrives,
        // so the caller can switch a live "reading prompt" placeholder to
        // "thinking" before any visible content exists.
        Action? onFirstEvent = null)
    {
        var clock = Stopwatch.StartNew();
        long? firstTokenMs = null;
        long? firstEventMs = null;
        ChatTokenUsage? usage = null;
        ChatServerTimings? serverTimings = null;
        string? finishReason = null;
        try
        {
            await foreach (var evt in llm.StreamChatAsync(modelId, history, options, ct))
            {
                // r14 4.1: stamp the first event of any kind, before the
                // content check, so a non-content stream prefix (reasoning or
                // tool deltas, transport buffering) is attributed to the stream
                // rather than misreported as "before first token".
                if (firstEventMs is null)
                {
                    firstEventMs = clock.ElapsedMilliseconds;
                    onFirstEvent?.Invoke();
                }

                if (evt.Usage is not null)
                {
                    usage = evt.Usage;
                    onUsage(evt.Usage);
                }

                if (evt.ServerTimings is not null)
                    serverTimings = evt.ServerTimings;

                if (evt.FinishReason is not null)
                    finishReason = evt.FinishReason;

                if (!string.IsNullOrEmpty(evt.ContentDelta))
                {
                    firstTokenMs ??= clock.ElapsedMilliseconds;
                    onToken(evt.ContentDelta);
                }
            }

            return new ChatSendResult(firstTokenMs ?? 0, clock.ElapsedMilliseconds, usage, Cancelled: false, Error: null, ServerTimings: serverTimings, FirstEventMs: firstEventMs ?? firstTokenMs ?? 0, FinishReason: finishReason);
        }
        catch (OperationCanceledException)
        {
            return new ChatSendResult(firstTokenMs ?? 0, clock.ElapsedMilliseconds, usage, Cancelled: true, Error: null, FirstEventMs: firstEventMs ?? 0);
        }
        catch (Exception ex)
        {
            return new ChatSendResult(firstTokenMs ?? 0, clock.ElapsedMilliseconds, usage, Cancelled: false, Error: ex.Message, FirstEventMs: firstEventMs ?? 0);
        }
    }
}
