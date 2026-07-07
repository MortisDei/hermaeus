namespace Aether.Voice;

/// <summary>
/// Converts a phoneme string (as produced by <see cref="KokoroPhonemizer"/>)
/// into the token-id sequence Kokoro's ONNX graph expects: pad token (0) at
/// both ends, one id per phoneme/punctuation symbol in between. Long input is
/// split into chunks that each fit Kokoro's context window.
/// </summary>
internal static class KokoroTokenizer
{
    private static readonly IReadOnlyDictionary<char, int> CharToId = BuildCharMap();

    /// <summary>
    /// Splits phonemized text into one or more token-id sequences, each
    /// already wrapped with the leading/trailing pad token, each no longer
    /// than <see cref="KokoroVocab.MaxSequenceTokens"/> phoneme tokens.
    /// </summary>
    public static List<int[]> Encode(string phonemes)
    {
        var chunks = new List<int[]>();
        if (string.IsNullOrEmpty(phonemes))
            return chunks;

        var ids = new List<int>(phonemes.Length);
        foreach (var c in phonemes)
        {
            if (CharToId.TryGetValue(c, out var id))
                ids.Add(id);
        }

        for (var offset = 0; offset < ids.Count; offset += KokoroVocab.MaxSequenceTokens)
        {
            var take = Math.Min(KokoroVocab.MaxSequenceTokens, ids.Count - offset);
            var chunk = new int[take + 2];
            chunk[0] = KokoroVocab.PadTokenId;
            for (var i = 0; i < take; i++)
                chunk[i + 1] = ids[offset + i];
            chunk[^1] = KokoroVocab.PadTokenId;
            chunks.Add(chunk);
        }

        return chunks;
    }

    private static IReadOnlyDictionary<char, int> BuildCharMap()
    {
        var map = new Dictionary<char, int>();
        foreach (var (symbol, id) in KokoroVocab.SymbolToId)
        {
            if (symbol.Length == 1)
                map[symbol[0]] = id;
        }

        return map;
    }
}
