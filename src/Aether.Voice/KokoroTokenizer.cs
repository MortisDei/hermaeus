using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Voice;

/// <summary>
/// How a phoneme chunk's END was determined (r19 4.1). Drives the length of
/// silence <see cref="NativeKokoroVoiceProvider"/> inserts before the next
/// chunk: a real sentence/clause/word boundary reads as a natural pause; a
/// forced cut mid-run (no boundary found in the whole window - pathological
/// input) gets none, since padding a mid-word split with silence would make
/// the clipping worse, not better.
/// </summary>
internal enum PhonemeChunkBoundary
{
    SentenceBreak,
    ClauseOrSpace,
    HardCut
}

/// <summary>One Kokoro input sequence: pad-wrapped token ids plus how its end was chosen.</summary>
internal readonly record struct PhonemeChunk(int[] Ids, PhonemeChunkBoundary Boundary);

/// <summary>
/// Converts a phoneme string (as produced by <see cref="KokoroPhonemizer"/>)
/// into the token-id sequence Kokoro's ONNX graph expects: pad token (0) at
/// both ends, one id per phoneme/punctuation symbol in between. Long input is
/// split into chunks that each fit Kokoro's context window.
/// </summary>
internal static class KokoroTokenizer
{
    private static readonly IReadOnlyDictionary<char, int> CharToId = BuildCharMap();

    // r19 4.1: a hard offset split (the old behaviour) can land mid-word,
    // producing an audible clipped/garbled word at the seam. Preferring the
    // last sentence punctuation, then clause punctuation, then a plain word
    // space within the window keeps every split at a natural boundary; only
    // a single unbroken run longer than the whole window falls back to the
    // hard cut.
    private static readonly HashSet<int> SentenceBreakIds = BuildIdSet(".", "!", "?");
    private static readonly HashSet<int> ClauseBreakIds = BuildIdSet(",", ";", ":");
    private static readonly HashSet<int> SpaceIds = BuildIdSet(KokoroVocab.Space);

    /// <summary>
    /// Splits phonemized text into one or more token-id sequences, each
    /// already wrapped with the leading/trailing pad token, each no longer
    /// than <see cref="KokoroVocab.MaxSequenceTokens"/> phoneme tokens.
    /// </summary>
    public static List<PhonemeChunk> Encode(string phonemes, IRuntimeLogService? logs = null)
    {
        var chunks = new List<PhonemeChunk>();
        if (string.IsNullOrEmpty(phonemes))
            return chunks;

        var ids = new List<int>(phonemes.Length);
        var dropped = 0;
        foreach (var c in phonemes)
        {
            if (CharToId.TryGetValue(c, out var id))
                ids.Add(id);
            else
                dropped++;
        }

        // r19 4.2: a character that reaches here but has no vocab id is
        // silently missing from the spoken output - previously only
        // discoverable by guessing. Logged once per utterance rather than
        // per character so a bad run doesn't flood the log.
        if (dropped > 0 && logs is not null)
        {
            logs.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Debug,
                RuntimeLogCategory.Voice,
                $"Phoneme tokenizer dropped {dropped} unmapped character(s) this utterance"));
        }

        var offset = 0;
        while (offset < ids.Count)
        {
            var windowEnd = Math.Min(offset + KokoroVocab.MaxSequenceTokens, ids.Count);
            int take;
            PhonemeChunkBoundary boundary;

            // A window that does not fill the whole context reaches the end
            // of the input outright; nothing to break early for.
            if (windowEnd - offset < KokoroVocab.MaxSequenceTokens)
            {
                take = windowEnd - offset;
                boundary = PhonemeChunkBoundary.HardCut;
            }
            else
            {
                var sentenceBreak = FindLastBreak(ids, offset, windowEnd, SentenceBreakIds);
                var clauseBreak = sentenceBreak >= 0 ? -1 : FindLastBreak(ids, offset, windowEnd, ClauseBreakIds);
                var spaceBreak = sentenceBreak >= 0 || clauseBreak >= 0 ? -1 : FindLastBreak(ids, offset, windowEnd, SpaceIds);

                if (sentenceBreak >= 0)
                {
                    take = sentenceBreak - offset + 1;
                    boundary = PhonemeChunkBoundary.SentenceBreak;
                }
                else if (clauseBreak >= 0)
                {
                    take = clauseBreak - offset + 1;
                    boundary = PhonemeChunkBoundary.ClauseOrSpace;
                }
                else if (spaceBreak >= 0)
                {
                    take = spaceBreak - offset + 1;
                    boundary = PhonemeChunkBoundary.ClauseOrSpace;
                }
                else
                {
                    take = windowEnd - offset;
                    boundary = PhonemeChunkBoundary.HardCut;
                }
            }

            var chunkIds = new int[take + 2];
            chunkIds[0] = KokoroVocab.PadTokenId;
            for (var i = 0; i < take; i++)
                chunkIds[i + 1] = ids[offset + i];
            chunkIds[^1] = KokoroVocab.PadTokenId;
            chunks.Add(new PhonemeChunk(chunkIds, boundary));

            offset += take;
        }

        return chunks;
    }

    /// <summary>Last index in [windowStart, windowEnd) whose id is in <paramref name="candidateIds"/>,
    /// excluding windowStart itself (a "break" at the very first character would produce an empty chunk).</summary>
    private static int FindLastBreak(List<int> ids, int windowStart, int windowEnd, HashSet<int> candidateIds)
    {
        for (var i = windowEnd - 1; i > windowStart; i--)
        {
            if (candidateIds.Contains(ids[i]))
                return i;
        }
        return -1;
    }

    private static HashSet<int> BuildIdSet(params string[] symbols) =>
        symbols.Select(s => KokoroVocab.SymbolToId[s]).ToHashSet();

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
