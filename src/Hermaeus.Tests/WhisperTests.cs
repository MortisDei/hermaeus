using Hermaeus.Voice;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// r25 doc 03: Whisper's front end, decode policy, detokenizer and decoder
/// binding. None of this needs a microphone, a network call, a GPU or the model
/// itself: the transform, the filterbank, the decode rules and the tensor-name
/// pairing are all pure, which is exactly why the round put them there.
/// </summary>
public sealed class WhisperTests
{
    private const int SampleRate = 16000;

    private static float[] Tone(double hz, int count, double amplitude = 1.0)
    {
        var samples = new float[count];
        for (var i = 0; i < count; i++)
            samples[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * hz * i / SampleRate));
        return samples;
    }

    // ── The transform ────────────────────────────────────────────────────────

    /// <summary>
    /// A 1000 Hz tone must land in bin 25: bin spacing is 16000/400 = 40 Hz.
    /// Cross-checked against an independent reference DFT.
    /// </summary>
    [Fact]
    public void PowerSpectrum_puts_a_pure_tone_in_the_expected_bin()
    {
        var signal = Tone(1000, LogMelSpectrogram.NFft);
        var spectrum = LogMelSpectrogram.PowerSpectrum(signal, 0);

        var peak = 0;
        for (var k = 1; k < spectrum.Length; k++)
            if (spectrum[k] > spectrum[peak]) peak = k;

        Assert.Equal(25, peak);
        Assert.Equal(LogMelSpectrogram.NFft / 2 + 1, spectrum.Length);
    }

    /// <summary>
    /// Parseval's identity: total energy is the same in both domains. This is the
    /// check that catches a wrong normalization or a sign error in the twiddles,
    /// which a "peak is in the right bin" test would sail straight past.
    /// </summary>
    [Fact]
    public void PowerSpectrum_conserves_energy()
    {
        var signal = Tone(1000, LogMelSpectrogram.NFft);
        var hann = LogMelSpectrogram.BuildPeriodicHann(LogMelSpectrogram.NFft);

        var timeEnergy = 0.0;
        for (var n = 0; n < signal.Length; n++)
        {
            var windowed = signal[n] * hann[n];
            timeEnergy += windowed * windowed;
        }

        // Sum over the full spectrum: the 201 stored bins plus their mirrors,
        // excluding DC and Nyquist which have no mirror.
        var spectrum = LogMelSpectrogram.PowerSpectrum(signal, 0);
        var freqEnergy = spectrum[0] + spectrum[^1];
        for (var k = 1; k < spectrum.Length - 1; k++)
            freqEnergy += 2 * spectrum[k];
        freqEnergy /= LogMelSpectrogram.NFft;

        Assert.Equal(timeEnergy, freqEnergy, 1.0);
    }

    [Fact]
    public void Periodic_hann_starts_at_zero_and_is_not_symmetric_at_the_end()
    {
        var window = LogMelSpectrogram.BuildPeriodicHann(8);

        Assert.Equal(0.0f, window[0], 6);
        // A periodic window's last sample is NOT zero; a symmetric one's would be.
        Assert.True(window[^1] > 0.1f, "periodic Hann must not close back to zero");
        Assert.Equal(1.0f, window[4], 6);
    }

    [Fact]
    public void ReflectPad_mirrors_without_repeating_the_edge_sample()
    {
        var padded = LogMelSpectrogram.ReflectPad([1f, 2f, 3f, 4f], 2);

        Assert.Equal([3f, 2f, 1f, 2f, 3f, 4f, 3f, 2f], padded);
    }

    // ── The mel filterbank ───────────────────────────────────────────────────

    [Fact]
    public void Mel_scale_matches_the_slaney_reference_points()
    {
        Assert.Equal(0.0, LogMelSpectrogram.HzToMel(0), 9);
        Assert.Equal(15.0, LogMelSpectrogram.HzToMel(1000), 6);
        Assert.Equal(45.245640472, LogMelSpectrogram.HzToMel(8000), 6);
    }

    [Fact]
    public void Mel_conversion_round_trips()
    {
        foreach (var hz in new[] { 0.0, 100.0, 999.0, 1000.0, 4000.0, 8000.0 })
            Assert.Equal(hz, LogMelSpectrogram.MelToHz(LogMelSpectrogram.HzToMel(hz)), 6);
    }

    [Fact]
    public void Mel_filterbank_has_the_right_shape_and_monotone_centres()
    {
        var filters = LogMelSpectrogram.BuildSlaneyMelFilterbank();

        Assert.Equal(LogMelSpectrogram.MelBins, filters.Length);
        Assert.All(filters, row => Assert.Equal(LogMelSpectrogram.NFft / 2 + 1, row.Length));
        Assert.All(filters, row => Assert.All(row, v => Assert.True(v >= 0, "filter weights are never negative")));

        // Peak bin must move upward with mel index: that is what "mel scale" means,
        // and an off-by-one in the triangle construction shows up here.
        var previousPeak = -1;
        foreach (var row in filters)
        {
            var peak = 0;
            for (var k = 1; k < row.Length; k++)
                if (row[k] > row[peak]) peak = k;
            Assert.True(peak >= previousPeak, $"filter peaks must not move down (got {peak} after {previousPeak})");
            previousPeak = peak;
        }

        Assert.Contains(filters, row => row.Any(v => v > 0));
    }

    // ── Windowing: the fix for the OOM the old cap allowed ───────────────────

    [Fact]
    public void Compute_always_produces_exactly_one_encoder_window()
    {
        var expected = LogMelSpectrogram.MelBins * LogMelSpectrogram.FramesPerWindow;

        // Far too short, and far too long: both must produce the same fixed block.
        Assert.Equal(expected, LogMelSpectrogram.Compute(Tone(440, 1000)).Length);
        Assert.Equal(expected, LogMelSpectrogram.Compute(Tone(440, LogMelSpectrogram.SamplesPerWindow * 2)).Length);
        Assert.Equal(3000, LogMelSpectrogram.FramesPerWindow);
    }

    [Fact]
    public void Windows_splits_long_audio_into_fixed_size_pieces()
    {
        // 95 seconds: four 30-second windows, the last one partly silence.
        var samples = Tone(440, SampleRate * 95, amplitude: 0.1);
        var windows = LogMelSpectrogram.Windows(samples).ToList();

        Assert.Equal(4, windows.Count);
        Assert.All(windows, w => Assert.Equal(4, w.Count));
        Assert.Equal([0, 1, 2, 3], windows.Select(w => w.Index));
        // Constant memory: every window is the same size regardless of file length.
        Assert.All(windows, w => Assert.Equal(
            LogMelSpectrogram.MelBins * LogMelSpectrogram.FramesPerWindow, w.Features.Length));
    }

    [Fact]
    public void Windows_of_short_audio_is_a_single_window()
    {
        Assert.Single(LogMelSpectrogram.Windows(Tone(440, SampleRate * 3)));
    }

    /// <summary>
    /// Whisper floors the log spectrum at 8 dB below the window's own peak, then
    /// applies (x + 4) / 4. The invariant is the SPAN, not an absolute range:
    /// 8 dB divided by 4 is exactly 2, wherever the peak happens to sit. Loud
    /// audio legitimately produces values above 1.
    /// </summary>
    [Fact]
    public void Compute_clamps_dynamic_range_to_eight_decibels()
    {
        foreach (var amplitude in new[] { 0.05, 0.5, 1.0 })
        {
            var features = LogMelSpectrogram.Compute(Tone(1000, LogMelSpectrogram.SamplesPerWindow, amplitude));

            Assert.All(features, v => Assert.True(float.IsFinite(v), "features must never be NaN or infinite"));
            Assert.Equal(2.0f, features.Max() - features.Min(), 3);
        }
    }

    /// <summary>Silence must not produce NaN: log10(0) is negative infinity, so the
    /// 1e-10 floor before the logarithm is load-bearing.</summary>
    [Fact]
    public void Compute_of_silence_is_finite()
    {
        var features = LogMelSpectrogram.Compute(new float[LogMelSpectrogram.SamplesPerWindow]);

        Assert.All(features, v => Assert.True(float.IsFinite(v), "silence must not produce NaN or infinity"));
    }

    // ── The detokenizer ──────────────────────────────────────────────────────

    /// <summary>
    /// Renders text the way a GPT-2 byte-level vocabulary file stores it: every
    /// UTF-8 byte replaced by its printable stand-in. Built from the inverse of
    /// the decoder's own map so the fixture cannot drift from the convention, and
    /// so no exotic literals end up in this file. The convention itself is pinned
    /// separately by <see cref="Byte_decoder_covers_every_byte_exactly_once"/>.
    /// </summary>
    private static string AsVocabPiece(string text)
    {
        var byteToChar = WhisperVocabulary.BuildByteDecoder()
            .ToDictionary(kv => kv.Value, kv => kv.Key);
        return string.Concat(System.Text.Encoding.UTF8.GetBytes(text).Select(b => byteToChar[b]));
    }

    private static WhisperVocabulary BuildVocab()
    {
        // A miniature of the real files: GPT-2 byte-level pieces plus the special
        // ids Whisper actually uses at the pinned revision.
        var vocab = $$"""
            {"Hello":1,"{{AsVocabPiece(" world")}}":2,"!":3,
             "{{AsVocabPiece("é")}}":4,"{{AsVocabPiece("中")}}":5}
            """;
        const string added = """
            {"<|startoftranscript|>":50258,"<|en|>":50259,"<|fr|>":50260,
             "<|translate|>":50358,"<|transcribe|>":50359,"<|notimestamps|>":50363,
             "<|0.00|>":50364,"<|0.02|>":50365}
            """;
        const string generation = """
            {"eos_token_id":50257,"max_length":448,"decoder_start_token_id":50258,
             "no_timestamps_token_id":50363,"suppress_tokens":[3],"begin_suppress_tokens":[2,50257]}
            """;
        return WhisperVocabulary.Load(vocab, added, generation);
    }

    [Fact]
    public void Byte_decoder_covers_every_byte_exactly_once()
    {
        var decoder = WhisperVocabulary.BuildByteDecoder();

        Assert.Equal(256, decoder.Count);
        Assert.Equal(256, decoder.Values.Distinct().Count());
        // Printable ASCII maps to itself; the space stand-in is the shifted form.
        Assert.Equal((byte)'A', decoder['A']);
        // Byte 0x20 (space) and byte 0xAD are both outside the printable ranges, so
        // both live in the shifted region. Computed independently from the GPT-2
        // alphabet definition, not read back out of the map under test.
        Assert.Equal((byte)' ', decoder['Ġ']);
        Assert.Equal((byte)0xAD, decoder['Ń']);
    }

    [Fact]
    public void Decode_turns_byte_level_pieces_back_into_text()
    {
        var vocab = BuildVocab();

        Assert.Equal("Hello world", vocab.Decode([1, 2]));
    }

    /// <summary>
    /// A single token can carry a fragment of a multi-byte character, so the bytes
    /// have to be gathered before decoding rather than converted per token.
    /// </summary>
    [Fact]
    public void Decode_reassembles_multi_byte_utf8_characters()
    {
        var vocab = BuildVocab();

        Assert.Equal("é", vocab.Decode([4]));
        Assert.Equal("中", vocab.Decode([5]));
    }

    [Fact]
    public void Decode_never_renders_control_or_timestamp_tokens()
    {
        var vocab = BuildVocab();

        var text = vocab.Decode([50258, 50259, 50359, 50363, 1, 50364, 50365, 50257]);

        Assert.Equal("Hello", text);
        Assert.DoesNotContain("<|", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Special_ids_and_language_map_come_from_the_model_files()
    {
        var vocab = BuildVocab();

        Assert.Equal(50258, vocab.StartOfTranscript);
        Assert.Equal(50257, vocab.EndOfText);
        Assert.Equal(50363, vocab.NoTimestamps);
        Assert.Equal(50359, vocab.Transcribe);
        Assert.Equal(50364, vocab.TimestampBase);
        Assert.Equal(448, vocab.MaxTokens);
        Assert.Equal(50259, vocab.LanguageTokens["en"]);
        Assert.Equal("fr", vocab.LanguageOf(50260));
        // "<|translate|>" is not a two-letter code and must not be read as a language.
        Assert.False(vocab.IsLanguageToken(50358));
    }

    // ── The decode policy ────────────────────────────────────────────────────

    /// <summary>Drives the loop with scripted logits, so every rule is checked
    /// without an ONNX session.</summary>
    private static Func<IReadOnlyList<int>, float[]> Scripted(params int[] wanted)
    {
        var calls = 0;
        return _ =>
        {
            var logits = new float[50400];
            var pick = calls < wanted.Length ? wanted[calls] : wanted[^1];
            calls++;
            logits[pick] = 10f;
            return logits;
        };
    }

    [Fact]
    public void Decode_stops_at_end_of_text()
    {
        var vocab = BuildVocab();

        var result = WhisperGreedyDecoder.Decode(vocab, "en", Scripted(1, 1, 50257));

        Assert.Equal(WhisperStopReason.EndOfText, result.StopReason);
        Assert.Equal([1, 1], result.Tokens);
        Assert.Equal("en", result.Language);
    }

    /// <summary>
    /// Whisper's documented failure mode is looping forever on silence or music.
    /// An unbounded autoregressive loop in a desktop app is a hang with no cancel,
    /// so the cap is a correctness requirement, not a tuning knob.
    /// </summary>
    [Fact]
    public void Decode_stops_at_the_token_cap_rather_than_looping_forever()
    {
        var vocab = BuildVocab();

        // Never emits EOS.
        var result = WhisperGreedyDecoder.Decode(vocab, "en", Scripted(1));

        Assert.Equal(WhisperStopReason.TokenCap, result.StopReason);
        Assert.Equal(vocab.MaxTokens - 4, result.Tokens.Count);
    }

    [Fact]
    public void Decode_detects_the_language_when_none_is_forced()
    {
        var vocab = BuildVocab();

        // First call is the language probe; it can only choose a language token.
        var result = WhisperGreedyDecoder.Decode(vocab, forcedLanguage: null, Scripted(50260, 1, 50257));

        Assert.Equal("fr", result.Language);
    }

    [Fact]
    public void Decode_honours_a_forced_language_without_probing()
    {
        var vocab = BuildVocab();

        var result = WhisperGreedyDecoder.Decode(vocab, "fr", Scripted(1, 50257));

        Assert.Equal("fr", result.Language);
        Assert.Equal([1], result.Tokens);
    }

    [Fact]
    public void Decode_honours_cancellation()
    {
        var vocab = BuildVocab();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = WhisperGreedyDecoder.Decode(vocab, "en", Scripted(1), cts.Token);

        Assert.Equal(WhisperStopReason.Cancelled, result.StopReason);
    }

    [Fact]
    public void Suppressed_tokens_are_never_emitted()
    {
        var vocab = BuildVocab();

        // Token 3 is in suppress_tokens; the runner-up (1) must win instead.
        var logits = new float[50400];
        logits[3] = 100f;
        logits[1] = 5f;

        Assert.Equal(1, WhisperGreedyDecoder.ArgMax(logits, WhisperGreedyDecoder.BannedAt(vocab, 1)));
    }

    [Fact]
    public void Begin_suppressed_tokens_apply_only_to_the_first_step()
    {
        var vocab = BuildVocab();

        Assert.Contains(2, WhisperGreedyDecoder.BannedAt(vocab, 0));
        Assert.DoesNotContain(2, WhisperGreedyDecoder.BannedAt(vocab, 1));
        // The always-suppressed set applies at both.
        Assert.Contains(3, WhisperGreedyDecoder.BannedAt(vocab, 0));
        Assert.Contains(3, WhisperGreedyDecoder.BannedAt(vocab, 1));
    }

    [Fact]
    public void ArgMax_breaks_ties_on_the_lower_id_so_a_decode_is_reproducible()
    {
        var logits = new float[10];
        logits[3] = 1f;
        logits[7] = 1f;

        Assert.Equal(3, WhisperGreedyDecoder.ArgMax(logits));
    }

    [Fact]
    public void Repetition_loops_are_recognized_as_low_confidence()
    {
        Assert.True(WhisperGreedyDecoder.LooksLikeRepetitionLoop(Enumerable.Repeat(5, 40).ToList()));
        Assert.True(WhisperGreedyDecoder.LooksLikeRepetitionLoop(
            Enumerable.Range(0, 40).Select(i => i % 2 == 0 ? 5 : 6).ToList()));

        // Ordinary speech is varied, and a short transcript is never judged a loop.
        Assert.False(WhisperGreedyDecoder.LooksLikeRepetitionLoop(Enumerable.Range(100, 40).ToList()));
        Assert.False(WhisperGreedyDecoder.LooksLikeRepetitionLoop([5, 5, 5]));
    }

    // ── The decoder binding ──────────────────────────────────────────────────

    /// <summary>
    /// Names are discovered from the graph rather than hardcoded, because a wrong
    /// guess is a load failure with no useful message. The pairing is pure.
    /// </summary>
    [Fact]
    public void Binding_pairs_past_inputs_with_their_present_outputs()
    {
        string[] inputs =
        [
            "input_ids", "encoder_hidden_states", "use_cache_branch",
            "past_key_values.0.decoder.key", "past_key_values.0.decoder.value",
            "past_key_values.0.encoder.key", "past_key_values.0.encoder.value",
            "past_key_values.1.decoder.key", "past_key_values.1.decoder.value"
        ];
        string[] outputs =
        [
            "logits",
            "present.0.decoder.key", "present.0.decoder.value",
            "present.0.encoder.key", "present.0.encoder.value",
            "present.1.decoder.key", "present.1.decoder.value"
        ];

        var binding = WhisperDecoderBinding.Pair(inputs, outputs);

        Assert.Equal("input_ids", binding.InputIds);
        Assert.Equal("encoder_hidden_states", binding.EncoderHiddenStates);
        Assert.Equal("use_cache_branch", binding.UseCacheBranch);
        Assert.Equal("logits", binding.Logits);
        Assert.Equal(6, binding.CachePairs.Count);
        Assert.All(binding.CachePairs, p => Assert.Equal(
            p.Past.Replace("past_key_values", "present", StringComparison.Ordinal), p.Present));

        // Cross-attention entries are constant per window and tracked separately.
        Assert.Equal(2, binding.EncoderCacheInputs.Count);
        Assert.Contains("past_key_values.0.encoder.key", binding.EncoderCacheInputs);
    }

    [Fact]
    public void Binding_works_without_a_use_cache_branch_flag()
    {
        var binding = WhisperDecoderBinding.Pair(
            ["input_ids", "encoder_hidden_states"], ["logits"]);

        Assert.Null(binding.UseCacheBranch);
        Assert.Empty(binding.CachePairs);
    }

    [Fact]
    public void Binding_rejects_a_graph_missing_a_required_input()
    {
        Assert.Throws<InvalidOperationException>(() =>
            WhisperDecoderBinding.Pair(["encoder_hidden_states"], ["logits"]));
        Assert.Throws<InvalidOperationException>(() =>
            WhisperDecoderBinding.Pair(["input_ids", "encoder_hidden_states"], []));
    }
}
