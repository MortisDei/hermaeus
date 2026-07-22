using Hermaeus.Voice;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>r19 4.2: paragraph breaks must contribute a pause, and punctuation must survive end to end through tokenization.</summary>
public sealed class VoicePunctuationAdherenceTests
{
    [Fact]
    public void Two_paragraph_input_yields_a_sentence_pause_token_at_the_break()
    {
        var phonemes = KokoroPhonemizer.ToPhonemes("First paragraph\n\nSecond paragraph");
        var periodId = KokoroVocab.SymbolToId["."];

        var chunks = KokoroTokenizer.Encode(phonemes);
        var allIds = chunks.SelectMany(c => c.Ids).ToList();

        Assert.Contains(periodId, allIds);
    }

    [Fact]
    public void Single_paragraph_input_has_no_sentence_pause_injected()
    {
        var phonemes = KokoroPhonemizer.ToPhonemes("one two three");
        var periodId = KokoroVocab.SymbolToId["."];

        var chunks = KokoroTokenizer.Encode(phonemes);
        var allIds = chunks.SelectMany(c => c.Ids).ToList();

        Assert.DoesNotContain(periodId, allIds);
    }

    [Fact]
    public void A_paragraph_break_already_ending_in_punctuation_does_not_get_a_second_pause_token()
    {
        var withPeriod = KokoroPhonemizer.ToPhonemes("First paragraph.\n\nSecond paragraph");
        var withoutPeriod = KokoroPhonemizer.ToPhonemes("First paragraph\n\nSecond paragraph");
        var periodId = KokoroVocab.SymbolToId["."];

        var countWith = KokoroTokenizer.Encode(withPeriod).SelectMany(c => c.Ids).Count(id => id == periodId);
        var countWithout = KokoroTokenizer.Encode(withoutPeriod).SelectMany(c => c.Ids).Count(id => id == periodId);

        Assert.Equal(countWith, countWithout);
    }

    [Fact]
    public void WaitEllipsisWhat_retains_the_pause_tokens_end_to_end()
    {
        var phonemes = KokoroPhonemizer.ToPhonemes("wait... what?");
        var periodId = KokoroVocab.SymbolToId["."];
        var questionId = KokoroVocab.SymbolToId["?"];

        var allIds = KokoroTokenizer.Encode(phonemes).SelectMany(c => c.Ids).ToList();

        Assert.Equal(3, allIds.Count(id => id == periodId));
        Assert.Contains(questionId, allIds);
    }

    [Fact]
    public void An_em_dash_normalizes_to_a_comma_pause_that_survives_tokenization()
    {
        var emDash = char.ConvertFromUtf32(0x2014);
        var phonemes = KokoroPhonemizer.ToPhonemes("wait" + emDash + "what");
        var commaId = KokoroVocab.SymbolToId[","];

        var allIds = KokoroTokenizer.Encode(phonemes).SelectMany(c => c.Ids).ToList();

        Assert.Contains(commaId, allIds);
    }
}
