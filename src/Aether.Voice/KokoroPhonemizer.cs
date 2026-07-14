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
        // Basic "Magic E" detection: if word ends in 'e' and has a vowel before it,
        // we treat the 'e' as silent and potentially shift the vowel.
        bool hasMagicE = word.Length > 2 && word[^1] == 'e' && "aeiouy".Contains(word[^2]);

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
                    'i' => "ɝ",
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

    /// <summary>
    /// A small set of very common English words whose pronunciation the
    /// letter-fallback rules above would otherwise get noticeably wrong
    /// (silent letters, irregular vowels). Not exhaustive by design.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Dictionary = BuildDictionary();

    private static IReadOnlyDictionary<string, string> BuildDictionary()
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Function words
            ["the"] = "ðə",
            ["a"] = "ə",
            ["an"] = "æn",
            ["and"] = "ænd",
            ["of"] = "ʌv",
            ["to"] = "tu",
            ["in"] = "ɪn",
            ["on"] = "ɑn",
            ["at"] = "æt",
            ["for"] = "fɔɹ",
            ["from"] = "fɹʌm",
            ["with"] = "wɪθ",
            ["without"] = "wɪˈðaʊt",
            ["into"] = "ˈɪntu",
            ["onto"] = "ˈɑntu",
            ["over"] = "ˈoʊvɚ",
            ["under"] = "ˈʌndɚ",
            ["between"] = "bɪˈtwiːn",
            ["through"] = "θɹu",
            ["around"] = "ɚˈaʊnd",
            ["about"] = "əˈbaʊt",
            ["before"] = "bɪˈfɔɹ",
            ["after"] = "ˈæftɚ",
            ["during"] = "ˈdʊɹɪŋ",
            ["while"] = "waɪl",
            ["until"] = "ʌnˈtɪl",

            // Pronouns
            ["i"] = "aɪ",
            ["me"] = "mi",
            ["my"] = "maɪ",
            ["mine"] = "maɪn",
            ["you"] = "ju",
            ["your"] = "jɔɹ",
            ["yours"] = "jɔɹz",
            ["he"] = "hi",
            ["him"] = "hɪm",
            ["his"] = "hɪz",
            ["she"] = "ʃi",
            ["her"] = "hɚ",
            ["hers"] = "hɚz",
            ["we"] = "wi",
            ["us"] = "ʌs",
            ["our"] = "aʊɹ",
            ["they"] = "ðeɪ",
            ["them"] = "ðɛm",
            ["their"] = "ðɛɹ",
            ["theirs"] = "ðɛɹz",

            // Demonstratives
            ["this"] = "ðɪs",
            ["that"] = "ðæt",
            ["these"] = "ðiːz",
            ["those"] = "ðoʊz",

            // Question words
            ["what"] = "wʌt",
            ["when"] = "wɛn",
            ["where"] = "wɛɹ",
            ["who"] = "hu",
            ["why"] = "waɪ",
            ["how"] = "haʊ",
            ["which"] = "wɪtʃ",
            ["whose"] = "huːz",

            // Irregular verbs
            ["be"] = "bi",
            ["am"] = "æm",
            ["is"] = "ɪz",
            ["are"] = "ɑɹ",
            ["was"] = "wʌz",
            ["were"] = "wɚ",
            ["been"] = "bɪn",
            ["have"] = "hæv",
            ["has"] = "hæz",
            ["had"] = "hæd",
            ["do"] = "du",
            ["does"] = "dʌz",
            ["did"] = "dɪd",
            ["done"] = "dʌn",
            ["say"] = "seɪ",
            ["says"] = "sɛz",
            ["said"] = "sɛd",
            ["go"] = "goʊ",
            ["goes"] = "goʊz",
            ["went"] = "wɛnt",
            ["gone"] = "ɡɔn",
            ["make"] = "meɪk",
            ["made"] = "meɪd",
            ["know"] = "noʊ",
            ["knew"] = "nu",
            ["known"] = "noʊn",
            ["take"] = "teɪk",
            ["took"] = "tʊk",
            ["taken"] = "ˈteɪkən",
            ["come"] = "kʌm",
            ["came"] = "keɪm",
            ["coming"] = "ˈkʌmɪŋ",
            ["see"] = "si",
            ["saw"] = "sɔ",
            ["seen"] = "siːn",
            ["get"] = "ɡɛt",
            ["got"] = "ɡɑt",
            ["gotten"] = "ˈɡɑtən",
            ["give"] = "ɡɪv",
            ["gave"] = "ɡeɪv",
            ["given"] = "ˈɡɪvən",

            // Modal verbs
            ["can"] = "kæn",
            ["could"] = "kʊd",
            ["should"] = "ʃʊd",
            ["would"] = "wʊd",
            ["will"] = "wɪl",
            ["shall"] = "ʃæl",
            ["may"] = "meɪ",
            ["might"] = "maɪt",
            ["must"] = "mʌst",

            // Contractions
            ["i'm"] = "aɪm",
            ["you're"] = "jɔɹ",
            ["we're"] = "wɪɹ",
            ["they're"] = "ðɛɹ",
            ["it's"] = "ɪts",
            ["that's"] = "ðæts",
            ["there's"] = "ðɛɹz",
            ["don't"] = "doʊnt",
            ["doesn't"] = "ˈdʌzənt",
            ["can't"] = "kænt",
            ["won't"] = "woʊnt",
            ["isn't"] = "ˈɪzənt",
            ["aren't"] = "ɑɹnt",
            ["shouldn't"] = "ˈʃʊdənt",
            ["wouldn't"] = "ˈwʊdənt",
            ["couldn't"] = "ˈkʊdənt",

            // Common adverbs
            ["just"] = "dʒʌst",
            ["very"] = "ˈvɛɹi",
            ["really"] = "ˈɹɪəli",
            ["only"] = "ˈoʊnli",
            ["even"] = "ˈiːvən",
            ["always"] = "ˈɔlweɪz",
            ["never"] = "ˈnɛvɚ",
            ["maybe"] = "ˈmeɪbi",
            ["almost"] = "ˈɔlmoʊst",
            ["quite"] = "kwaɪt",
            ["still"] = "stɪl",
            ["again"] = "əˈɡɛn",

            // Filler words
            ["like"] = "laɪk",
            ["well"] = "wɛl",
            ["okay"] = "oʊˈkeɪ",
            ["yeah"] = "jæ",
            ["uh"] = "ʌ",
            ["um"] = "ʌm",
            ["hmm"] = "hm",

            // Numbers
            ["zero"] = "ˈziɹoʊ",
            ["one"] = "wʌn",
            ["two"] = "tu",
            ["three"] = "θɹi",
            ["four"] = "fɔɹ",
            ["five"] = "faɪv",
            ["six"] = "sɪks",
            ["seven"] = "ˈsɛvən",
            ["eight"] = "eɪt",
            ["nine"] = "naɪn",
            ["ten"] = "tɛn",
            ["eleven"] = "ɪˈlɛvən",
            ["twelve"] = "twɛlv",
            ["thirteen"] = "ˈθɜːtiːn",
            ["fourteen"] = "ˈfɔːtiːn",
            ["fifteen"] = "ˈfɪftiːn",
            ["twenty"] = "ˈtwɛni",
            ["thirty"] = "ˈθɜːti",
            ["forty"] = "ˈfɔːti",
            ["fifty"] = "ˈfɪfti",
            ["hundred"] = "ˈhʌndɹəd",

            // Days
            ["monday"] = "ˈmʌndeɪ",
            ["tuesday"] = "ˈtuːzdeɪ",
            ["wednesday"] = "ˈwɛnzdeɪ",
            ["thursday"] = "ˈθɜːzdeɪ",
            ["friday"] = "ˈfɹaɪdeɪ",
            ["saturday"] = "ˈsætɚdeɪ",
            ["sunday"] = "ˈsʌndeɪ",

            // Months
            ["january"] = "ˈdʒænjʊˌɛɹi",
            ["february"] = "ˈfɛbɹuˌɛɹi",
            ["march"] = "mɑɹtʃ",
            ["april"] = "ˈeɪpɹəl",
            ["may"] = "meɪ",
            ["june"] = "dʒuːn",
            ["july"] = "dʒuˈlaɪ",
            ["august"] = "ˈɔːɡəst",
            ["september"] = "sɛpˈtɛmbɚ",
            ["october"] = "ɑkˈtoʊbɚ",
            ["november"] = "noʊˈvɛmbɚ",
            ["december"] = "dɪˈsɛmbɚ",

            // High-frequency nouns
            ["time"] = "taɪm",
            ["person"] = "ˈpɜːsən",
            ["year"] = "jɪɹ",
            ["way"] = "weɪ",
            ["day"] = "deɪ",
            ["thing"] = "θɪŋ",
            ["man"] = "mæn",
            ["woman"] = "ˈwʊmən",
            ["child"] = "tʃaɪld",
            ["life"] = "laɪf",
            ["world"] = "wɜːld",
            ["hand"] = "hænd",
            ["part"] = "pɑɹt",
            ["place"] = "pleɪs",
            ["work"] = "wɜːk",
            ["week"] = "wiːk",
            ["case"] = "keɪs",
            ["point"] = "pɔɪnt",
            ["government"] = "ˈɡʌvɚnmənt",
            ["company"] = "ˈkʌmpəni",
            ["number"] = "ˈnʌmbɚ",
            ["group"] = "ɡɹuːp",

            // High-frequency adjectives
            ["good"] = "ɡʊd",
            ["bad"] = "bæd",
            ["new"] = "nu",
            ["first"] = "fɜːst",
            ["last"] = "læst",
            ["long"] = "lɔŋ",
            ["great"] = "ɡɹeɪt",
            ["little"] = "ˈlɪtəl",
            ["own"] = "oʊn",
            ["other"] = "ˈʌðɚ",
            ["old"] = "oʊld",
            ["right"] = "ɹaɪt",
            ["big"] = "bɪɡ",
            ["small"] = "smɔl",

            // Critical diphthong fixes
            ["voice"] = "vɔɪs",
            ["choice"] = "tʃɔɪs",
            ["noise"] = "nɔɪz",
            ["view"] = "vju",
            ["preview"] = "ˈpɹiːvju",
            ["review"] = "ɹɪˈvju",
            ["few"] = "fju",
            ["new"] = "nju",
            ["blue"] = "blu",
            ["crew"] = "kɹu",
            ["chew"] = "tʃu",
            ["due"] = "dju",
            ["queue"] = "kju"
        };

        return entries;
    }

}
