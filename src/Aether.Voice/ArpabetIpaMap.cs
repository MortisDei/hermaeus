namespace Aether.Voice;

/// <summary>
/// Maps the 39-phone ARPABET set (as used by cmudict) onto the IPA symbols
/// present in <see cref="KokoroVocab.SymbolToId"/>. AH is stress-dependent
/// (unstressed AH0 is a schwa, stressed AH is a wedge) and is special-cased
/// by <see cref="CmuPronouncingDictionary"/> rather than listed here.
/// </summary>
internal static class ArpabetIpaMap
{
    public static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AA"] = "ɑ",
        ["AE"] = "æ",
        ["AO"] = "ɔ",
        ["AW"] = "aʊ",
        ["AY"] = "aɪ",
        ["B"] = "b",
        ["CH"] = "ʧ",
        ["D"] = "d",
        ["DH"] = "ð",
        ["EH"] = "ɛ",
        ["ER"] = "ɚ",
        ["EY"] = "eɪ",
        ["F"] = "f",
        ["G"] = "ɡ",
        ["HH"] = "h",
        ["IH"] = "ɪ",
        ["IY"] = "i",
        ["JH"] = "ʤ",
        ["K"] = "k",
        ["L"] = "l",
        ["M"] = "m",
        ["N"] = "n",
        ["NG"] = "ŋ",
        ["OW"] = "oʊ",
        ["OY"] = "ɔɪ",
        ["P"] = "p",
        ["R"] = "ɹ",
        ["S"] = "s",
        ["SH"] = "ʃ",
        ["T"] = "t",
        ["TH"] = "θ",
        ["UH"] = "ʊ",
        ["UW"] = "u",
        ["V"] = "v",
        ["W"] = "w",
        ["Y"] = "j",
        ["Z"] = "z",
        ["ZH"] = "ʒ"
    };

    /// <summary>Unstressed AH ("AH0"): schwa. Every other AH: wedge.</summary>
    public const string UnstressedAh = "ə";
    public const string StressedAh = "ʌ";
}
