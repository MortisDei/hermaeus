using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Aether.Voice;

/// <summary>
/// Lazy-loading wrapper around Kokoro's ONNX graph
/// (onnx-community/Kokoro-82M-v1.0-ONNX). Follows the same asset posture as
/// Aether.Rag's OnnxCrossEncoderReranker: never downloads on the synthesis
/// path, only loads assets that are already present and SHA256-verified;
/// downloading is an explicit, separate install step.
/// </summary>
internal sealed class KokoroOnnxModel : IDisposable
{
    // onnx-community/Kokoro-82M-v1.0-ONNX, quantized fp16 variant: small
    // enough for a default download, still full quality voices/style vectors.
    private const string ModelCommit = "main";
    private const string RepoBaseUrl = $"https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/{ModelCommit}";
    private const string ModelUrl = $"{RepoBaseUrl}/onnx/model_q8f16.onnx";
    private const string ModelFileName = "model_q8f16.onnx";
    public const string ModelSha256 = "04c658aec1b6008857c2ad10f8c589d4180d0ec427e7e6118ceb487e215c3cd0";

    public const int SampleRate = 24000;

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(20) };
    private static readonly IReadOnlyDictionary<string, string> VoiceSha256 = KokoroVoiceAssets.Sha256ByVoice;

    private readonly string _assetsRoot;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private InferenceSession? _session;
    private readonly Dictionary<string, float[]> _voiceStyleCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _unavailable;

    public KokoroOnnxModel(string assetsRoot)
    {
        _assetsRoot = assetsRoot;
    }

    public static string ModelPath(string assetsRoot) => Path.Combine(assetsRoot, ModelFileName);
    public static string VoicePath(string assetsRoot, string voice) => Path.Combine(assetsRoot, "voices", $"{voice}.bin");

    public bool AssetsPresent(string voice) =>
        File.Exists(ModelPath(_assetsRoot)) && File.Exists(VoicePath(_assetsRoot, voice));

    public async Task<bool> EnsureLoadedAsync(string voice, CancellationToken ct)
    {
        if (_session is not null)
            return true;

        await _gate.WaitAsync(ct);
        try
        {
            if (_session is not null)
                return true;

            if (_unavailable)
                return false;

            var modelPath = ModelPath(_assetsRoot);
            if (!File.Exists(modelPath) || !await VerifySha256Async(modelPath, ModelSha256, ct))
            {
                _unavailable = true;
                return false;
            }

            LogPreflight("about to load InferenceSession from EnsureLoadedAsync");
            _session = new InferenceSession(modelPath);
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

    /// <summary>
    /// Downloads and SHA256-verifies the model plus the requested voice
    /// style file. Never called from the synthesis path; only from an
    /// explicit setup/doctor action.
    /// </summary>
    public async Task InstallAssetsAsync(IReadOnlyList<string> voices, IProgress<string>? progress, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_assetsRoot);
            Directory.CreateDirectory(Path.Combine(_assetsRoot, "voices"));
            LogPreflight("install starting");

            progress?.Report("Downloading Kokoro ONNX model...");
            await DownloadIfMissingAsync(ModelPath(_assetsRoot), ModelUrl, ModelSha256, progress, ct);
            LogPreflight("model download+verify complete");

            foreach (var voice in voices)
            {
                if (!VoiceSha256.TryGetValue(voice, out var expectedHash))
                    continue;

                progress?.Report($"Downloading voice: {voice}...");
                await DownloadIfMissingAsync(
                    VoicePath(_assetsRoot, voice),
                    $"{RepoBaseUrl}/voices/{voice}.bin",
                    expectedHash,
                    progress,
                    ct);
                LogPreflight($"voice download+verify complete: {voice}");
            }

            _session?.Dispose();
            _session = null;
            // A native ONNX Runtime fault here (corrupt/incompatible model) bypasses
            // managed exception handling and kills the process; this line is flushed
            // to disk immediately before the risky call so a crash still leaves a
            // record of exactly where it happened.
            LogPreflight("about to load InferenceSession after install");
            _session = new InferenceSession(ModelPath(_assetsRoot));
            _unavailable = false;
            progress?.Report("Kokoro native voice assets installed.");
        }
        catch (Exception ex)
        {
            _unavailable = true;
            progress?.Report($"Kokoro native install failed: {ex.Message}");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Runs inference for one already-chunked token sequence and returns raw
    /// 24kHz float PCM samples.
    /// </summary>
    public float[] Synthesize(int[] tokenIds, string voice, double speed)
    {
        if (_session is null)
            throw new InvalidOperationException("Kokoro ONNX session is not loaded.");

        // The style/voice row is indexed by the phoneme count *before* the
        // two boundary pad tokens were added (matches the model card's
        // reference Python: `ref_s = voices[len(tokens)]` where `tokens`
        // excludes the pad wrapper added just before inference).
        var coreTokenCount = Math.Max(0, tokenIds.Length - 2);
        var style = LoadStyleVector(voice, coreTokenCount);
        var inputIds = new DenseTensor<long>(tokenIds.Select(id => (long)id).ToArray(), new[] { 1, tokenIds.Length });
        var styleTensor = new DenseTensor<float>(style, new[] { 1, style.Length });
        var speedTensor = new DenseTensor<float>(new[] { (float)speed }, new[] { 1 });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("style", styleTensor),
            NamedOnnxValue.CreateFromTensor("speed", speedTensor)
        };

        using var results = _session.Run(inputs);
        return results.First().AsEnumerable<float>().ToArray();
    }

    private float[] LoadStyleVector(string voice, int tokenCount)
    {
        const int StyleDimensions = 256;
        var path = VoicePath(_assetsRoot, voice);
        if (!_voiceStyleCache.TryGetValue(path, out var allStyles))
        {
            var bytes = File.ReadAllBytes(path);
            allStyles = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, allStyles, 0, bytes.Length);
            _voiceStyleCache[path] = allStyles;
        }

        // voices/*.bin is shaped (-1, 1, 256): one 256-float style row per
        // possible token-sequence length, indexed by that length (see the
        // model card's reference Python snippet).
        var rowCount = allStyles.Length / StyleDimensions;
        var row = Math.Clamp(tokenCount, 0, rowCount - 1);
        var style = new float[StyleDimensions];
        Array.Copy(allStyles, row * StyleDimensions, style, 0, StyleDimensions);
        return style;
    }

    private async Task DownloadIfMissingAsync(string path, string url, string expectedSha256, IProgress<string>? progress, CancellationToken ct)
    {
        if (File.Exists(path) && await VerifySha256Async(path, expectedSha256, ct))
            return;

        var temp = $"{path}.download";
        using var response = await _http.GetAsync(url, ct);
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

    private void LogPreflight(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(_assetsRoot, "kokoro_native_install.log"),
                $"{DateTime.UtcNow:O} {message} (model size: {SafeFileLength(ModelPath(_assetsRoot))} bytes)\n");
        }
        catch { }
    }

    private static long SafeFileLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return -1; }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _gate.Dispose();
    }
}
