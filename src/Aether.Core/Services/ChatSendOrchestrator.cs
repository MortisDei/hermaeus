using System.Diagnostics;

namespace Aether.Core.Services;

public sealed record ChatSendResult(
    long FirstTokenMs,
    long TotalLatencyMs,
    ChatTokenUsage? Usage,
    bool Cancelled,
    string? Error);

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
        CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        long? firstTokenMs = null;
        ChatTokenUsage? usage = null;
        try
        {
            await foreach (var evt in llm.StreamChatAsync(modelId, history, options, ct))
            {
                if (evt.Usage is not null)
                {
                    usage = evt.Usage;
                    onUsage(evt.Usage);
                }

                if (!string.IsNullOrEmpty(evt.ContentDelta))
                {
                    firstTokenMs ??= clock.ElapsedMilliseconds;
                    onToken(evt.ContentDelta);
                }
            }

            return new ChatSendResult(firstTokenMs ?? 0, clock.ElapsedMilliseconds, usage, Cancelled: false, Error: null);
        }
        catch (OperationCanceledException)
        {
            return new ChatSendResult(firstTokenMs ?? 0, clock.ElapsedMilliseconds, usage, Cancelled: true, Error: null);
        }
        catch (Exception ex)
        {
            return new ChatSendResult(firstTokenMs ?? 0, clock.ElapsedMilliseconds, usage, Cancelled: false, Error: ex.Message);
        }
    }
}
