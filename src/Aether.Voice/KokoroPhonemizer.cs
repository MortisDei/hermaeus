using System.Text;

namespace Aether.Voice;

/// <summary>
/// A deliberately small, English-only text-to-phoneme converter. This is not
/// a port of misaki (Kokoro's real G2P dependency, which needs espeak-ng and
/// full linguistic rule sets); it is a dictionary of common words plus a
/// letter-by-letter fallback for everything else, scoped exactly as
/// docs/review/archived/r1/07-roadmap.md item 5 describes: "focused, English-first ...
/// not attempting full multi-language phonemization." Pronunciation accuracy
/// on out-of-dictionary words is approximate by design.
/// </summary>
internal static class KokoroPhonemizer
{
    public static string ToPhonemes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sb = new StringBuilder();
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            if (i > 0)
                sb.Append(KokoroVocab.Space);

            AppendWord(sb, words[i]);
        }

        return sb.ToString();
    }

    private static void AppendWord(StringBuilder sb, string rawWord)
    {
        var trailingPunctuation = new StringBuilder();
        var word = rawWord;
        while (word.Length > 0 && IsSentencePunctuation(word[^1]))
        {
            trailingPunctuation.Insert(0, word[^1]);
            word = word[..^1];
        }

        var core = word.Trim('"', '(', ')');
        if (core.Length == 0)
        {
            sb.Append(trailingPunctuation);
            return;
        }

        if (Dictionary.TryGetValue(core, out var phonemes))
            sb.Append(phonemes);
        else
            AppendFallback(sb, core.ToLowerInvariant());

        sb.Append(trailingPunctuation);
    }

    private static bool IsSentencePunctuation(char c) => c is '.' or ',' or '!' or '?' or ';' or ':';

    private static void AppendFallback(StringBuilder sb, string word)
    {
        var i = 0;
        while (i < word.Length)
        {
            var consumed = TryMatchDigraph(word, i, out var phoneme);
            if (consumed == 0)
            {
                consumed = 1;
                phoneme = MapLetter(word[i]);
            }

            sb.Append(phoneme);
            i += consumed;
        }
    }

    private static int TryMatchDigraph(string word, int i, out string phoneme)
    {
        var remaining = word.Length - i;
        if (remaining >= 2)
        {
            var pair = word.Substring(i, 2);
            switch (pair)
            {
                case "th": phoneme = KokoroVocab.Theta; return 2;
                case "sh": phoneme = KokoroVocab.Esh; return 2;
                case "ch": phoneme = "t" + KokoroVocab.Esh; return 2;
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
        'j' => "d" + KokoroVocab.Ezh,
        'r' => KokoroVocab.TurnedR,
        'x' => "ks",
        'c' => "k",
        'q' => "k",
        _ when char.IsLetter(c) => c.ToString(),
        _ => string.Empty
    };

    /// <summary>
    /// A small set of very common English words whose pronunciation the
    /// letter-fallback rules above would otherwise get noticeably wrong
    /// (silent letters, irregular vowels). Not exhaustive by design.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Dictionary = BuildDictionary();

    private static IReadOnlyDictionary<string, string> BuildDictionary()
    {
        string R = KokoroVocab.TurnedR, Sch = KokoroVocab.Schwa, Ash = KokoroVocab.Ash,
            I = KokoroVocab.NearCloseI, U = KokoroVocab.NearCloseU,
            V = KokoroVocab.Wedge, Aa = KokoroVocab.OpenBackA, Oo = KokoroVocab.OpenO,
            Th = KokoroVocab.Theta, Dh = KokoroVocab.Eth, Sh = KokoroVocab.Esh,
            Ng = KokoroVocab.Eng, G = KokoroVocab.ScriptG;

        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["the"] = Dh + Sch,
            ["a"] = Sch,
            ["an"] = Ash + "n",
            ["is"] = "ɪz",
            ["was"] = "wʌz",
            ["are"] = "ɑɹ",
            ["were"] = "wɜɹ",
            ["to"] = "tu",
            ["of"] = "ʌv",
            ["and"] = Ash + "nd",
            ["in"] = "ɪn",
            ["that"] = Dh + Ash + "t",
            ["it"] = "ɪt",
            ["you"] = "ju",
            ["he"] = "hi",
            ["she"] = Sh + "i",
            ["we"] = "wi",
            ["they"] = Dh + "e" + I,
            ["this"] = Dh + "ɪs",
            ["have"] = "h" + Ash + "v",
            ["has"] = "h" + Ash + "z",
            ["had"] = "h" + Ash + "d",
            ["do"] = "du",
            ["does"] = "d" + V + "z",
            ["did"] = "dɪd",
            ["will"] = "wɪl",
            ["would"] = "w" + U + "d",
            ["can"] = "k" + Ash + "n",
            ["could"] = "k" + U + "d",
            ["should"] = Sh + U + "d",
            ["not"] = "n" + Aa + "t",
            ["no"] = "no" + U,
            ["yes"] = "jɛs",
            ["hello"] = "hɛlo" + U,
            ["world"] = "wɜɹld",
            ["one"] = "w" + V + "n",
            ["two"] = "tu",
            ["three"] = "θɹi",
            ["what"] = "w" + V + "t",
            ["when"] = "wɛn",
            ["where"] = "wɛɹ",
            ["who"] = "hu",
            ["why"] = "wa" + I,
            ["how"] = "ha" + U,
            ["i"] = "a" + I,
            ["my"] = "ma" + I,
            ["your"] = "j" + Oo + R,
            ["their"] = Dh + "ɛɹ",
            ["with"] = "wɪ" + Th,
            ["from"] = "fɹ" + V + "m",
            ["for"] = "f" + Oo + R,
            ["on"] = Aa + "n",
            ["at"] = Ash + "t",
            ["by"] = "ba" + I,
            ["all"] = Oo + "l",
            ["be"] = "bi",
            ["been"] = "bɪn",
            ["good"] = G + U + "d",
            ["going"] = G + "o" + U + "ɪ" + Ng
        };

        return entries;
    }
}
