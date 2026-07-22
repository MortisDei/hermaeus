using System.Globalization;
using System.Text.RegularExpressions;

namespace Hermaeus.Voice;

/// <summary>
/// Expands digits, currency, ordinals, percentages, clock times and a few
/// common symbols into plain English words before phonemization. Kokoro's
/// phoneme vocabulary (<see cref="KokoroVocab"/>) contains no digit or
/// symbol tokens, so anything left unexpanded here is silently dropped by
/// <see cref="KokoroTokenizer.Encode"/>.
/// </summary>
internal static partial class KokoroTextNormalizer
{
    private static readonly string[] Ones =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
        "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen",
        "seventeen", "eighteen", "nineteen"
    ];

    private static readonly string[] Tens =
    [
        "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"
    ];

    private static readonly string[] OrdinalOnes =
    [
        "zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh",
        "eighth", "ninth", "tenth", "eleventh", "twelfth", "thirteenth", "fourteenth",
        "fifteenth", "sixteenth", "seventeenth", "eighteenth", "nineteenth"
    ];

    private static readonly string[] OrdinalTens =
    [
        "", "", "twentieth", "thirtieth", "fortieth", "fiftieth", "sixtieth",
        "seventieth", "eightieth", "ninetieth"
    ];

    [GeneratedRegex(@"\$(\d{1,3}(?:,\d{3})*|\d+)(?:\.(\d{2}))?")]
    private static partial Regex CurrencyPattern();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s?%")]
    private static partial Regex PercentPattern();

    [GeneratedRegex(@"\b([01]?\d|2[0-3]):([0-5]\d)\b")]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"\b(\d+)(st|nd|rd|th)\b")]
    private static partial Regex OrdinalPattern();

    [GeneratedRegex(@"(?<!\d)-(\d+(?:\.\d+)?)")]
    private static partial Regex NegativePattern();

    [GeneratedRegex(@"\b(\d+)\.(\d+)\b")]
    private static partial Regex DecimalPattern();

    [GeneratedRegex(@"\b\d[\d,]*\b")]
    private static partial Regex CardinalPattern();

    [GeneratedRegex(@"(?<=^|\s)([&+/@=])(?=\s|$)")]
    private static partial Regex SymbolPattern();

    private static readonly IReadOnlyDictionary<char, string> Symbols = new Dictionary<char, string>
    {
        ['&'] = "and",
        ['+'] = "plus",
        ['/'] = "slash",
        ['@'] = "at",
        ['='] = "equals"
    };

    // ── Typographic punctuation (r10 03-field-follow-ups.md 3.3) ────────────
    // LLM chat output uses U+2014/U+2013 dashes, curly quotes, ellipsis and
    // markdown emphasis constantly; none of these are in the phonemizer's
    // dictionary lookup or letter-fallback tables, so left as-is they cause
    // dictionary misses (fused words) and dropped characters.

    [GeneratedRegex("[\\u2014\\u2013]")]
    private static partial Regex EmDashPattern();

    [GeneratedRegex(@"(?<=[\w\s])--(?=[\w\s])")]
    private static partial Regex DoubleHyphenPattern();

    [GeneratedRegex("[\\u2018\\u2019]")]
    private static partial Regex CurlySingleQuotePattern();

    [GeneratedRegex("[\\u201C\\u201D]")]
    private static partial Regex CurlyDoubleQuotePattern();

    [GeneratedRegex(@"(?<!\w)[*`_]|[*`_](?!\w)")]
    private static partial Regex MarkdownEmphasisPattern();

    /// <summary>
    /// Expands numbers, currency, percentages, ordinals and clock times into
    /// plain English words, and spells out a handful of standalone symbols.
    /// All-caps acronym spelling (e.g. "GGUF" -> "g g u f") is deliberately
    /// NOT done here: at the text level a spelled-out single letter is
    /// indistinguishable from a genuine one-letter word ("a", "I"), so that
    /// expansion happens per-word in <see cref="KokoroPhonemizer"/> instead,
    /// where the original token is still available to check unambiguously.
    /// </summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var s = text;
        s = EmDashPattern().Replace(s, ", ");
        s = DoubleHyphenPattern().Replace(s, ", ");
        s = CurlySingleQuotePattern().Replace(s, "'");
        s = CurlyDoubleQuotePattern().Replace(s, "\"");
        s = s.Replace("\u2026", "...", StringComparison.Ordinal);
        s = MarkdownEmphasisPattern().Replace(s, "");
        s = CurrencyPattern().Replace(s, m => ExpandCurrency(m));
        s = PercentPattern().Replace(s, m => ExpandPercent(m));
        s = TimePattern().Replace(s, m => ExpandTime(m));
        s = OrdinalPattern().Replace(s, m => ExpandOrdinal(m));
        s = NegativePattern().Replace(s, m => "minus " + ExpandNumberText(m.Groups[1].Value));
        s = DecimalPattern().Replace(s, m => ExpandDecimal(m));
        s = CardinalPattern().Replace(s, m => ExpandNumberText(m.Value));
        s = SymbolPattern().Replace(s, m => Symbols[m.Value[0]]);
        return s;
    }

    private static string ExpandCurrency(Match m)
    {
        var dollarsText = m.Groups[1].Value.Replace(",", "");
        var dollars = long.Parse(dollarsText, CultureInfo.InvariantCulture);
        var result = CardinalToWords(dollars) + " dollar" + (dollars == 1 ? "" : "s");

        if (m.Groups[2].Success)
        {
            var cents = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (cents > 0)
                result += " " + CardinalToWords(cents) + " cent" + (cents == 1 ? "" : "s");
        }

        return result;
    }

    private static string ExpandPercent(Match m)
    {
        var numberText = m.Groups[1].Value;
        var words = numberText.Contains('.') ? ExpandDecimalText(numberText) : ExpandNumberText(numberText);
        return words + " percent";
    }

    private static string ExpandTime(Match m)
    {
        var hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var minute = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var hourWords = CardinalToWords(hour);

        if (minute == 0)
            return hourWords + " o'clock";
        if (minute < 10)
            return hourWords + " oh " + Ones[minute];
        return hourWords + " " + CardinalToWords(minute);
    }

    private static string ExpandOrdinal(Match m)
    {
        var n = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return Ordinal(n);
    }

    private static string ExpandDecimal(Match m)
    {
        var intPart = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var fraction = m.Groups[2].Value;
        return CardinalToWords(intPart) + " point " + string.Join(' ', fraction.Select(c => Ones[c - '0']));
    }

    private static string ExpandDecimalText(string numberText)
    {
        var parts = numberText.Split('.', 2);
        var intPart = long.Parse(parts[0], CultureInfo.InvariantCulture);
        return CardinalToWords(intPart) + " point " + string.Join(' ', parts[1].Select(c => Ones[c - '0']));
    }

    private static string ExpandNumberText(string numberText)
    {
        var digitsOnly = numberText.Replace(",", "");
        if (digitsOnly.Length > 12 || !long.TryParse(digitsOnly, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            return string.Join(' ', digitsOnly.Select(c => Ones[c - '0']));

        return CardinalToWords(n);
    }

    internal static string CardinalToWords(long n)
    {
        if (n == 0) return "zero";
        if (n < 0) return "minus " + CardinalToWords(-n);

        if (n >= 1_000_000_000)
            return Combine(CardinalToWords(n / 1_000_000_000), "billion", n % 1_000_000_000);
        if (n >= 1_000_000)
            return Combine(CardinalToWords(n / 1_000_000), "million", n % 1_000_000);
        if (n >= 1_000)
            return Combine(CardinalToWords(n / 1_000), "thousand", n % 1_000);
        if (n >= 100)
        {
            var rest = n % 100;
            var s = Ones[n / 100] + " hundred";
            return rest == 0 ? s : s + " " + CardinalToWords(rest);
        }
        if (n >= 20)
        {
            var ones = n % 10;
            return ones == 0 ? Tens[n / 10] : Tens[n / 10] + " " + Ones[ones];
        }
        return Ones[n];
    }

    private static string Combine(string groupWords, string scaleWord, long rest)
    {
        var s = groupWords + " " + scaleWord;
        return rest == 0 ? s : s + " " + CardinalToWords(rest);
    }

    internal static string Ordinal(long n)
    {
        if (n < 0) return "minus " + Ordinal(-n);
        if (n < 20) return OrdinalOnes[n];
        if (n < 100)
        {
            var ones = n % 10;
            return ones == 0 ? OrdinalTens[n / 10] : Tens[n / 10] + " " + OrdinalOnes[ones];
        }

        var remainder = n % 100;
        if (remainder == 0)
            return OrdinalizeRoundWord(CardinalToWords(n));

        return CardinalToWords(n - remainder) + " " + Ordinal(remainder);
    }

    private static string OrdinalizeRoundWord(string cardinalWords)
    {
        if (cardinalWords.EndsWith("hundred", StringComparison.Ordinal))
            return cardinalWords + "th";
        if (cardinalWords.EndsWith("thousand", StringComparison.Ordinal))
            return cardinalWords + "th";
        if (cardinalWords.EndsWith("million", StringComparison.Ordinal))
            return cardinalWords + "th";
        if (cardinalWords.EndsWith("billion", StringComparison.Ordinal))
            return cardinalWords + "th";
        return cardinalWords + "th";
    }
}
