namespace Hermaeus.Voice;

/// <summary>
/// Whisper's audio front end (r25 doc 03 3.2): 16 kHz mono PCM in, an
/// 80 x 3000 log-Mel spectrogram out, which is the only thing Whisper's
/// encoder accepts.
///
/// Every parameter here is fixed by the model's own
/// <c>preprocessor_config.json</c> at the pinned revision (feature_size 80,
/// n_fft 400, hop_length 160, chunk_length 30, n_samples 480000,
/// nb_max_frames 3000, sampling_rate 16000), not chosen here.
///
/// Pure: <c>float[]</c> in, <c>float[]</c> out, no session and no IO, so it
/// gets the same treatment the CTC helpers it replaces did.
///
/// **On the transform.** The pack proposed a radix-2 FFT; that was wrong, and
/// the mistake is worth recording. n_fft is 400 = 2^4 * 5^2, so it is not a
/// power of two and radix-2 does not apply. Zero-padding to 512 is not an
/// option either: it computes a 512-point DFT whose bins sit at different
/// frequencies than the 201 bins the mel filterbank is defined over. The
/// choices were a mixed-radix (2 and 5) implementation or a direct DFT over
/// only the 201 bins actually needed. This takes the direct DFT with a
/// precomputed twiddle table: 241M multiply-adds per 30-second window, which
/// is a fraction of what the encoder that consumes it costs, in exchange for
/// code that is obviously correct and fully testable. If mel ever shows up in
/// a profile, a mixed-radix transform is the optimization, not a rewrite.
/// </summary>
internal static class LogMelSpectrogram
{
    public const int SampleRate = 16000;
    public const int NFft = 400;
    public const int HopLength = 160;
    public const int MelBins = 80;

    /// <summary>30 seconds at 16 kHz: the fixed window Whisper's encoder expects.</summary>
    public const int SamplesPerWindow = SampleRate * 30;

    /// <summary>SamplesPerWindow / HopLength. The encoder's input is exactly this wide.</summary>
    public const int FramesPerWindow = SamplesPerWindow / HopLength;

    private const int SpectrumBins = NFft / 2 + 1;

    // Built once and reused across every frame and every window.
    private static readonly float[] _hann = BuildPeriodicHann(NFft);
    private static readonly float[] _twiddleCos = BuildTwiddle(cosine: true);
    private static readonly float[] _twiddleSin = BuildTwiddle(cosine: false);
    private static readonly float[][] _melFilters = BuildSlaneyMelFilterbank();

    /// <summary>
    /// Computes the 80 x <see cref="FramesPerWindow"/> feature block for one
    /// 30-second window, padding with silence or trimming as needed, and
    /// returns it flattened in [mel, frame] order, which is what the encoder's
    /// <c>input_features</c> tensor wants.
    /// </summary>
    public static float[] Compute(ReadOnlySpan<float> samples)
    {
        var padded = new float[SamplesPerWindow];
        var copy = Math.Min(samples.Length, SamplesPerWindow);
        samples[..copy].CopyTo(padded);

        // Whisper reflect-pads by n_fft/2 so frame centres line up with sample
        // indices (librosa center=True), then drops the final frame.
        var reflected = ReflectPad(padded, NFft / 2);

        var power = new float[FramesPerWindow][];
        for (var frame = 0; frame < FramesPerWindow; frame++)
            power[frame] = PowerSpectrum(reflected, frame * HopLength);

        var result = new float[MelBins * FramesPerWindow];
        var max = float.NegativeInfinity;

        for (var mel = 0; mel < MelBins; mel++)
        {
            var filter = _melFilters[mel];
            for (var frame = 0; frame < FramesPerWindow; frame++)
            {
                var sum = 0.0;
                var spectrum = power[frame];
                for (var bin = 0; bin < SpectrumBins; bin++)
                    sum += filter[bin] * spectrum[bin];

                var value = (float)Math.Log10(Math.Max(sum, 1e-10));
                result[(mel * FramesPerWindow) + frame] = value;
                if (value > max) max = value;
            }
        }

        // Whisper's own normalization: floor at 8 dB below the window's peak,
        // then map roughly onto [-1, 1].
        var floor = max - 8.0f;
        for (var i = 0; i < result.Length; i++)
            result[i] = (Math.Max(result[i], floor) + 4.0f) / 4.0f;

        return result;
    }

    /// <summary>
    /// Splits a recording into the fixed-size windows the encoder consumes.
    /// This is what makes memory constant in file length (r25 doc 03 3.4): a
    /// forty-minute file costs the same per window as a five-second one,
    /// instead of one ever-growing tensor.
    /// </summary>
    public static IEnumerable<(int Index, int Count, float[] Features)> Windows(float[] samples, int overlapSamples = 0)
    {
        var stride = Math.Max(1, SamplesPerWindow - Math.Max(0, overlapSamples));
        var count = samples.Length <= SamplesPerWindow
            ? 1
            : (int)Math.Ceiling((samples.Length - SamplesPerWindow) / (double)stride) + 1;

        for (var i = 0; i < count; i++)
        {
            var start = i * stride;
            if (start >= samples.Length && i > 0)
                break;
            var length = Math.Min(SamplesPerWindow, samples.Length - start);
            yield return (i, count, Compute(samples.AsSpan(start, Math.Max(0, length))));
        }
    }

    /// <summary>Squared magnitude of the 201 non-redundant bins of a 400-point real DFT.</summary>
    internal static float[] PowerSpectrum(float[] signal, int offset)
    {
        var windowed = new float[NFft];
        for (var n = 0; n < NFft; n++)
            windowed[n] = signal[offset + n] * _hann[n];

        var spectrum = new float[SpectrumBins];
        for (var k = 0; k < SpectrumBins; k++)
        {
            var baseIndex = k * NFft;
            var re = 0.0;
            var im = 0.0;
            for (var n = 0; n < NFft; n++)
            {
                var sample = windowed[n];
                re += sample * _twiddleCos[baseIndex + n];
                im += sample * _twiddleSin[baseIndex + n];
            }
            spectrum[k] = (float)((re * re) + (im * im));
        }

        return spectrum;
    }

    /// <summary>Periodic Hann (torch.hann_window's default), not the symmetric variant.</summary>
    internal static float[] BuildPeriodicHann(int length)
    {
        var window = new float[length];
        for (var n = 0; n < length; n++)
            window[n] = (float)(0.5 - (0.5 * Math.Cos(2.0 * Math.PI * n / length)));
        return window;
    }

    internal static float[] ReflectPad(float[] signal, int pad)
    {
        var padded = new float[signal.Length + (2 * pad)];
        Array.Copy(signal, 0, padded, pad, signal.Length);
        for (var i = 0; i < pad; i++)
        {
            padded[pad - 1 - i] = signal[Math.Min(i + 1, signal.Length - 1)];
            padded[pad + signal.Length + i] = signal[Math.Max(signal.Length - 2 - i, 0)];
        }
        return padded;
    }

    private static float[] BuildTwiddle(bool cosine)
    {
        var table = new float[SpectrumBins * NFft];
        for (var k = 0; k < SpectrumBins; k++)
            for (var n = 0; n < NFft; n++)
            {
                var angle = -2.0 * Math.PI * k * n / NFft;
                table[(k * NFft) + n] = (float)(cosine ? Math.Cos(angle) : Math.Sin(angle));
            }
        return table;
    }

    // ── Mel filterbank ──────────────────────────────────────────────────────
    // Slaney-scale, Slaney-normalized triangular filters, which is what
    // librosa.filters.mel(htk=False, norm="slaney") produces and what Whisper
    // ships. Computed from the formula rather than embedded as a blob, so its
    // shape and normalization are assertable instead of opaque.

    private const double MelBreakHz = 1000.0;
    private const double MelLinearStep = 200.0 / 3.0;

    internal static double HzToMel(double hz)
    {
        var mel = hz / MelLinearStep;
        if (hz < MelBreakHz)
            return mel;

        var minLogMel = MelBreakHz / MelLinearStep;
        var logStep = Math.Log(6.4) / 27.0;
        return minLogMel + (Math.Log(hz / MelBreakHz) / logStep);
    }

    internal static double MelToHz(double mel)
    {
        var minLogMel = MelBreakHz / MelLinearStep;
        if (mel < minLogMel)
            return mel * MelLinearStep;

        var logStep = Math.Log(6.4) / 27.0;
        return MelBreakHz * Math.Exp(logStep * (mel - minLogMel));
    }

    internal static float[][] BuildSlaneyMelFilterbank()
    {
        var fftFreqs = new double[SpectrumBins];
        for (var k = 0; k < SpectrumBins; k++)
            fftFreqs[k] = (double)k * SampleRate / NFft;

        var melMin = HzToMel(0);
        var melMax = HzToMel(SampleRate / 2.0);
        var points = new double[MelBins + 2];
        for (var i = 0; i < points.Length; i++)
            points[i] = MelToHz(melMin + ((melMax - melMin) * i / (MelBins + 1)));

        var filters = new float[MelBins][];
        for (var mel = 0; mel < MelBins; mel++)
        {
            var lower = points[mel];
            var centre = points[mel + 1];
            var upper = points[mel + 2];

            // Slaney normalization: equal area per filter, so wide high-frequency
            // filters do not dominate narrow low-frequency ones.
            var enorm = 2.0 / (upper - lower);

            var row = new float[SpectrumBins];
            for (var k = 0; k < SpectrumBins; k++)
            {
                var freq = fftFreqs[k];
                var rising = centre > lower ? (freq - lower) / (centre - lower) : 0.0;
                var falling = upper > centre ? (upper - freq) / (upper - centre) : 0.0;
                row[k] = (float)(Math.Max(0.0, Math.Min(rising, falling)) * enorm);
            }
            filters[mel] = row;
        }

        return filters;
    }
}
