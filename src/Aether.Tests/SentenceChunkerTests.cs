using Aether.Core.Services;
using Xunit;

namespace Aether.Tests;

public sealed class SentenceChunkerTests
{
    [Fact]
    public void Append_emits_chunks_that_reconstruct_original_words_in_order()
    {
        var chunker = new SentenceChunker();
        const string text = "This is sentence one. This is sentence two, still fairly short. " +
                             "Now the total is long enough to trigger a chunk emission finally.";

        var chunks = new List<string>();
        foreach (var ch in text)
            chunks.AddRange(chunker.Append(ch.ToString()));
        var remainder = chunker.Flush();
        if (remainder is not null)
            chunks.Add(remainder);

        var reconstructedWords = string.Join(" ", chunks).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var originalWords = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(originalWords, reconstructedWords);
    }

    [Fact]
    public void Append_only_emits_chunks_at_least_sixty_chars_except_final_flush()
    {
        var chunker = new SentenceChunker();
        const string text = "Short one. Short two. Short three. " +
                             "This sentence pushes the running total past the sixty character threshold now.";

        foreach (var ch in text)
            foreach (var chunk in chunker.Append(ch.ToString()))
                Assert.True(chunk.Length >= 60, $"chunk shorter than 60 chars: '{chunk}'");
    }

    [Fact]
    public void Append_merges_consecutive_short_sentences_until_threshold_reached()
    {
        var chunker = new SentenceChunker();
        var emitted = new List<string>();
        const string text = "One. Two. Three. Four. Five sentences merged together to clear the length floor finally.";
        foreach (var ch in text)
            emitted.AddRange(chunker.Append(ch.ToString()));

        Assert.All(emitted, chunk => Assert.True(chunk.Length >= 60));
    }

    [Fact]
    public void Flush_returns_null_when_nothing_buffered()
    {
        var chunker = new SentenceChunker();
        Assert.Null(chunker.Flush());
    }

    [Fact]
    public void Flush_returns_trimmed_remainder_after_partial_sentence()
    {
        var chunker = new SentenceChunker();
        chunker.Append("  Hello world without a terminator  ");

        Assert.Equal("Hello world without a terminator", chunker.Flush());
    }

    [Fact]
    public void Flush_clears_state_so_a_second_flush_is_null()
    {
        var chunker = new SentenceChunker();
        chunker.Append("leftover text");
        chunker.Flush();

        Assert.Null(chunker.Flush());
    }

    [Fact]
    public void Append_with_empty_token_returns_no_chunks()
    {
        var chunker = new SentenceChunker();
        Assert.Empty(chunker.Append(string.Empty));
    }

    [Fact]
    public void Append_does_not_cut_on_decimal_point_without_trailing_whitespace()
    {
        var chunker = new SentenceChunker();
        var chunks = chunker.Append("The value is 3.14 exactly, keep going until whitespace ends this run of text.");

        // "3.14" is a terminator immediately followed by a digit, not whitespace, so it must not be treated as a cut point.
        Assert.Empty(chunks);
        Assert.Equal("The value is 3.14 exactly, keep going until whitespace ends this run of text.", chunker.Flush());
    }
}
