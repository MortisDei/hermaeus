using System.Security.Cryptography;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Hermaeus.Voice;

/// <summary>
/// Lazy-loading wrapper around a CTC speech-recognition ONNX graph
/// (facebook/wav2vec2-base-960h). Same asset posture as
/// <see cref="KokoroOnnxModel"/>: never downloads on the transcription path,
/// only loads assets that are already present and SHA256-verified; downloading
/// is an explicit, separate install step.
///
/// CTC (not an encoder-decoder) was chosen over a from-scratch Whisper port for
/// this round: one forward pass over the raw waveform, one argmax per frame, no
/// autoregressive decode loop, no KV cache, no BPE tokenizer - a 32-symbol
/// character vocabulary is the entire "tokenizer". This keeps the in-process
/// ONNX backend the owner asked for (see docs/review/05-voice-input.md's 5.1)
/// without taking on Whisper's decoder complexity in the same round as three
/// other large items. English-only; a multilingual model is future work.
/// </summary>
internal sealed class Wav2Vec2OnnxModel : IDisposable
{
    // facebook/wav2vec2-base-960h, pinned commit (official Meta repo, CTC,
    // 16kHz mono, English). SHA256 verified 2026-07-27 by downloading the file
    // directly and hashing it (recorded here as this round's equivalent of
    // KokoroOnnxModel's release-tag verification date comment).
    private const string ModelCommit = "6d2b9ffaac8aabc45934584ee608c5fb5ee34a4e";
    private const string ModelUrl = $"https://huggingface.co/facebook/wav2vec2-base-960h/resolve/{ModelCommit}/onnx/model.onnx";
    private const string ModelFileName = "model.onnx";
    public const string ModelSha256 = "b73fe60ddcd3fd07f91d65d50b4f10ba99039104c4fb5db5bdafbb27610bb6eb";

    public const int SampleRate = 16000;

    // vocab.json at the pinned commit. Index 0 is the CTC blank token; index 4
    // ("|") is the word delimiter, mapped to a space on decode.
    internal static readonly string[] Vocab =
    [
        "<pad>", "<s>", "</s>", "<unk>", "|", "E", "T", "A", "O", "N", "I", "H", "S", "R", "D", "L",
        "U", "M", "W", "C", "F", "G", "Y", "P", "B", "V", "K", "'", "X", "J", "Q", "Z"
    ];
    private const int BlankIndex = 0;
    private const int WordDelimiterIndex = 4;

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(20) };

    private readonly Func<string> _assetsRootProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private InferenceSession? _session;
    private string? _loadedAssetsRoot;
    private bool _unavailable;

    private string AssetsRoot => _assetsRootProvider();

    public Wav2Vec2OnnxModel(Func<string> assetsRootProvider)
    {
        _assetsRootProvider = assetsRootProvider;
    }

    public static string ModelPath(string assetsRoot) => Path.Combine(assetsRoot, ModelFileName);

    public bool AssetsPresent() => File.Exists(ModelPath(AssetsRoot));

    public async Task<bool> EnsureLoadedAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            InvalidateIfRootChanged();

            if (_session is not null)
                return true;
            if (_unavailable)
                return false;

            var modelPath = ModelPath(AssetsRoot);
            if (!File.Exists(modelPath) || !await VerifySha256Async(modelPath, ModelSha256, ct))
            {
                _unavailable = true;
                return false;
            }

            _session = new InferenceSession(modelPath, BuildSessionOptions());
            _loadedAssetsRoot = AssetsRoot;
            return true;
        }
        catch
        {
            _unavailable = true;
            _session?.Dispose();
            _session = null;
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Downloads and SHA256-verifies the model. Never called from the
    /// transcription path; only from an explicit setup/doctor action.</summary>
    public async Task InstallAssetsAsync(IProgress<string>? progress, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            InvalidateIfRootChanged();
            Directory.CreateDirectory(AssetsRoot);

            progress?.Report("Downloading speech recognition model...");
            await DownloadIfMissingAsync(ModelPath(AssetsRoot), ModelUrl, ModelSha256, progress, ct);

            _session?.Dispose();
            _session = new InferenceSession(ModelPath(AssetsRoot), BuildSessionOptions());
            _loadedAssetsRoot = AssetsRoot;
            _unavailable = false;
            progress?.Report("Speech recognition model installed.");
        }
        catch (Exception ex)
        {
            _unavailable = true;
            progress?.Report($"Speech recognition install failed: {ex.Message}");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Runs the encoder+CTC head over normalized samples and greedy-decodes
    /// the result. The ONNX call aside, everything here is pure and independently
    /// testable: <see cref="NormalizeZeroMeanUnitVariance"/> and <see cref="GreedyCtcDecode"/>.</summary>
    public string Transcribe(float[] samples)
    {
        if (_session is null)
            throw new InvalidOperationException("Speech recognition ONNX session is not loaded.");

        var normalized = NormalizeZeroMeanUnitVariance(samples);
        var input = new DenseTensor<float>(normalized, new[] { 1, normalized.Length });
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input_values", input) };

        using var results = _session.Run(inputs);
        var logits = results.First(r => r.Name == "logits").AsTensor<float>();
        var frames = logits.Dimensions[1];
        var vocabSize = logits.Dimensions[2];

        var ids = new int[frames];
        for (var t = 0; t < frames; t++)
        {
            var best = 0;
            var bestScore = float.NegativeInfinity;
            for (var v = 0; v < vocabSize; v++)
            {
                var score = logits[0, t, v];
                if (score > bestScore) { bestScore = score; best = v; }
            }
            ids[t] = best;
        }

        return GreedyCtcDecode(ids);
    }

    /// <summary>CTC greedy decode: collapse consecutive duplicate frame ids, drop the
    /// blank, map the word delimiter to a space.</summary>
    internal static string GreedyCtcDecode(IReadOnlyList<int> frameIds)
    {
        var sb = new StringBuilder();
        var previous = -1;
        foreach (var id in frameIds)
        {
            if (id == previous) continue;
            previous = id;
            if (id == BlankIndex) continue;
            if (id == WordDelimiterIndex) { sb.Append(' '); continue; }
            if (id >= 0 && id < Vocab.Length) sb.Append(Vocab[id]);
        }
        return sb.ToString().Trim();
    }

    /// <summary>HF Wav2Vec2FeatureExtractor's zero-mean/unit-variance normalization
    /// (do_normalize=true in preprocessor_config.json at the pinned commit).</summary>
    internal static float[] NormalizeZeroMeanUnitVariance(float[] samples)
    {
        if (samples.Length == 0) return samples;

        var mean = 0.0;
        foreach (var s in samples) mean += s;
        mean /= samples.Length;

        var variance = 0.0;
        foreach (var s in samples) variance += (s - mean) * (s - mean);
        variance /= samples.Length;

        var denom = Math.Sqrt(variance + 1e-7);
        var result = new float[samples.Length];
        for (var i = 0; i < samples.Length; i++)
            result[i] = (float)((samples[i] - mean) / denom);
        return result;
    }

    private async Task DownloadIfMissingAsync(string path, string url, string expectedSha256, IProgress<string>? progress, CancellationToken ct)
    {
        if (File.Exists(path) && await VerifySha256Async(path, expectedSha256, ct))
            return;

        var temp = $"{path}.download";
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var target = File.Create(temp))
            await source.CopyToAsync(target, ct);

        if (!await VerifySha256Async(temp, expectedSha256, ct))
        {
            File.Delete(temp);
            throw new InvalidOperationException($"{Path.GetFileName(path)} failed SHA256 verification.");
        }

        File.Move(temp, path, overwrite: true);
        progress?.Report($"Downloaded: {Path.GetFileName(path)}");
    }

    public static async Task<bool> VerifySha256Async(string path, string expectedSha256, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return false;

        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Same conservative posture as <see cref="KokoroOnnxModel"/>'s session
    /// options: sequential, single-threaded, basic optimizations only.</summary>
    private static SessionOptions BuildSessionOptions() => new()
    {
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC,
        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        InterOpNumThreads = 1,
        IntraOpNumThreads = 1
    };

    private void InvalidateIfRootChanged()
    {
        if (_loadedAssetsRoot is null || _loadedAssetsRoot == AssetsRoot)
            return;

        _session?.Dispose();
        _session = null;
        _unavailable = false;
        _loadedAssetsRoot = null;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _gate.Dispose();
    }
}
