using System.Text;
using System.Text.Json;

namespace Hermaeus.Voice;

/// <summary>
/// Whisper's tokenizer, decode side only (r25 doc 03 3.3).
///
/// Greedy decoding starts from a fixed prompt of special token ids and never
/// needs to turn text into tokens, so this does not implement byte-pair
/// encoding at all: no merge table, no ranking, no tokenizer library. What is
/// left is an id-to-string table plus GPT-2's byte-level alphabet, which maps
/// printable stand-in characters back to the raw bytes they represent. Those
/// bytes are then decoded as UTF-8, which is what makes multi-byte characters
/// (accents, CJK, emoji) come out whole even though a single token can carry
/// a fragment of one.
/// </summary>
internal sealed class WhisperVocabulary
{
    private readonly Dictionary<int, string> _tokens;
    private readonly Dictionary<char, byte> _byteDecoder;

    /// <summary>Special ids read from the model's own generation config, never assumed.</summary>
    public required int StartOfTranscript { get; init; }
    public required int EndOfText { get; init; }
    public required int NoTimestamps { get; init; }
    public required int Transcribe { get; init; }

    /// <summary>The first timestamp token. Everything at or above it is a timestamp,
    /// which a `notimestamps` decode should never emit and must never render as text.</summary>
    public required int TimestampBase { get; init; }

    /// <summary>Language token id per two-letter code, e.g. "en" to 50259.</summary>
    public required IReadOnlyDictionary<string, int> LanguageTokens { get; init; }

    /// <summary>Ids the model must never emit (non-speech markers and the like).</summary>
    public required IReadOnlySet<int> SuppressedTokens { get; init; }

    /// <summary>Ids suppressed only on the first decode step, so a transcript cannot
    /// open with a bare space or end immediately.</summary>
    public required IReadOnlySet<int> BeginSuppressedTokens { get; init; }

    /// <summary>The model's own generation cap; also the hard stop for the decode loop.</summary>
    public required int MaxTokens { get; init; }

    private WhisperVocabulary(Dictionary<int, string> tokens)
    {
        _tokens = tokens;
        _byteDecoder = BuildByteDecoder();
    }

    /// <summary>Reverse of the language token map, for reporting what was detected.</summary>
    public string LanguageOf(int tokenId)
    {
        foreach (var (code, id) in LanguageTokens)
            if (id == tokenId)
                return code;
        return string.Empty;
    }

    public bool IsLanguageToken(int tokenId) => LanguageTokens.Values.Contains(tokenId);

    /// <summary>
    /// Turns generated ids into text. Special and timestamp ids contribute
    /// nothing: they are control tokens, and rendering them would put
    /// "&lt;|notimestamps|&gt;" into the user's chat box.
    /// </summary>
    public string Decode(IEnumerable<int> tokenIds)
    {
        var bytes = new List<byte>();
        foreach (var id in tokenIds)
        {
            if (id >= TimestampBase || id == EndOfText || id == StartOfTranscript || id == NoTimestamps
                || id == Transcribe || IsLanguageToken(id))
                continue;
            if (!_tokens.TryGetValue(id, out var piece))
                continue;

            foreach (var c in piece)
            {
                if (_byteDecoder.TryGetValue(c, out var raw))
                    bytes.Add(raw);
                else
                    bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
            }
        }

        // Invalid sequences become the replacement character rather than throwing:
        // a window boundary can split a multi-byte character, and a slightly
        // mangled character is better than losing the transcript.
        return new UTF8Encoding(false, throwOnInvalidBytes: false).GetString(bytes.ToArray());
    }

    /// <summary>
    /// GPT-2's byte-to-unicode alphabet, inverted. Printable ASCII maps to
    /// itself; everything else is shifted into a private range so that every
    /// byte has a printable stand-in inside the vocabulary file.
    /// </summary>
    internal static Dictionary<char, byte> BuildByteDecoder()
    {
        var direct = new List<int>();
        for (var b = '!'; b <= '~'; b++) direct.Add(b);
        for (var b = 0xA1; b <= 0xAC; b++) direct.Add(b);
        for (var b = 0xAE; b <= 0xFF; b++) direct.Add(b);

        var map = new Dictionary<char, byte>();
        foreach (var b in direct)
            map[(char)b] = (byte)b;

        var next = 0;
        for (var b = 0; b < 256; b++)
        {
            if (direct.Contains(b))
                continue;
            map[(char)(256 + next)] = (byte)b;
            next++;
        }

        return map;
    }

    /// <summary>
    /// Loads from the installed asset files. Every id and every suppression list
    /// comes from the model's own <c>generation_config.json</c> and
    /// <c>added_tokens.json</c>, so a different Whisper export stays correct
    /// without touching this code.
    /// </summary>
    public static WhisperVocabulary Load(string vocabJson, string addedTokensJson, string generationConfigJson)
    {
        var tokens = new Dictionary<int, string>();
        using (var vocab = JsonDocument.Parse(vocabJson))
            foreach (var entry in vocab.RootElement.EnumerateObject())
                tokens[entry.Value.GetInt32()] = entry.Name;

        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var startOfTranscript = 0;
        var noTimestamps = 0;
        var transcribe = 0;
        var timestampBase = int.MaxValue;

        using (var added = JsonDocument.Parse(addedTokensJson))
            foreach (var entry in added.RootElement.EnumerateObject())
            {
                var id = entry.Value.GetInt32();
                tokens[id] = entry.Name;

                switch (entry.Name)
                {
                    case "<|startoftranscript|>": startOfTranscript = id; continue;
                    case "<|notimestamps|>": noTimestamps = id; continue;
                    case "<|transcribe|>": transcribe = id; continue;
                }

                // "<|0.00|>" and friends: the lowest such id is where timestamps begin.
                if (entry.Name.StartsWith("<|", StringComparison.Ordinal)
                    && entry.Name.EndsWith("|>", StringComparison.Ordinal))
                {
                    var inner = entry.Name[2..^2];
                    if (inner.Length == 2 && inner.All(char.IsAsciiLetterLower))
                        languages[inner] = id;
                    else if (double.TryParse(inner, System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out _))
                        timestampBase = Math.Min(timestampBase, id);
                }
            }

        var suppress = new HashSet<int>();
        var beginSuppress = new HashSet<int>();
        var endOfText = 0;
        var maxTokens = 448;

        using (var generation = JsonDocument.Parse(generationConfigJson))
        {
            var root = generation.RootElement;
            if (root.TryGetProperty("eos_token_id", out var eos))
                endOfText = eos.GetInt32();
            if (root.TryGetProperty("max_length", out var max))
                maxTokens = max.GetInt32();
            if (root.TryGetProperty("suppress_tokens", out var list))
                foreach (var id in list.EnumerateArray())
                    suppress.Add(id.GetInt32());
            if (root.TryGetProperty("begin_suppress_tokens", out var beginList))
                foreach (var id in beginList.EnumerateArray())
                    beginSuppress.Add(id.GetInt32());
            if (root.TryGetProperty("decoder_start_token_id", out var start) && startOfTranscript == 0)
                startOfTranscript = start.GetInt32();
            if (root.TryGetProperty("no_timestamps_token_id", out var nts) && noTimestamps == 0)
                noTimestamps = nts.GetInt32();
        }

        return new WhisperVocabulary(tokens)
        {
            StartOfTranscript = startOfTranscript,
            EndOfText = endOfText,
            NoTimestamps = noTimestamps,
            Transcribe = transcribe,
            TimestampBase = timestampBase == int.MaxValue ? int.MaxValue : timestampBase,
            LanguageTokens = languages,
            SuppressedTokens = suppress,
            BeginSuppressedTokens = beginSuppress,
            MaxTokens = maxTokens
        };
    }
}
