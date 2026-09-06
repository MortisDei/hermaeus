using System.Security.Cryptography;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Hermaeus.Voice;

/// <summary>
/// Lazy-loading wrapper around Kokoro's ONNX graph
/// (onnx-community/Kokoro-82M-v1.0-ONNX). Follows the same asset posture as
/// Hermaeus.Rag's OnnxCrossEncoderReranker: never downloads on the synthesis
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

    private readonly Func<string> _assetsRootProvider;
    private readonly AppLifecycleJournalService? _journal;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private InferenceSession? _session;
    private string? _loadedAssetsRoot;
    private string? _stateAssetsRoot;
    private readonly Dictionary<string, float[]> _voiceStyleCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _unavailable;
    private readonly IResourceCoordinator? _resourceCoordinator;

    /// <summary>Stable, user-facing reason for the most recent admission failure.</summary>
    public string LastAdmissionFailure { get; private set; } = "not_attempted";

    /// <summary>
    /// Re-resolved on every access rather than captured once, so a
    /// LocalAiAssetsRoot change in Settings takes effect on the next
    /// install/load without requiring an app restart (this singleton is
    /// constructed once for the process lifetime).
    /// </summary>
    private string AssetsRoot => _assetsRootProvider();

    public KokoroOnnxModel(
        Func<string> assetsRootProvider,
        AppLifecycleJournalService? journal = null,
        IResourceCoordinator? resourceCoordinator = null)
    {
        _assetsRootProvider = assetsRootProvider;
        _journal = journal;
        _resourceCoordinator = resourceCoordinator;
    }

    public bool IsLoaded => _session is not null;

    public static string ModelPath(string assetsRoot) => Path.Combine(assetsRoot, ModelFileName);
    public static string VoicePath(string assetsRoot, string voice) => Path.Combine(assetsRoot, "voices", $"{voice}.bin");

    public bool AssetsPresent(string voice) =>
        File.Exists(ModelPath(AssetsRoot)) && File.Exists(VoicePath(AssetsRoot, voice));

    public async Task<bool> EnsureLoadedAsync(string voice, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            InvalidateIfRootChanged();

            if (_session is not null)
            {
                LastAdmissionFailure = string.Empty;
                return true;
            }

            if (_unavailable)
                return false;

            var modelPath = ModelPath(AssetsRoot);
            if (!File.Exists(modelPath))
            {
                LastAdmissionFailure = "model_missing";
                _unavailable = true;
                return false;
            }
            if (!await VerifySha256Async(modelPath, ModelSha256, ct))
            {
                LastAdmissionFailure = "model_sha256_mismatch";
                _unavailable = true;
                return false;
            }

            return await LoadSessionAsync(modelPath, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            LastAdmissionFailure = "load_exception";
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
            InvalidateIfRootChanged();

            Directory.CreateDirectory(AssetsRoot);
            Directory.CreateDirectory(Path.Combine(AssetsRoot, "voices"));
            LogPreflight("install starting");

            progress?.Report("Downloading Kokoro ONNX model...");
            await DownloadIfMissingAsync(ModelPath(AssetsRoot), ModelUrl, ModelSha256, progress, ct);
            LogPreflight("model download+verify complete");

            foreach (var voice in voices)
            {
                if (!VoiceSha256.TryGetValue(voice, out var expectedHash))
                    continue;

                progress?.Report($"Downloading voice: {voice}...");
                await DownloadIfMissingAsync(
                    VoicePath(AssetsRoot, voice),
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
            if (!await LoadSessionAsync(ModelPath(AssetsRoot), ct))
                throw new InvalidOperationException($"Kokoro ONNX session was not admitted: {LastAdmissionFailure}.");
            _unavailable = false;
            progress?.Report("Kokoro native voice assets installed.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
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

    private async Task<bool> LoadSessionAsync(string modelPath, CancellationToken ct)
    {
        IResourceAdmissionLease? lease = null;
        try
        {
            lease = await AcquireAdmissionAsync(ct);
            LogPreflight("about to load InferenceSession");
            _journal?.RecordOperation("loading Kokoro native ONNX session (EnsureLoadedAsync)");
            _session = new InferenceSession(modelPath, BuildSessionOptions());
            var contractFailure = ValidateSessionContract(_session);
            if (contractFailure is not null)
            {
                LastAdmissionFailure = contractFailure;
                _session.Dispose();
                _session = null;
                return false;
            }
            _loadedAssetsRoot = AssetsRoot;
            if (lease is not null)
            {
                var proposal = lease.Plan.ProposedAllocations.Single();
                await lease.CompleteAsync(new ResourceAllocation(
                    proposal.AllocationId,
                    proposal.ConsumerId,
                    proposal.AttemptId,
                    ResourceLifecycleState.Active,
                    proposal.RuntimeIdentity,
                    proposal.ModelIdentities,
                    proposal.ConfigurationIdentity,
                    proposal.ProcessIdentity,
                    proposal.Components,
                    DateTime.UtcNow,
                    proposal.Evidence));
            }
            _journal?.RecordOperation("Kokoro native ONNX session loaded");
            LastAdmissionFailure = string.Empty;
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastAdmissionFailure = $"onnx_session_exception:{ex.GetType().Name}:{TrimFailure(ex.Message)}";
            _session?.Dispose();
            _session = null;
            return false;
        }
        finally
        {
            if (lease is not null && !lease.IsCompleted && !lease.IsReleased)
                await lease.DisposeAsync();
        }
    }

    private async Task<IResourceAdmissionLease?> AcquireAdmissionAsync(CancellationToken ct)
    {
        if (_resourceCoordinator is null)
            return null;
        const string consumerId = ResourceConsumerIds.NativeKokoro;
        _resourceCoordinator.RegisterConsumer(new ResourceConsumerDescriptor(
            consumerId,
            ResourceConsumerKind.TextToSpeech,
            ResourceOwnerIdentity.InProcess(consumerId),
            // The Services-side adapter registers the logical consumer before
            // this lazy model is first loaded. Keep the lifecycle owner
            // identical so admission is idempotent. The Python Kokoro
            // provider has a different provider id and never reaches this
            // registration path.
            nameof(NativeKokoroVoiceProvider),
            ResourcePriorityClass.Foreground,
            ResourceReclaimability.Cooperative,
            // Keep the descriptor sequence identical to ResourceConsumerAdapters.Kokoro.
            [ResourceKind.DeviceMemory, ResourceKind.SystemResidentMemory]));
        var proposal = new ResourceAllocation(
            $"inprocess-{ResourceConsumerIds.NativeKokoro}",
            consumerId,
            null,
            ResourceLifecycleState.Planned,
            null,
            null,
            null,
            null,
            [new ResourceAllocationComponent(
                "onnx-session",
                ResourceComponentKind.OnnxSession,
                null,
                null,
                null,
                null,
                ResourceEvidenceState.Unknown,
                ResourceKind.SystemResidentMemory)],
            null,
            null);
        return await _resourceCoordinator.AcquireAsync(new ResourceAdmissionRequest(
            consumerId,
            proposal,
            callerId: $"{ResourceConsumerIds.NativeKokoro}.load",
            allowUnknown: true), ct);
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
        var path = VoicePath(AssetsRoot, voice);
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
        // ResponseHeadersRead avoids buffering the whole response (a few
        // hundred MB for the model) into memory before streaming it to disk,
        // which could OOM low-RAM machines (docs/review/01-code-audit.md P2-6).
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

    /// <summary>
    /// Deliberately conservative: the default (all-optimizations, all-thread)
    /// session options have been observed to crash the whole process natively
    /// (bypassing managed exception handling) when loading this specific
    /// quantized graph on at least one real machine. Disabling graph-fusion
    /// optimizations and multi-threaded execution trades a little inference
    /// speed for avoiding whatever fused/parallel kernel path was crashing.
    /// </summary>
    private static SessionOptions BuildSessionOptions() => new()
    {
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC,
        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        InterOpNumThreads = 1,
        IntraOpNumThreads = 1
    };

    private static string? ValidateSessionContract(InferenceSession session)
    {
        foreach (var required in new[] { "input_ids", "style", "speed" })
        {
            if (!session.InputMetadata.Keys.Contains(required, StringComparer.Ordinal))
                return $"onnx_contract_missing_input:{required}";
        }

        return session.OutputMetadata.Count == 0 ? "onnx_contract_missing_output" : null;
    }

    private static string TrimFailure(string message)
    {
        var singleLine = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= 240 ? singleLine : singleLine[..240];
    }

    /// <summary>
    /// Must be called under <see cref="_gate"/>. Drops any loaded session and
    /// cached voice styles if LocalAiAssetsRoot changed since they were
    /// loaded, so a mid-session settings change doesn't leave this singleton
    /// serving a model/voice from a stale, no-longer-configured location.
    /// </summary>
    private void InvalidateIfRootChanged()
    {
        var currentRoot = AssetsRoot;
        if (_stateAssetsRoot is null)
        {
            _stateAssetsRoot = currentRoot;
            return;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(_stateAssetsRoot, currentRoot, comparison))
            return;

        _session?.Dispose();
        _session = null;
        _resourceCoordinator?.ReleaseAllocation($"inprocess-{ResourceConsumerIds.NativeKokoro}");
        _unavailable = false;
        _voiceStyleCache.Clear();
        _loadedAssetsRoot = null;
        _stateAssetsRoot = currentRoot;
    }

    private void LogPreflight(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AssetsRoot, "kokoro_native_install.log"),
                $"{DateTime.UtcNow:O} {message} (model size: {SafeFileLength(ModelPath(AssetsRoot))} bytes)\n");
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
        _resourceCoordinator?.ReleaseAllocation($"inprocess-{ResourceConsumerIds.NativeKokoro}");
        _gate.Dispose();
    }
}
