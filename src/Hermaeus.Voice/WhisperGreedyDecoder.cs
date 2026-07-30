namespace Hermaeus.Voice;

/// <summary>Why a decode stopped. Reported so a caller can tell a finished sentence
/// from a truncated one rather than guessing from the text.</summary>
internal enum WhisperStopReason
{
    EndOfText,
    TokenCap,
    Cancelled
}

internal sealed record WhisperDecodeResult(
    IReadOnlyList<int> Tokens,
    WhisperStopReason StopReason,
    string Language);

/// <summary>
/// Whisper's greedy decode policy (r25 doc 03 3.3), with the model call behind
/// a delegate so every rule here is testable without an ONNX session.
///
/// The rules are not decoration. Whisper's documented failure mode is looping
/// forever on silence or music, and an unbounded autoregressive loop inside a
/// desktop app is a hang with no cancel button, so the token cap is a
/// correctness requirement rather than a tuning knob.
/// </summary>
internal static class WhisperGreedyDecoder
{
    /// <summary>
    /// Runs the loop. <paramref name="step"/> is given the tokens generated so
    /// far and returns the next-token logits.
    /// </summary>
    public static WhisperDecodeResult Decode(
        WhisperVocabulary vocab,
        string? forcedLanguage,
        Func<IReadOnlyList<int>, float[]> step,
        CancellationToken ct = default)
    {
        // The forced prompt. Language is either pinned by the user or left for the
        // model to pick, in which case its first prediction IS the language token.
        var prompt = new List<int> { vocab.StartOfTranscript };
        var language = string.Empty;

        if (!string.IsNullOrWhiteSpace(forcedLanguage)
            && vocab.LanguageTokens.TryGetValue(forcedLanguage.Trim(), out var forcedId))
        {
            prompt.Add(forcedId);
            language = forcedLanguage.Trim().ToLowerInvariant();
        }
        else
        {
            var logits = step(prompt);
            var detected = ArgMax(logits, allow: vocab.LanguageTokens.Values.ToHashSet());
            prompt.Add(detected);
            language = vocab.LanguageOf(detected);
        }

        prompt.Add(vocab.Transcribe);
        prompt.Add(vocab.NoTimestamps);

        var generated = new List<int>();
        var cap = Math.Max(1, vocab.MaxTokens - prompt.Count);

        for (var i = 0; i < cap; i++)
        {
            if (ct.IsCancellationRequested)
                return new WhisperDecodeResult(generated, WhisperStopReason.Cancelled, language);

            var logits = step([.. prompt, .. generated]);
            var next = ArgMax(logits, banned: BannedAt(vocab, i));

            if (next == vocab.EndOfText)
                return new WhisperDecodeResult(generated, WhisperStopReason.EndOfText, language);

            generated.Add(next);
        }

        return new WhisperDecodeResult(generated, WhisperStopReason.TokenCap, language);
    }

    /// <summary>
    /// Tokens the model may not emit at step <paramref name="index"/>. The
    /// begin-suppress set applies only to the first generated token, which is
    /// what stops a transcript opening with a bare space or ending instantly.
    /// </summary>
    internal static IReadOnlySet<int> BannedAt(WhisperVocabulary vocab, int index)
    {
        if (index != 0)
            return vocab.SuppressedTokens;

        var banned = new HashSet<int>(vocab.SuppressedTokens);
        banned.UnionWith(vocab.BeginSuppressedTokens);
        return banned;
    }

    /// <summary>
    /// Highest-scoring id, honouring an allowlist or a banned set. Ties break on
    /// the lower id so a decode is reproducible for the same audio.
    /// </summary>
    internal static int ArgMax(float[] logits, IReadOnlySet<int>? banned = null, IReadOnlySet<int>? allow = null)
    {
        var best = -1;
        var bestScore = float.NegativeInfinity;

        for (var i = 0; i < logits.Length; i++)
        {
            if (allow is not null && !allow.Contains(i))
                continue;
            if (banned is not null && banned.Contains(i))
                continue;
            if (logits[i] > bestScore)
            {
                bestScore = logits[i];
                best = i;
            }
        }

        // Everything masked out (a degenerate logit vector) still has to return
        // something rather than throwing inside a transcription.
        return best < 0 ? 0 : best;
    }

    /// <summary>
    /// Whisper's own hallucination signal: a transcript whose gzip-style
    /// repetition ratio is extreme is the model looping, not speech. Used to set
    /// <c>IsLowConfidence</c>, which is what lets r24's hands-free mode refuse
    /// to auto-send a hallucinated turn. Before r25 that flag only meant "the
    /// text came back empty".
    /// </summary>
    internal static bool LooksLikeRepetitionLoop(IReadOnlyList<int> tokens, int minLength = 24)
    {
        if (tokens.Count < minLength)
            return false;

        var distinct = tokens.Distinct().Count();
        if (distinct <= 2)
            return true;

        // A short cycle repeated to fill the budget: check whether the tail is a
        // handful of tokens going round.
        var tail = tokens.Skip(Math.Max(0, tokens.Count - minLength)).ToList();
        return tail.Distinct().Count() * 4 <= tail.Count;
    }
}
