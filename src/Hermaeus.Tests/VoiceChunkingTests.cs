using Hermaeus.Voice;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>r19 4.1: phoneme chunk splitting must prefer sentence/clause/word boundaries over a hard offset cut, and the stitcher must insert silence sized to how natural the boundary was.</summary>
public sealed class VoiceChunkingTests
{
    [Fact]
    public void Encode_splits_at_a_sentence_break_inside_the_window_and_the_chunk_ends_with_it()
    {
        var periodId = KokoroTokenizer_TestAccess.SentenceBreakId();
        var before = new string(KokoroVocab.Ash[0], 300);
        var after = new string(KokoroVocab.Ash[0], 300);
        var phonemes = before + "." + after;

        var chunks = KokoroTokenizer.Encode(phonemes);

        Assert.True(chunks.Count >= 2, "input longer than one window should split into multiple chunks");
        var first = chunks[0];
        Assert.Equal(PhonemeChunkBoundary.SentenceBreak, first.Boundary);
        // Last id before the trailing pad token is the period itself.
        Assert.Equal(periodId, first.Ids[^2]);
    }

    [Fact]
    public void Encode_never_produces_an_oversized_chunk_for_an_unbroken_run()
    {
        // No punctuation and no space anywhere: nothing to break on but the hard cut.
        var phonemes = new string(KokoroVocab.Ash[0], KokoroVocab.MaxSequenceTokens * 2 + 5);

        var chunks = KokoroTokenizer.Encode(phonemes);

        Assert.True(chunks.Count >= 3);
        foreach (var chunk in chunks)
            Assert.True(chunk.Ids.Length <= KokoroVocab.MaxSequenceTokens + 2, "a chunk must never exceed the context window plus the two pad tokens");
        Assert.All(chunks.Take(chunks.Count - 1), c => Assert.Equal(PhonemeChunkBoundary.HardCut, c.Boundary));
    }

    [Fact]
    public void Encode_preserves_the_total_non_pad_token_count_across_chunks()
    {
        var before = new string(KokoroVocab.Ash[0], 300);
        var after = new string(KokoroVocab.Ash[0], 300);
        var phonemes = before + "." + after;

        var chunks = KokoroTokenizer.Encode(phonemes);
        var totalNonPad = chunks.Sum(c => c.Ids.Length - 2);

        Assert.Equal(phonemes.Length, totalNonPad);
    }

    [Fact]
    public void Encode_splits_at_a_clause_break_when_no_sentence_break_exists_in_the_window()
    {
        var before = new string(KokoroVocab.Ash[0], 300);
        var after = new string(KokoroVocab.Ash[0], 300);
        var phonemes = before + "," + after;

        var chunks = KokoroTokenizer.Encode(phonemes);

        Assert.Equal(PhonemeChunkBoundary.ClauseOrSpace, chunks[0].Boundary);
    }

    [Fact]
    public void Encode_splits_at_a_word_space_when_no_punctuation_break_exists_in_the_window()
    {
        var before = new string(KokoroVocab.Ash[0], 300);
        var after = new string(KokoroVocab.Ash[0], 300);
        var phonemes = before + " " + after;

        var chunks = KokoroTokenizer.Encode(phonemes);

        Assert.Equal(PhonemeChunkBoundary.ClauseOrSpace, chunks[0].Boundary);
    }

    // ── Stitching: silence sized by boundary kind ───────────────────────────────

    [Fact]
    public void SilenceSamples_is_longer_after_a_sentence_break_than_a_clause_or_space_break()
    {
        var sentenceSilence = NativeKokoroVoiceProvider.SilenceSamples(PhonemeChunkBoundary.SentenceBreak);
        var clauseSilence = NativeKokoroVoiceProvider.SilenceSamples(PhonemeChunkBoundary.ClauseOrSpace);

        Assert.True(sentenceSilence.Length > clauseSilence.Length);
        Assert.All(sentenceSilence, s => Assert.Equal(0f, s));
        Assert.All(clauseSilence, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void SilenceSamples_inserts_nothing_for_a_hard_cut()
    {
        Assert.Empty(NativeKokoroVoiceProvider.SilenceSamples(PhonemeChunkBoundary.HardCut));
    }

    [Fact]
    public void Stitched_sample_count_equals_the_chunk_totals_plus_the_inserted_silence()
    {
        // Simulates the stitch loop in GenerateSpeechAsync without a live ONNX session.
        float[] Fake(int n) => new float[n];
        var chunkSamples = new[] { Fake(1000), Fake(2000), Fake(500) };
        var boundaries = new[] { PhonemeChunkBoundary.SentenceBreak, PhonemeChunkBoundary.ClauseOrSpace };

        var stitched = new List<float>();
        for (var i = 0; i < chunkSamples.Length; i++)
        {
            stitched.AddRange(chunkSamples[i]);
            if (i < boundaries.Length)
                stitched.AddRange(NativeKokoroVoiceProvider.SilenceSamples(boundaries[i]));
        }

        var expectedSilence = NativeKokoroVoiceProvider.SilenceSamples(PhonemeChunkBoundary.SentenceBreak).Length
            + NativeKokoroVoiceProvider.SilenceSamples(PhonemeChunkBoundary.ClauseOrSpace).Length;
        Assert.Equal(chunkSamples.Sum(c => c.Length) + expectedSilence, stitched.Count);
    }
}

/// <summary>Tiny seam so the test can name the exact vocab id for "." without duplicating the literal.</summary>
internal static class KokoroTokenizer_TestAccess
{
    public static int SentenceBreakId() => KokoroVocab.SymbolToId["."];
}
