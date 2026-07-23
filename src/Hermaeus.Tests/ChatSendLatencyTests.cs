using System.Runtime.CompilerServices;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// Latency-truth accounting for a chat send: separating the first streamed
/// event of any kind from the first visible content token, and diagnosing a
/// CPU-speed prompt read on a GPU machine.
/// </summary>
public sealed class ChatSendLatencyTests
{
    [Fact]
    public async Task StreamAsync_records_first_event_before_first_content()
    {
        var llm = new PrefixThenContentLlm(nonContentEvents: 3);
        var result = await ChatSendOrchestrator.StreamAsync(
            llm, "m", [], LlmChatOptions.Default, _ => { }, _ => { }, CancellationToken.None);

        Assert.True(result.FirstEventMs <= result.FirstTokenMs,
            "the first stream event must be recorded no later than the first visible token");
        Assert.False(result.Cancelled);
    }

    [Fact]
    public void NonContentStreamMs_is_the_gap_from_first_event_to_first_token()
    {
        var timing = new ChatSendTiming(0, 0, 0, 0, FirstTokenMs: 302_500, TotalMs: 585_000,
            ServerTimings: null, FirstEventMs: 191_200);
        Assert.Equal(111_300, timing.NonContentStreamMs);
        Assert.Contains("non-content stream 111300 ms", timing.Format());
    }

    [Fact]
    public void SlowSendBottleneckHint_only_fires_for_cpu_speed_prompt_on_gpu_machine()
    {
        Assert.NotNull(ChatSendTiming.SlowSendBottleneckHint(51, gpuPresentButCpuInference: true));
        Assert.Null(ChatSendTiming.SlowSendBottleneckHint(51, gpuPresentButCpuInference: false));
        // A GPU-offloaded fast prompt eval adds nothing.
        Assert.Null(ChatSendTiming.SlowSendBottleneckHint(1200, gpuPresentButCpuInference: true));
    }

    [Fact]
    public void StreamingPhase_respects_grace_then_shows_the_first_word_and_clears_on_content()
    {
        // Within the grace window nothing shows (no flicker for fast sends).
        Assert.Equal(string.Empty, ChatStreamingPhase.Describe(1_500, sawContent: false));
        // Past grace: the (default, index 0) word with the elapsed seconds.
        Assert.Equal("Reading prompt... 5s", ChatStreamingPhase.Describe(5_000, sawContent: false));
        // Once visible content arrives the placeholder disappears.
        Assert.Equal(string.Empty, ChatStreamingPhase.Describe(12_000, sawContent: true));
    }

    // ── r19 6.4 / field-report follow-up: rotating "thinking" status words ─────

    [Fact]
    public void StreamingPhase_rotates_the_word_over_time_while_thinking()
    {
        Assert.Equal("Reading prompt... 12s", ChatStreamingPhase.Describe(12_000, sawContent: false, wordIndex: 0));
        Assert.Equal("Thinking... 12s", ChatStreamingPhase.Describe(12_000, sawContent: false, wordIndex: 1));
        // Cycles back around once the index exceeds the word list length.
        Assert.Equal("Reading prompt... 12s", ChatStreamingPhase.Describe(12_000, sawContent: false, wordIndex: ChatStreamingPhase.WhimsyWords.Count));
    }

    [Fact]
    public void StreamingPhase_freezes_and_clears_the_label_once_content_arrives()
    {
        Assert.Equal("Thinking... 3s", ChatStreamingPhase.Describe(3_000, sawContent: false, wordIndex: 1));
        // Content arriving clears the label regardless of wordIndex.
        Assert.Equal(string.Empty, ChatStreamingPhase.Describe(3_000, sawContent: true, wordIndex: 1));
    }

    [Fact]
    public void StreamingPhase_wraps_negative_word_indexes_instead_of_throwing()
    {
        // A random per-send starting offset plus elapsed/2.5s can't go negative
        // in practice, but the modulo math must stay in-bounds regardless.
        var text = ChatStreamingPhase.Describe(5_000, sawContent: false, wordIndex: -1);
        Assert.Equal($"{ChatStreamingPhase.WhimsyWords[^1]}... 5s", text);
    }

    [Fact]
    public async Task StreamAsync_fires_onFirstEvent_once_before_content()
    {
        var firstEvents = 0;
        var llm = new PrefixThenContentLlm(nonContentEvents: 2);
        await ChatSendOrchestrator.StreamAsync(
            llm, "m", [], LlmChatOptions.Default, _ => { }, _ => { }, CancellationToken.None,
            onFirstEvent: () => Interlocked.Increment(ref firstEvents));
        Assert.Equal(1, firstEvents);
    }

    private sealed class PrefixThenContentLlm(int nonContentEvents) : ILlmService
    {
        public string ProviderName => "fake";
        public bool IsConfigured => true;
        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) => Task.FromResult(new List<LlmModel>());

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId, IReadOnlyList<ChatMessage> messages, LlmChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            for (var i = 0; i < nonContentEvents; i++)
            {
                // A non-content event that still reaches the orchestrator (e.g. a
                // server-timings-only chunk): no visible ContentDelta.
                await Task.Delay(1, ct);
                yield return new LlmStreamEvent(ServerTimings: new ChatServerTimings(1, 1, 1, 1));
            }
            await Task.Delay(1, ct);
            yield return new LlmStreamEvent("hello", IsFinal: true);
        }
    }
}
