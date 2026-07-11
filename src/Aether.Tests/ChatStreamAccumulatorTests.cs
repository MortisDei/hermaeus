using Aether.Core.Services;
using Xunit;

namespace Aether.Tests;

public sealed class ChatStreamAccumulatorTests
{
    [Fact]
    public void TryAppend_holds_small_tokens_until_forced()
    {
        var accumulator = new ChatStreamAccumulator(flushIntervalMs: 60_000, flushSizeThreshold: 256);

        var flushedFirst = accumulator.TryAppend("a", force: false, out var firstChunk);
        Assert.False(flushedFirst);
        Assert.Equal(string.Empty, firstChunk);

        var flushedForced = accumulator.TryAppend("b", force: true, out var forcedChunk);
        Assert.True(flushedForced);
        Assert.Equal("ab", forcedChunk);
    }

    [Fact]
    public void TryAppend_flushes_once_size_threshold_is_reached()
    {
        var accumulator = new ChatStreamAccumulator(flushIntervalMs: 60_000, flushSizeThreshold: 4);

        Assert.False(accumulator.TryAppend("ab", force: false, out _));
        var flushed = accumulator.TryAppend("cd", force: false, out var chunk);

        Assert.True(flushed);
        Assert.Equal("abcd", chunk);
    }

    [Fact]
    public void TryAppend_does_not_flush_empty_buffer_even_when_forced()
    {
        var accumulator = new ChatStreamAccumulator();

        var flushed = accumulator.TryAppend(string.Empty, force: true, out var chunk);

        Assert.False(flushed);
        Assert.Equal(string.Empty, chunk);
    }

    [Fact]
    public void RenderBatches_counts_each_flush()
    {
        var accumulator = new ChatStreamAccumulator(flushIntervalMs: 60_000, flushSizeThreshold: 1);

        accumulator.TryAppend("a", force: false, out _);
        accumulator.TryAppend("b", force: false, out _);

        Assert.Equal(2, accumulator.RenderBatches);
    }
}
