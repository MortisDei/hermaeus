using Aether.Voice;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

/// <summary>
/// docs/review/01-voice-pronunciation.md: text normalization, the CMUdict
/// lexicon, the user override lexicon, suffix morphology, and the golden
/// pronunciation regression set.
/// </summary>
internal static class VoicePronunciationTests
{
    // ── 1.1 Text normalizer ─────────────────────────────────────────────────

    public static Task NormalizerExpandsCardinalsWithThousandsSeparators()
    {
        Equal("i have one thousand two hundred thirty four dollars saved", KokoroTextNormalizer.Normalize("i have 1,234 dollars saved"), "Comma-grouped thousands must expand to words.");
        Equal("one hundred", KokoroTextNormalizer.Normalize("100"), "Three-digit round number must expand correctly.");
        Equal("one billion", KokoroTextNormalizer.Normalize("1000000000"), "A ten-digit number must expand up to the billions tier.");
        return Task.CompletedTask;
    }

    public static Task NormalizerExpandsDecimals()
    {
        Equal("three point one four", KokoroTextNormalizer.Normalize("3.14"), "Decimals must read each fractional digit individually after 'point'.");
        return Task.CompletedTask;
    }

    public static Task NormalizerExpandsNegatives()
    {
        Equal("minus four", KokoroTextNormalizer.Normalize("-4"), "A minus sign directly prefixing a number must read as 'minus'.");
        // A hyphenated range must not be misread as a negative number.
        Equal("ten-twenty", KokoroTextNormalizer.Normalize("10-20"), "A hyphen between two numbers (a range) must not be read as a negative sign.");
        return Task.CompletedTask;
    }

    public static Task NormalizerExpandsOrdinals()
    {
        Equal("first", KokoroTextNormalizer.Normalize("1st"), "1st must expand to 'first'.");
        Equal("second", KokoroTextNormalizer.Normalize("2nd"), "2nd must expand to 'second'.");
        Equal("third", KokoroTextNormalizer.Normalize("3rd"), "3rd must expand to 'third'.");
        Equal("twenty first", KokoroTextNormalizer.Normalize("21st"), "21st must expand to 'twenty first'.");
        return Task.CompletedTask;
    }

    public static Task NormalizerExpandsPercent()
    {
        Equal("eighty five percent", KokoroTextNormalizer.Normalize("85%"), "Percent values must expand to words followed by 'percent'.");
        return Task.CompletedTask;
    }

    public static Task NormalizerExpandsCurrency()
    {
        Equal("five dollars", KokoroTextNormalizer.Normalize("$5"), "Whole dollar amounts must expand with a pluralized 'dollars'.");
        Equal("five dollars twenty cents", KokoroTextNormalizer.Normalize("$5.20"), "Dollars-and-cents amounts must expand both parts.");
        Equal("one dollar", KokoroTextNormalizer.Normalize("$1"), "Singular dollar amount must not pluralize 'dollar'.");
        return Task.CompletedTask;
    }

    public static Task NormalizerExpandsClockTimes()
    {
        Equal("three thirty", KokoroTextNormalizer.Normalize("3:30"), "Clock times must read as hour then minutes.");
        Equal("three oh five", KokoroTextNormalizer.Normalize("3:05"), "Minutes under ten must read with a leading 'oh'.");
        return Task.CompletedTask;
    }

    public static Task NormalizerExpandsStandaloneSymbols()
    {
        Equal("cats and dogs", KokoroTextNormalizer.Normalize("cats & dogs"), "A standalone ampersand must expand to 'and'.");
        Equal("this plus that", KokoroTextNormalizer.Normalize("this + that"), "A standalone plus sign must expand to 'plus'.");
        return Task.CompletedTask;
    }

    public static Task NormalizerLeavesPlainProseUnchanged()
    {
        Equal("the quick brown fox", KokoroTextNormalizer.Normalize("the quick brown fox"), "Plain prose with no digits or symbols must pass through unchanged.");
        return Task.CompletedTask;
    }

    public static Task NormalizerDoesNotDropDigitsThatWereTheOriginalBug()
    {
        // aea2326-era bug: MapLetter mapped non-letters to empty and the
        // tokenizer silently skipped them, so "3" vanished entirely.
        var normalized = KokoroTextNormalizer.Normalize("You have 3 errors");
        True(normalized.Contains("three"), $"Expected digit 3 to expand to the word 'three', got: {normalized}");
        return Task.CompletedTask;
    }

    // ── 1.2 CMUdict lexicon ──────────────────────────────────────────────────

    public static Task ArpabetIpaMapUsesOnlyVocabSymbols()
    {
        foreach (var (arpabet, ipa) in ArpabetIpaMap.Map)
            foreach (var c in ipa)
                True(KokoroVocab.SymbolToId.ContainsKey(c.ToString()), $"ARPABET '{arpabet}' maps to '{ipa}' which contains '{c}' (U+{(int)c:X4}), not in Kokoro's vocabulary.");

        foreach (var c in ArpabetIpaMap.UnstressedAh)
            True(KokoroVocab.SymbolToId.ContainsKey(c.ToString()), "Unstressed AH symbol must be in the vocabulary.");
        foreach (var c in ArpabetIpaMap.StressedAh)
            True(KokoroVocab.SymbolToId.ContainsKey(c.ToString()), "Stressed AH symbol must be in the vocabulary.");
        return Task.CompletedTask;
    }

    public static Task CmuDictResolvesGoldenWordsToExactIpa()
    {
        AssertCmu("voice", "vˈɔɪs");
        AssertCmu("choice", "ʧˈɔɪs");
        AssertCmu("colonel", "kˈɚnəl");
        AssertCmu("wednesday", "wˈɛnzdi");
        AssertCmu("queue", "kjˈu");
        AssertCmu("ghost", "ɡˈoʊst");
        return Task.CompletedTask;
    }

    public static Task CmuDictMissWordsFallBackGracefully()
    {
        False(CmuPronouncingDictionary.TryGetIpa("gguf", out _), "cmudict should not contain the invented token 'gguf'.");
        return Task.CompletedTask;
    }

    // ── 1.3 User override lexicon ────────────────────────────────────────────

    public static async Task UserLexiconOverridesCmuDict()
    {
        using var temp = new TempDir();
        var lexiconPath = temp.PathFor("lexicon.txt");
        await File.WriteAllTextAsync(lexiconPath, "voice = ˈtɛst\n");

        var phonemes = KokoroPhonemizer.ToPhonemes("voice", lexiconPath);
        Equal("ˈtɛst", phonemes, "A user lexicon entry must take priority over the CMUdict pronunciation.");
    }

    public static Task UserLexiconSeedsDefaultsIncludingTheAppsOwnName()
    {
        using var temp = new TempDir();
        var lexiconPath = temp.PathFor("voice/lexicon.txt");

        var phonemes = KokoroPhonemizer.ToPhonemes("aether", lexiconPath);
        Equal("ˈiθɚ", phonemes, "Aether's own name must be seeded into a fresh user lexicon file.");
        True(File.Exists(lexiconPath), "The lexicon file must be written to disk on first use.");
        return Task.CompletedTask;
    }

    public static async Task UserLexiconSkipsInvalidLinesWithoutThrowing()
    {
        using var temp = new TempDir();
        var lexiconPath = temp.PathFor("lexicon.txt");
        await File.WriteAllTextAsync(lexiconPath, "not a valid line\nvoice = ˈtɛst\nbadword = xyz123notipa\n");

        var phonemes = KokoroPhonemizer.ToPhonemes("voice", lexiconPath);
        Equal("ˈtɛst", phonemes, "A valid line after invalid ones must still be parsed.");

        False(KokoroUserLexicon.TryGetIpa(lexiconPath, "badword", out _), "A line whose IPA contains characters outside Kokoro's vocabulary must be skipped.");
    }

    public static async Task UserLexiconReloadsWhenFileChanges()
    {
        using var temp = new TempDir();
        var lexiconPath = temp.PathFor("lexicon.txt");
        await File.WriteAllTextAsync(lexiconPath, "voice = ˈtɛst\n");
        Equal("ˈtɛst", KokoroPhonemizer.ToPhonemes("voice", lexiconPath), "Initial lexicon content must be picked up.");

        // Ensure the mtime actually advances on filesystems with coarse timestamp resolution.
        await Task.Delay(50);
        await File.WriteAllTextAsync(lexiconPath, "voice = ˈnu\n");
        Equal("ˈnu", KokoroPhonemizer.ToPhonemes("voice", lexiconPath), "Changing the lexicon file must be picked up without restarting the app.");
    }

    // ── 1.4 Morphological retry ──────────────────────────────────────────────

    public static Task MorphologyResolvesPossessiveOfUserLexiconWord()
    {
        using var temp = new TempDir();
        var lexiconPath = temp.PathFor("voice/lexicon.txt");
        // Force the default seed (which includes "aether") to be written first.
        KokoroPhonemizer.ToPhonemes("aether", lexiconPath);

        Equal("ˈiθɚz", KokoroPhonemizer.ToPhonemes("aether's", lexiconPath), "Possessive suffix morphology should reuse the stem's lexicon pronunciation plus a voiced 's'.");
        return Task.CompletedTask;
    }

    public static Task CommonInflectedFormsResolveToExactIpa()
    {
        AssertCmu("servers", "sˈɚvɚz");
        AssertCmu("wanted", "wˈɔntɪd");
        AssertCmu("running", "ɹˈʌnɪŋ");
        return Task.CompletedTask;
    }

    public static Task WordInitialGhIsHardGNotF()
    {
        // "ghorx" is not a real word (and not in cmudict), so it exercises
        // the rule-based fallback tier directly rather than a dictionary hit.
        False(CmuPronouncingDictionary.TryGetIpa("ghorx", out _), "'ghorx' must not be a real cmudict entry for this test to exercise the fallback.");
        var phonemes = KokoroPhonemizer.ToPhonemes("ghorx");
        True(phonemes.StartsWith(KokoroVocab.ScriptG, StringComparison.Ordinal), $"Word-initial 'gh' should phonemize as a hard g, got: {phonemes}");
        return Task.CompletedTask;
    }

    // ── Unknown all-caps acronyms ────────────────────────────────────────────

    public static Task UnknownAcronymIsSpelledOutLetterByLetter()
    {
        var phonemes = KokoroPhonemizer.ToPhonemes("GGUF");
        Equal("ʤi ʤi ju ɛf", phonemes, "An unknown all-caps acronym must be spelled out using letter names, not raw letter sounds.");
        return Task.CompletedTask;
    }

    public static Task KnownAcronymUsesItsRealCmuDictPronunciation()
    {
        // "api" has a real cmudict entry ("ay pee eye"); it must not be
        // re-spelled by the acronym fallback.
        var viaCmu = CmuPronouncingDictionary.TryGetIpa("api", out var expected);
        True(viaCmu, "cmudict is expected to already have an entry for 'api'.");
        Equal(expected, KokoroPhonemizer.ToPhonemes("API"), "A known acronym must use its real dictionary pronunciation, not the letter-spelling fallback.");
        return Task.CompletedTask;
    }

    // ── 1.5 Golden pronunciation regression set ──────────────────────────────

    private static readonly string[] GoldenSentences =
    [
        "You have 3 errors",
        "It costs $5.20",
        "The meeting is at 3:30",
        "She finished 21st in the race",
        "Battery is at 85%",
        "The temperature is -4 degrees",
        "Pi is approximately 3.14",
        "cats & dogs",
        "This is a GGUF model file",
        "Aether uses Ollama and Kokoro",
        "The voice sounds robotic",
        "He made the right choice",
        "Wednesday is the deadline",
        "Please queue the next request",
        "The ghost story was frightening",
        "The servers are running",
        "She wanted to leave early",
        "I can't believe it's already done",
        "We should review the API response",
        "The quick brown fox jumps over the lazy dog"
    ];

    public static Task GoldenSentencesProduceStablePhonemization()
    {
        foreach (var sentence in GoldenSentences)
        {
            var first = KokoroPhonemizer.ToPhonemes(sentence);
            var second = KokoroPhonemizer.ToPhonemes(sentence);
            Equal(first, second, $"Phonemization of '{sentence}' must be deterministic.");
            True(first.Length > 0, $"'{sentence}' must produce non-empty phonemes.");
        }
        return Task.CompletedTask;
    }

    public static Task GoldenSentencesDropNoCharactersDuringTokenization()
    {
        foreach (var sentence in GoldenSentences)
        {
            var phonemes = KokoroPhonemizer.ToPhonemes(sentence);
            var chunks = KokoroTokenizer.Encode(phonemes);
            var totalTokens = chunks.Sum(c => c.Length) - (chunks.Count * 2); // minus the two pad tokens per chunk
            Equal(phonemes.Length, totalTokens, $"'{sentence}' phonemized to {phonemes.Length} chars but only {totalTokens} were tokenized; some characters were silently dropped.");
        }
        return Task.CompletedTask;
    }

    private static void AssertCmu(string word, string expectedIpa)
    {
        True(CmuPronouncingDictionary.TryGetIpa(word, out var ipa), $"Expected cmudict to contain '{word}'.");
        Equal(expectedIpa, ipa, $"Unexpected IPA for '{word}'.");
        Equal(expectedIpa, KokoroPhonemizer.ToPhonemes(word), $"Phonemizer output for '{word}' should match its cmudict entry exactly.");
    }
}
