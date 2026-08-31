using System.Security.Cryptography;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Hermaeus.Voice;

/// <summary>
/// In-process Whisper (r25 doc 03). Replaces the wav2vec2 CTC model r24
/// shipped, which had a 32-symbol uppercase vocabulary with no punctuation in
/// it, so every transcript read HELLO CAN YOU CHECK THE BUILD and no
/// post-processing could recover what the model never produced.
///
/// Same asset posture as <see cref="KokoroOnnxModel"/>: never downloads on the
/// transcription path, only loads assets already present and SHA256-verified;
/// downloading is an explicit, separate, approval-gated install step.
///
/// **The KV cache is graph IO, not arithmetic.** r24 rejected in-process
/// Whisper partly on the grounds that "KV cache management" had to be written
/// from scratch. With an exported decoder that is not so: ONNX Runtime returns
/// <c>present.*</c> tensors which are fed back as <c>past_key_values.*</c> on
/// the next step, and the merged decoder switches between its first and
/// subsequent passes on a <c>use_cache_branch</c> flag. It is bookkeeping over
/// named tensors. Recorded here so a later round does not re-litigate it.
///
/// Input and output names are discovered from the loaded sessions rather than
/// hardcoded, so a differently-exported Whisper stays loadable.
/// </summary>
internal sealed class WhisperOnnxModel : IDisposable
{
    // onnx-community/whisper-base, pinned revision. The two ONNX files are
    // Git LFS objects, so their SHA256 is the authoritative lfs.oid published by
    // the Hugging Face tree API (the same mechanism r11's starter-model catalog
    // and r13's model manifest already use, since downloading hundreds of
    // megabytes to compute a hash by hand is not practical). The small JSON
    // assets are NOT LFS objects, so their git blob ids are not content hashes;
    // those four were downloaded and hashed directly.
    //
    // Verified 2026-07-30 against revision 1846881b6b3a3024392c1eea3ad983695bc23925.
    private const string Repo = "onnx-community/whisper-base";
    private const string Revision = "1846881b6b3a3024392c1eea3ad983695bc23925";

    internal static readonly WhisperAsset Encoder = new(
        "onnx/encoder_model.onnx", "encoder_model.onnx", 82_468_078,
        "a9f3b752833b49e880dec91ee5b6d936112be7c3ea07c221024ba493439f46fe");

    internal static readonly WhisperAsset Decoder = new(
        "onnx/decoder_model_merged.onnx", "decoder_model_merged.onnx", 208_521_528,
        "514903744bb1b45803ec571af99b31110491c6f77b0a154825866995fb124b73");

    internal static readonly WhisperAsset Vocab = new(
        "vocab.json", "vocab.json", 1_036_584,
        "50d6a919f0a0601d56a04eb583c780d18553aa388254ba3158eb6a00f13e2c1a");

    internal static readonly WhisperAsset AddedTokens = new(
        "added_tokens.json", "added_tokens.json", 34_604,
        "9715fd2243b6f06a5858b5e32950d2853f73dd5bc201aafcf76f5082a2d8acd1");

    internal static readonly WhisperAsset GenerationConfig = new(
        "generation_config.json", "generation_config.json", 3_832,
        "61070cf8de25b1e9256e8e102ded49d8d24a8369ed36ef84fdf21549e68125a0");

    internal static readonly WhisperAsset[] AllAssets =
        [Encoder, Decoder, Vocab, AddedTokens, GenerationConfig];

    /// <summary>Total download size, for the install plan's risk notes.</summary>
    internal static long TotalDownloadBytes => AllAssets.Sum(a => a.SizeBytes);

    public const int SampleRate = LogMelSpectrogram.SampleRate;

    /// <summary>Multilingual: 98 language tokens at the pinned revision.</summary>
    public const bool IsMultilingual = true;

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };

    private readonly Func<string> _assetsRootProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private InferenceSession? _encoder;
    private InferenceSession? _decoder;
    private WhisperVocabulary? _vocab;
    private WhisperDecoderBinding? _binding;
    private string? _loadedAssetsRoot;
    private bool _unavailable;
    private readonly IResourceCoordinator? _resourceCoordinator;

    private string AssetsRoot => _assetsRootProvider();

    public WhisperOnnxModel(Func<string> assetsRootProvider, IResourceCoordinator? resourceCoordinator = null)
    {
        _assetsRootProvider = assetsRootProvider;
        _resourceCoordinator = resourceCoordinator;
    }

    public bool IsLoaded => _encoder is not null && _decoder is not null && _vocab is not null;

    public static string PathFor(string assetsRoot, WhisperAsset asset) =>
        Path.Combine(assetsRoot, asset.LocalName);

    public bool AssetsPresent() => AllAssets.All(a => File.Exists(PathFor(AssetsRoot, a)));

    public async Task<bool> EnsureLoadedAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            InvalidateIfRootChanged();

            if (_encoder is not null && _decoder is not null && _vocab is not null)
                return true;
            if (_unavailable)
                return false;

            var root = AssetsRoot;
            foreach (var asset in AllAssets)
            {
                var path = PathFor(root, asset);
                if (!File.Exists(path) || !await VerifySha256Async(path, asset.Sha256, ct))
                {
                    _unavailable = true;
                    return false;
                }
            }

            return await LoadSessionsAsync(root, ct);
        }
        catch
        {
            _unavailable = true;
            DisposeSessions();
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> LoadSessionsAsync(string root, CancellationToken ct)
    {
        IResourceAdmissionLease? lease = null;
        try
        {
            lease = await AcquireAdmissionAsync(ct);
            _encoder = new InferenceSession(PathFor(root, Encoder), BuildSessionOptions());
            _decoder = new InferenceSession(PathFor(root, Decoder), BuildSessionOptions());
            _binding = WhisperDecoderBinding.Discover(_decoder);
            _vocab = WhisperVocabulary.Load(
                await File.ReadAllTextAsync(PathFor(root, Vocab), ct),
                await File.ReadAllTextAsync(PathFor(root, AddedTokens), ct),
                await File.ReadAllTextAsync(PathFor(root, GenerationConfig), ct));
            _loadedAssetsRoot = root;
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
            return true;
        }
        catch
        {
            DisposeSessions();
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
        const string consumerId = "stt.whisper";
        _resourceCoordinator.RegisterConsumer(new ResourceConsumerDescriptor(
            consumerId,
            ResourceConsumerKind.SpeechToText,
            ResourceOwnerIdentity.InProcess(consumerId),
            nameof(WhisperOnnxModel),
            ResourcePriorityClass.Foreground,
            ResourceReclaimability.Cooperative,
            [ResourceKind.SystemResidentMemory, ResourceKind.DeviceMemory]));
        var proposal = new ResourceAllocation(
            "inprocess-stt.whisper",
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
            callerId: "stt.whisper.load",
            allowUnknown: true), ct);
    }

    /// <summary>Downloads and SHA256-verifies every asset. Never called from the
    /// transcription path; only from an explicit setup or Doctor action.</summary>
    public async Task InstallAssetsAsync(IProgress<string>? progress, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            InvalidateIfRootChanged();
            Directory.CreateDirectory(AssetsRoot);

            foreach (var asset in AllAssets)
            {
                progress?.Report($"Downloading {asset.LocalName}...");
                await DownloadIfMissingAsync(asset, ct);
            }

            DisposeSessions();
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

    /// <summary>
    /// Transcribes any length of audio in fixed 30-second windows, so peak memory
    /// is the same for a forty-minute file as for a five-second one. Cancellation
    /// is honoured between windows.
    /// </summary>
    public WhisperTranscription Transcribe(
        float[] samples, string? forcedLanguage, IProgress<string>? progress, CancellationToken ct)
    {
        if (_encoder is null || _decoder is null || _vocab is null || _binding is null)
            throw new InvalidOperationException("Whisper ONNX sessions are not loaded.");

        var text = new System.Text.StringBuilder();
        var language = string.Empty;
        var truncated = false;
        var looping = false;

        foreach (var (index, count, features) in LogMelSpectrogram.Windows(samples))
        {
            ct.ThrowIfCancellationRequested();
            if (count > 1)
                progress?.Report($"Transcribing part {index + 1} of {count}...");

            var encoded = RunEncoder(features);
            var session = new WhisperDecoderSession(_decoder, _binding, encoded);
            var result = WhisperGreedyDecoder.Decode(_vocab, forcedLanguage, session.Step, ct);

            if (language.Length == 0)
                language = result.Language;
            if (result.StopReason == WhisperStopReason.TokenCap)
                truncated = true;
            if (WhisperGreedyDecoder.LooksLikeRepetitionLoop(result.Tokens))
                looping = true;

            var piece = _vocab.Decode(result.Tokens);
            if (piece.Length > 0)
            {
                if (text.Length > 0) text.Append(' ');
                text.Append(piece.Trim());
            }
        }

        return new WhisperTranscription(text.ToString().Trim(), language, truncated, looping);
    }

    private DenseTensor<float> RunEncoder(float[] features)
    {
        var input = new DenseTensor<float>(
            features, [1, LogMelSpectrogram.MelBins, LogMelSpectrogram.FramesPerWindow]);
        var name = _encoder!.InputMetadata.Keys.First();
        using var results = _encoder.Run([NamedOnnxValue.CreateFromTensor(name, input)]);
        var output = results.First().AsTensor<float>();
        return new DenseTensor<float>(output.ToArray(), output.Dimensions.ToArray());
    }

    private async Task DownloadIfMissingAsync(WhisperAsset asset, CancellationToken ct)
    {
        var path = PathFor(AssetsRoot, asset);
        if (File.Exists(path) && await VerifySha256Async(path, asset.Sha256, ct))
            return;

        var url = $"https://huggingface.co/{Repo}/resolve/{Revision}/{asset.RemotePath}";
        var temp = $"{path}.download";
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var target = File.Create(temp))
            await source.CopyToAsync(target, ct);

        if (!await VerifySha256Async(temp, asset.Sha256, ct))
        {
            File.Delete(temp);
            throw new InvalidOperationException($"{asset.LocalName} failed SHA256 verification.");
        }

        File.Move(temp, path, overwrite: true);
    }

    public static async Task<bool> VerifySha256Async(string path, string expected, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return false;

        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return string.Equals(Convert.ToHexString(hash), expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Same conservative posture as <see cref="KokoroOnnxModel"/>'s options.</summary>
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

        DisposeSessions();
        _unavailable = false;
        _loadedAssetsRoot = null;
    }

    private void DisposeSessions()
    {
        _encoder?.Dispose();
        _decoder?.Dispose();
        _encoder = null;
        _decoder = null;
        _vocab = null;
        _binding = null;
        _resourceCoordinator?.ReleaseAllocation("inprocess-stt.whisper");
    }

    public void Dispose()
    {
        DisposeSessions();
        _gate.Dispose();
    }
}

/// <summary>One pinned model file: where it comes from, what it is called locally,
/// how big it is, and the SHA256 it must match.</summary>
internal sealed record WhisperAsset(string RemotePath, string LocalName, long SizeBytes, string Sha256);

internal sealed record WhisperTranscription(string Text, string Language, bool HitTokenCap, bool LooksLikeLoop);
