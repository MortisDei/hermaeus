using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Voice;

/// <summary>
/// Converts English text to phonemes for Kokoro's ONNX graph. Numbers,
/// currency, percentages, ordinals and clock times are expanded to words
/// first (<see cref="KokoroTextNormalizer"/>), then each word is resolved in
/// order: the user's pronunciation lexicon, the embedded CMU Pronouncing
/// Dictionary (<see cref="CmuPronouncingDictionary"/>), a suffix-stripping
/// retry against both of those, unknown all-caps acronyms spelled out
/// letter by letter, and finally a small letter-by-letter rule fallback for
/// anything still unresolved (names, invented words, typos). Pronunciation
/// accuracy on that final fallback tier is approximate by design.
/// </summary>
internal static class KokoroPhonemizer
{
    public static string ToPhonemes(string text, string? userLexiconPath = null, IRuntimeLogService? logs = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = KokoroTextNormalizer.Normalize(text);
        normalized = InjectParagraphPauses(normalized);
        var sb = new StringBuilder();
        var words = normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            if (i > 0)
                sb.Append(KokoroVocab.Space);

            AppendWord(sb, words[i], userLexiconPath, logs);
        }

        return sb.ToString();
    }

    private static void AppendWord(StringBuilder sb, string rawWord, string? userLexiconPath, IRuntimeLogService? logs)
    {
        var trailingPunctuation = new StringBuilder();
        var word = rawWord;
        while (word.Length > 0 && IsSentencePunctuation(word[^1]))
        {
            trailingPunctuation.Insert(0, word[^1]);
            word = word[..^1];
        }

        var core = word.Trim('"', '\'', '(', ')', '[', ']');
        if (core.Length == 0)
        {
            sb.Append(trailingPunctuation);
            return;
        }

        sb.Append(ResolveCore(core, userLexiconPath, logs));
        sb.Append(trailingPunctuation);
    }

    private static bool IsSentencePunctuation(char c) => c is '.' or ',' or '!' or '?' or ';' or ':';

    // ── Paragraph pauses (r19 4.2) ──────────────────────────────────────────
    // The word-splitter below treats a paragraph break the same as a single
    // space (it splits on ALL whitespace), so "\n\n" between paragraphs used
    // to contribute no pause at all. A blank line (two or more newlines,
    // with optional surrounding horizontal whitespace) becomes a sentence
    // pause here, before the split, unless the text already ends in
    // sentence punctuation right before the break.
    private static readonly Regex ParagraphBreakPattern =
        new(@"[ \t]*\r?\n[ \t]*\r?\n[ \t\r\n]*", RegexOptions.Compiled);

    private static string InjectParagraphPauses(string text)
    {
        return ParagraphBreakPattern.Replace(text, match =>
        {
            var k = match.Index - 1;
            while (k >= 0 && char.IsWhiteSpace(text[k]))
                k--;
            var needsPause = k < 0 || !IsSentencePunctuation(text[k]);
            return needsPause ? ". " : " ";
        });
    }

    private static string ResolveCore(string core, string? userLexiconPath, IRuntimeLogService? logs)
    {
        var lower = core.ToLowerInvariant();

        if (KokoroUserLexicon.TryGetIpa(userLexiconPath, lower, out var userIpa, logs))
            return userIpa;

        if (CmuPronouncingDictionary.TryGetIpa(lower, out var cmuIpa))
            return cmuIpa;

        if (TryResolveWithSuffixMorphology(lower, userLexiconPath, logs, out var morphIpa))
            return morphIpa;

        if (IsAcronymCandidate(core))
            return SpellAcronym(core);

        LogFallbackWord(lower, logs);
        var sb = new StringBuilder();
        AppendFallback(sb, lower);
        return sb.ToString();
    }

    // ── Fallback diagnosability (r10 03-field-follow-ups.md 3.3 item 5) ────
    // The letter-by-letter fallback tier is approximate by design; logging
    // which words actually hit it (once per distinct word per session) lets
    // the next pronunciation report be checked against real fallback words
    // instead of guessed at.
    private static readonly ConcurrentDictionary<string, byte> LoggedFallbackWords = new(StringComparer.Ordinal);

    private static void LogFallbackWord(string lower, IRuntimeLogService? logs)
    {
        if (logs is null) return;
        if (!LoggedFallbackWords.TryAdd(lower, 0)) return;
        logs.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            RuntimeLogLevel.Debug,
            RuntimeLogCategory.Voice,
            $"Pronunciation letter-rule fallback used for '{lower}'"));
    }

    // ── Suffix morphology (doc 01 item 1.4) ─────────────────────────────────

    private static bool TryResolveWithSuffixMorphology(string lower, string? userLexiconPath, IRuntimeLogService? logs, out string ipa)
    {
        ipa = string.Empty;

        if (lower.EndsWith("'s", StringComparison.Ordinal) && lower.Length > 2)
        {
            var stem = lower[..^2];
            if (TryResolveStem(stem, userLexiconPath, logs, out var stemIpa))
            {
                ipa = stemIpa + SuffixPhoneme(stemIpa, isPastTense: false);
                return true;
            }
        }

        if (lower.EndsWith("ing", StringComparison.Ordinal) && lower.Length > 4)
        {
            var stem = lower[..^3];
            if (TryResolveStemWithSilentE(stem, userLexiconPath, logs, out var stemIpa))
            {
                ipa = stemIpa + "ɪŋ";
                return true;
            }
        }

        if (lower.EndsWith("ed", StringComparison.Ordinal) && lower.Length > 3)
        {
            var stem = lower[..^2];
            if (TryResolveStemWithSilentE(stem, userLexiconPath, logs, out var stemIpa))
            {
                ipa = stemIpa + SuffixPhoneme(stemIpa, isPastTense: true);
                return true;
            }
        }

        if (lower.EndsWith("es", StringComparison.Ordinal) && lower.Length > 3)
        {
            var stem = lower[..^2];
            if (TryResolveStem(stem, userLexiconPath, logs, out var stemIpa))
            {
                ipa = stemIpa + "ɪz";
                return true;
            }
        }

        if (lower.EndsWith("s", StringComparison.Ordinal) && !lower.EndsWith("ss", StringComparison.Ordinal) && lower.Length > 2)
        {
            var stem = lower[..^1];
            if (TryResolveStem(stem, userLexiconPath, logs, out var stemIpa))
            {
                ipa = stemIpa + SuffixPhoneme(stemIpa, isPastTense: false);
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveStem(string stem, string? userLexiconPath, IRuntimeLogService? logs, out string ipa)
    {
        if (KokoroUserLexicon.TryGetIpa(userLexiconPath, stem, out ipa, logs)) return true;
        if (CmuPronouncingDictionary.TryGetIpa(stem, out ipa)) return true;
        ipa = string.Empty;
        return false;
    }

    /// <summary>Tries the bare stem, then the stem with a silent 'e' restored ("hop" -> "hope").</summary>
    private static bool TryResolveStemWithSilentE(string stem, string? userLexiconPath, IRuntimeLogService? logs, out string ipa)
    {
        if (TryResolveStem(stem, userLexiconPath, logs, out ipa)) return true;
        return TryResolveStem(stem + "e", userLexiconPath, logs, out ipa);
    }

    private static readonly HashSet<char> VoicelessConsonants = ['p', 't', 'k', 'f', 'θ', 's', 'ʃ', 'ʧ', 'h'];
    private static readonly HashSet<char> Sibilants = ['s', 'z', 'ʃ', 'ʒ', 'ʧ', 'ʤ'];

    private static string SuffixPhoneme(string stemIpa, bool isPastTense)
    {
        var last = LastSoundChar(stemIpa);
        if (isPastTense)
        {
            if (last is 't' or 'd') return "ɪd";
            return VoicelessConsonants.Contains(last) ? "t" : "d";
        }

        if (Sibilants.Contains(last)) return "ɪz";
        return VoicelessConsonants.Contains(last) ? "s" : "z";
    }

    private static char LastSoundChar(string ipa)
    {
        for (var i = ipa.Length - 1; i >= 0; i--)
        {
            var c = ipa[i];
            if (c is 'ˈ' or 'ˌ' or 'ː') continue;
            return c;
        }
        return '\0';
    }

    // ── Unknown all-caps acronyms (doc 01 item 1.1's spelling bullet) ──────

    private static bool IsAcronymCandidate(string core) =>
        core.Length is >= 2 and <= 6 && core.All(char.IsUpper);

    private static readonly IReadOnlyDictionary<char, string> LetterNames = new Dictionary<char, string>
    {
        ['a'] = "eɪ", ['b'] = "bi", ['c'] = "si", ['d'] = "di", ['e'] = "i",
        ['f'] = "ɛf", ['g'] = "ʤi", ['h'] = "eɪʧ", ['i'] = "aɪ", ['j'] = "ʤeɪ",
        ['k'] = "keɪ", ['l'] = "ɛl", ['m'] = "ɛm", ['n'] = "ɛn", ['o'] = "oʊ",
        ['p'] = "pi", ['q'] = "kju", ['r'] = "ɑɹ", ['s'] = "ɛs", ['t'] = "ti",
        ['u'] = "ju", ['v'] = "vi", ['w'] = "dʌbəlju", ['x'] = "ɛks", ['y'] = "waɪ",
        ['z'] = "zi"
    };

    private static string SpellAcronym(string core)
    {
        var sb = new StringBuilder();
        foreach (var c in core.ToLowerInvariant())
        {
            if (!LetterNames.TryGetValue(c, out var name))
                continue;
            if (sb.Length > 0)
                sb.Append(KokoroVocab.Space);
            sb.Append(name);
        }
        return sb.ToString();
    }

    // ── Rule-based fallback for words the dictionary has no entry for ──────

    private static bool HasMagicE(string word)
    {
        if (word.Length <= 3 || word[^1] != 'e' || "aeiouy".Contains(word[^2]))
            return false;

        for (var i = word.Length - 3; i >= 0; i--)
        {
            if ("aeiouy".Contains(word[i]))
                return true;
        }

        return false;
    }

    private static void AppendFallback(StringBuilder sb, string word)
    {
        // "Magic E" detection: the real English pattern is vowel-CONSONANT-e
        // ("joke", "hope", "state"), not vowel-e ("see"). A trailing 'e' is
        // silent only when the character right before it is a consonant AND
        // a vowel appears somewhere before that consonant.
        bool hasMagicE = HasMagicE(word);

        var i = 0;
        while (i < word.Length)
        {
            if (hasMagicE && i == word.Length - 1)
            {
                i++; // Skip the silent 'e'
                break;
            }

            // Rhoticity: Check for vowel + 'r' combinations
            if (i + 1 < word.Length && word[i + 1] == 'r' && "aeiouy".Contains(word[i]))
            {
                char v = word[i];
                string rhotic = v switch
                {
                    'a' => "ɑɹ",
                    'e' => "ɚ",
                    // "ɝ" (stressed r-colored vowel) is not in Kokoro's vocabulary;
                    // "ɜɹ" stays within the vocab and matches the a/o/u pattern below.
                    'i' => "ɜɹ",
                    'o' => "ɔɹ",
                    'u' => "ʊɹ",
                    _ => KokoroVocab.TurnedR
                };
                sb.Append(rhotic);
                i += 2;
                continue;
            }

            var consumed = TryMatchDigraph(word, i, out var phoneme);
            if (consumed == 0)
            {
                consumed = 1;
                char c = word[i];

                // Simple vowel shift for Magic E
                if (hasMagicE && "aeiouy".Contains(c))
                {
                    phoneme = c switch
                    {
                        'a' => "e", // a -> eɪ (approx)
                        'e' => "i", // e -> iː (approx)
                        'i' => "aɪ", // i -> aɪ
                        'o' => "oʊ", // o -> oʊ
                        'u' => "ju", // u -> juː
                        _ => MapLetter(c)
                    };
                }
                else
                {
                    phoneme = MapLetter(c);
                }
            }

            sb.Append(phoneme);
            i += consumed;
        }
    }

    private static int TryMatchDigraph(string word, int i, out string phoneme)
    {
        var remaining = word.Length - i;
        if (remaining >= 4)
        {
            var quad = word.Substring(i, 4);
            if (quad == "tion") { phoneme = "ʃən"; return 4; }
            if (quad == "sion") { phoneme = "ʒən"; return 4; }
        }

        if (remaining >= 2)
        {
            var pair = word.Substring(i, 2);
            switch (pair)
            {
                case "th": phoneme = KokoroVocab.Theta; return 2;
                case "sh": phoneme = KokoroVocab.Esh; return 2;
                case "ch": phoneme = "ʧ"; return 2;
                case "ng": phoneme = KokoroVocab.Eng; return 2;
                case "ph": phoneme = "f"; return 2;
                case "qu": phoneme = "k" + "w"; return 2;
                case "ck": phoneme = "k"; return 2;
                case "oo": phoneme = KokoroVocab.NearCloseU; return 2;
                case "ee": phoneme = "i"; return 2;
                case "ea": phoneme = "i"; return 2;
                case "ai": phoneme = "e" + KokoroVocab.NearCloseI; return 2;
                case "ay": phoneme = "e" + KokoroVocab.NearCloseI; return 2;
                case "oy": phoneme = KokoroVocab.OpenO + KokoroVocab.NearCloseI; return 2;
                case "oi": phoneme = KokoroVocab.OpenO + KokoroVocab.NearCloseI; return 2;
                case "ou": phoneme = KokoroVocab.OpenBackA + KokoroVocab.NearCloseU; return 2;
                case "ow": phoneme = KokoroVocab.OpenBackA + KokoroVocab.NearCloseU; return 2;
                // Word-initial "gh" is a hard /g/ (ghost, ghastly); word-final/medial
                // "gh" (enough, laugh, tough) is /f/, handled by the fallthrough below.
                case "gh" when i == 0: phoneme = KokoroVocab.ScriptG; return 2;
                case "gh": phoneme = "f"; return 2;
                case "wh": phoneme = "w"; return 2;
                case "kn": phoneme = "n"; return 2;
            }
        }

        phoneme = string.Empty;
        return 0;
    }

    private static string MapLetter(char c) => c switch
    {
        'a' => KokoroVocab.Ash,
        'e' => KokoroVocab.OpenMidE,
        'i' => KokoroVocab.NearCloseI,
        'o' => KokoroVocab.OpenBackA,
        'u' => KokoroVocab.Wedge,
        'y' => KokoroVocab.NearCloseI,
        'g' => KokoroVocab.ScriptG,
        'j' => "ʤ",
        'r' => KokoroVocab.TurnedR,
        'x' => "ks",
        'c' => "k",
        'q' => "k",
        _ when char.IsLetter(c) => c.ToString(),
        _ => string.Empty
    };
}
