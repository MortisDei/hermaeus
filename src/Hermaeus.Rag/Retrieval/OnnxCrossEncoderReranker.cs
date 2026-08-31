using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Hermaeus.Rag.Retrieval;

public sealed class OnnxCrossEncoderReranker : IReranker, IDisposable
{
    public const int MaximumExperimentBatchSize = 8;
    public const int MaximumExperimentCandidates = 20;
    public const float ScoreEquivalenceTolerance = 0.00001f;

    private const string ModelCommit = "eeed17e3bfc6fa06a790f2d12a9501fec587fccf";
    private const string ModelUrl = $"https://huggingface.co/cross-encoder/ms-marco-MiniLM-L6-v2/resolve/{ModelCommit}/onnx/model_O4.onnx";
    private const string VocabUrl = $"https://huggingface.co/cross-encoder/ms-marco-MiniLM-L6-v2/resolve/{ModelCommit}/vocab.txt";
    private const string ModelFileName = "model_O4.onnx";
    private const string VocabFileName = "vocab.txt";
    public const string ModelSha256 = "b232c2eeedd97a593edc177e3ce4cbd1d6c8f6d8f61a5c201cd0cdeb8134da18";
    public const string VocabSha256 = "07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly ISettingsService _settings;
    private readonly AppLifecycleJournalService? _journal;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private InferenceSession? _session;
    private BertTokenizer? _tokenizer;
    private string? _loadedAssetKey;
    private string? _unavailableAssetKey;
    private readonly IResourceCoordinator? _resourceCoordinator;

    public OnnxCrossEncoderReranker(
        ISettingsService settings,
        AppLifecycleJournalService? journal = null,
        IResourceCoordinator? resourceCoordinator = null)
    {
        _settings = settings;
        _journal = journal;
        _resourceCoordinator = resourceCoordinator;
    }

    /// <summary>True only while the verified ONNX session and tokenizer are resident.</summary>
    public bool IsLoaded => _session is not null && _tokenizer is not null;

    public async Task<List<ScoredChunk>> RerankAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int topK,
        CancellationToken ct = default)
    {
        if (!_settings.Settings.Rag.RerankerEnabled || candidates.Count == 0)
            return candidates.Take(topK).ToList();

        var loaded = await EnsureLoadedAsync(ct);
        if (!loaded || _session is null || _tokenizer is null)
            return candidates.Take(topK).ToList();

        var maxCandidates = Math.Clamp(_settings.Settings.Rag.RerankerMaxCandidates, topK, 100);
        var maxLength = Math.Clamp(_settings.Settings.Rag.RerankerMaxLength, 64, 512);
        var reranked = new List<ScoredChunk>();

        foreach (var candidate in candidates.Take(maxCandidates))
        {
            ct.ThrowIfCancellationRequested();
            var score = ScorePair(query, candidate.Chunk.Content, maxLength);
            reranked.Add(candidate with { Score = score, Source = ScoreSource.Reranker });
        }

        var originalRanks = candidates
            .Select((candidate, index) => new { candidate.Chunk.Id, index })
            .ToDictionary(x => x.Id, x => x.index);

        return reranked
            .OrderByDescending(x => x.Score)
            .ThenBy(x => originalRanks.GetValueOrDefault(x.Chunk.Id, int.MaxValue))
            .Take(topK)
            .ToList();
    }

    private async Task<bool> EnsureLoadedAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        string? assetKey = null;
        try
        {
            var modelDir = ResolveModelDirectory(_settings.Settings);
            var modelPath = Path.Combine(modelDir, ModelFileName);
            var vocabPath = Path.Combine(modelDir, VocabFileName);
            assetKey = CreateAssetIdentityKey(modelPath, vocabPath);

            if (_session is not null && _tokenizer is not null
                && string.Equals(_loadedAssetKey, assetKey, StringComparison.Ordinal))
                return true;

            if (!ShouldAttemptAssetLoad(assetKey, _loadedAssetKey, _unavailableAssetKey))
                return false;

            if (_loadedAssetKey is not null || _session is not null || _tokenizer is not null)
                ReleaseLoadedSession();

            // Do not perform heavy downloads during query path. Only initialize if assets already exist.
            if (!File.Exists(modelPath) || !File.Exists(vocabPath))
            {
                _unavailableAssetKey = assetKey;
                return false;
            }

            if (!await VerifyFileSha256Async(modelPath, ModelSha256, ct)
                || !await VerifyFileSha256Async(vocabPath, VocabSha256, ct))
            {
                _unavailableAssetKey = assetKey;
                return false;
            }

            if (!await LoadSessionAsync(modelPath, vocabPath, ct))
            {
                _unavailableAssetKey = assetKey;
                return false;
            }

            _loadedAssetKey = assetKey;
            _unavailableAssetKey = null;
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ReleaseLoadedSession();
            throw;
        }
        catch
        {
            _unavailableAssetKey = assetKey;
            ReleaseLoadedSession();
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Install model assets explicitly. This method performs heavy downloads and should be
    // invoked from a setup or doctor action rather than the query path.
    public async Task<bool> InstallAssetsAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var modelDir = ResolveModelDirectory(_settings.Settings);
            var modelPath = Path.Combine(modelDir, ModelFileName);
            var vocabPath = Path.Combine(modelDir, VocabFileName);
            Directory.CreateDirectory(modelDir);
            progress?.Report("Downloading reranker model...");
            await DownloadIfMissingAsync(modelPath, ModelUrl, ModelSha256, progress, ct);
            progress?.Report("Downloading reranker vocabulary...");
            await DownloadIfMissingAsync(vocabPath, VocabUrl, VocabSha256, progress, ct);
            progress?.Report("Loading reranker model...");
            ReleaseLoadedSession();
            if (!await LoadSessionAsync(modelPath, vocabPath, ct))
                throw new InvalidOperationException("Reranker assets were present but the ONNX session was not admitted.");
            _loadedAssetKey = CreateAssetIdentityKey(modelPath, vocabPath);
            _unavailableAssetKey = null;
            progress?.Report("Reranker installed");
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ReleaseLoadedSession();
            throw;
        }
        catch (Exception ex)
        {
            progress?.Report($"Reranker install failed: {ex.Message}");
            ReleaseLoadedSession();
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> LoadSessionAsync(string modelPath, string vocabPath, CancellationToken ct)
    {
        IResourceAdmissionLease? lease = null;
        try
        {
            lease = await AcquireAdmissionAsync(ct);
            _tokenizer = BertTokenizer.Create(vocabPath);
            _journal?.RecordOperation("loading reranker ONNX session (EnsureLoadedAsync)");
            _session = new InferenceSession(modelPath);
            _journal?.RecordOperation("reranker ONNX session loaded");
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _session?.Dispose();
            _session = null;
            _tokenizer = null;
            throw;
        }
        catch
        {
            _session?.Dispose();
            _session = null;
            _tokenizer = null;
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
        const string consumerId = "rag.reranker";
        _resourceCoordinator.RegisterConsumer(new ResourceConsumerDescriptor(
            consumerId,
            ResourceConsumerKind.Reranker,
            ResourceOwnerIdentity.InProcess(consumerId),
            nameof(OnnxCrossEncoderReranker),
            ResourcePriorityClass.Background,
            ResourceReclaimability.Cooperative,
            [ResourceKind.SystemResidentMemory, ResourceKind.DeviceMemory]));
        var proposal = new ResourceAllocation(
            "inprocess-rag.reranker",
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
            callerId: "rag.reranker.load",
            allowUnknown: true), ct);
    }

    private float ScorePair(string query, string passage, int maxLength)
    {
        var encoded = EncodePair(query, passage, maxLength);
        var shape = new[] { 1, maxLength };
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(encoded.InputIds, shape)),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(encoded.AttentionMask, shape)),
            NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(encoded.TokenTypeIds, shape))
        };

        using var results = _session!.Run(inputs);
        var output = results.First().AsEnumerable<float>().FirstOrDefault();
        return Sigmoid(output);
    }

    private float[] ScoreBatch(IReadOnlyList<EncodedPair> encoded)
    {
        if (encoded.Count == 0)
            return [];

        var maxLength = encoded[0].InputIds.Length;
        var inputIds = new long[encoded.Count * maxLength];
        var attentionMask = new long[inputIds.Length];
        var tokenTypeIds = new long[inputIds.Length];
        for (var index = 0; index < encoded.Count; index++)
        {
            var offset = index * maxLength;
            Array.Copy(encoded[index].InputIds, 0, inputIds, offset, maxLength);
            Array.Copy(encoded[index].AttentionMask, 0, attentionMask, offset, maxLength);
            Array.Copy(encoded[index].TokenTypeIds, 0, tokenTypeIds, offset, maxLength);
        }

        var shape = new[] { encoded.Count, maxLength };
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds, shape)),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(attentionMask, shape)),
            NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(tokenTypeIds, shape))
        };

        using var results = _session!.Run(inputs);
        var logits = results.First().AsEnumerable<float>().ToArray();
        if (logits.Length != encoded.Count)
            throw new InvalidOperationException($"Reranker batch returned {logits.Length} logits for {encoded.Count} pairs.");

        return logits.Select(Sigmoid).ToArray();
    }

    /// <summary>
    /// Runs a bounded, explicit experiment against the verified pinned model.
    /// Normal queries remain sequential until a separately reviewed production
    /// decision enables batching.
    /// </summary>
    public async Task<RerankerBatchExperimentResult> RunBatchExperimentAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int batchSize = MaximumExperimentBatchSize,
        int maxLength = 256,
        CancellationToken ct = default)
    {
        if (batchSize is < 2 or > MaximumExperimentBatchSize)
            return RerankerBatchExperimentResult.Unavailable(
                "reranker-batch-bounds",
                $"Batch size must be between 2 and {MaximumExperimentBatchSize}.");
        if (maxLength is < 64 or > 512)
            return RerankerBatchExperimentResult.Unavailable(
                "reranker-batch-bounds",
                "Maximum sequence length must be between 64 and 512.");
        if (candidates.Count < 2)
            return RerankerBatchExperimentResult.Unavailable(
                "reranker-batch-input",
                "At least two candidates are required for a batch comparison.");

        if (!await EnsureLoadedAsync(ct) || _session is null || _tokenizer is null)
            return RerankerBatchExperimentResult.Unknown(
                "reranker-batch-assets-unknown",
                "A verified pinned ONNX session is not available in the selected asset set.");

        if (!HasDynamicBatchGraph(_session, maxLength, out var graphDetail))
            return RerankerBatchExperimentResult.Unavailable("reranker-batch-fixed-graph", graphDetail);

        var pairs = candidates
            .Take(MaximumExperimentCandidates)
            .Select(candidate => EncodePair(query, candidate.Chunk.Content, maxLength))
            .ToArray();
        var maximumTensorBytes = checked((long)batchSize * maxLength * sizeof(long) * 3);

        var sequentialScores = new float[pairs.Length];
        var sequentialStart = Stopwatch.GetTimestamp();
        for (var index = 0; index < pairs.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            sequentialScores[index] = ScorePair(query, candidates[index].Chunk.Content, maxLength);
        }
        var sequentialDuration = Stopwatch.GetElapsedTime(sequentialStart);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var batchedScores = new float[pairs.Length];
        var batchedStart = Stopwatch.GetTimestamp();
        for (var offset = 0; offset < pairs.Length; offset += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var count = Math.Min(batchSize, pairs.Length - offset);
            var batch = pairs.AsSpan(offset, count).ToArray();
            var scores = ScoreBatch(batch);
            Array.Copy(scores, 0, batchedScores, offset, count);
        }
        var batchedDuration = Stopwatch.GetElapsedTime(batchedStart);
        var allocatedBytes = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);

        var maximumDifference = sequentialScores
            .Zip(batchedScores, (sequential, batched) => MathF.Abs(sequential - batched))
            .DefaultIfEmpty()
            .Max();
        var orderEquivalent = RankingOrder(sequentialScores).SequenceEqual(RankingOrder(batchedScores));
        var equivalent = maximumDifference <= ScoreEquivalenceTolerance && orderEquivalent;
        var benefitObserved = batchedDuration < sequentialDuration;
        var evidenceCode = equivalent
            ? benefitObserved ? "reranker-batch-equivalent-benefit" : "reranker-batch-equivalent-no-benefit"
            : "reranker-batch-equivalence-failed";
        var state = equivalent ? CapabilityState.Available : CapabilityState.Unavailable;
        var detail = $"Dynamic batch graph; {pairs.Length} pairs; max batch {batchSize}; "
            + $"score delta {maximumDifference:R}; ordering equivalent={orderEquivalent}; "
            + $"batch benefit observed={benefitObserved}; tensor working-set cap={maximumTensorBytes} bytes.";

        return new RerankerBatchExperimentResult(
            state,
            evidenceCode,
            detail,
            pairs.Length,
            batchSize,
            maxLength,
            true,
            orderEquivalent,
            maximumDifference,
            sequentialDuration,
            batchedDuration,
            allocatedBytes,
            maximumTensorBytes,
            benefitObserved);
    }

    internal static string CreateAssetIdentityKey(string modelPath, string vocabPath) =>
        $"{Path.GetFullPath(modelPath)}|{FileSignature(modelPath)}|{Path.GetFullPath(vocabPath)}|{FileSignature(vocabPath)}";

    internal static bool ShouldAttemptAssetLoad(string assetKey, string? loadedAssetKey, string? unavailableAssetKey) =>
        !string.Equals(assetKey, loadedAssetKey, StringComparison.Ordinal)
        && !string.Equals(assetKey, unavailableAssetKey, StringComparison.Ordinal);

    private static bool HasDynamicBatchGraph(InferenceSession session, int maxLength, out string detail)
    {
        foreach (var name in new[] { "input_ids", "attention_mask", "token_type_ids" })
        {
            if (!session.InputMetadata.TryGetValue(name, out var metadata)
                || !metadata.IsTensor
                || metadata.Dimensions.Length != 2
                || metadata.Dimensions[0] >= 0
                || (metadata.Dimensions[1] > 0 && metadata.Dimensions[1] != maxLength))
            {
                detail = $"The pinned ONNX graph does not expose a dynamic batch dimension for {name}.";
                return false;
            }
        }

        var output = session.OutputMetadata.Values.FirstOrDefault();
        if (output is null || !output.IsTensor || output.Dimensions.Length != 2
            || output.Dimensions[0] >= 0 || output.Dimensions[1] != 1)
        {
            detail = "The pinned ONNX graph does not expose dynamic [batch, 1] logits.";
            return false;
        }

        detail = "The pinned ONNX graph exposes dynamic batch inputs and [batch, 1] logits.";
        return true;
    }

    private static int[] RankingOrder(IReadOnlyList<float> scores) =>
        Enumerable.Range(0, scores.Count)
            .OrderByDescending(index => scores[index])
            .ThenBy(index => index)
            .ToArray();

    private static string FileSignature(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks}" : "missing";
        }
        catch
        {
            return "unreadable";
        }
    }

    private EncodedPair EncodePair(string query, string passage, int maxLength)
    {
        var tokenizer = _tokenizer!;
        var queryBudget = Math.Min(64, maxLength / 3);
        var passageBudget = Math.Max(8, maxLength - queryBudget - 3);
        var queryIds = tokenizer.EncodeToIds(query, queryBudget, addSpecialTokens: false, out _, out _).ToArray();
        var passageIds = tokenizer.EncodeToIds(passage, passageBudget, addSpecialTokens: false, out _, out _).ToArray();

        var inputIds = new long[maxLength];
        var attentionMask = new long[maxLength];
        var tokenTypeIds = new long[maxLength];
        var pos = 0;

        Add(tokenizer.ClassificationTokenId, segment: 0);
        foreach (var id in queryIds) Add(id, segment: 0);
        Add(tokenizer.SeparatorTokenId, segment: 0);
        foreach (var id in passageIds) Add(id, segment: 1);
        Add(tokenizer.SeparatorTokenId, segment: 1);

        return new EncodedPair(inputIds, attentionMask, tokenTypeIds);

        void Add(int id, int segment)
        {
            if (pos >= maxLength)
                return;

            inputIds[pos] = id;
            attentionMask[pos] = 1;
            tokenTypeIds[pos] = segment;
            pos++;
        }
    }

    private async Task DownloadIfMissingAsync(string path, string url, string expectedSha256, IProgress<string>? progress, CancellationToken ct)
    {
        if (File.Exists(path) && await VerifyFileSha256Async(path, expectedSha256, ct))
            return;

        var temp = $"{path}.download";
        progress?.Report($"Starting download: {Path.GetFileName(path)}");
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var target = File.Create(temp))
            await source.CopyToAsync(target, ct);
        if (!await VerifyFileSha256Async(temp, expectedSha256, ct))
        {
            File.Delete(temp);
            if (File.Exists(path))
                File.Delete(path);
            throw new InvalidOperationException($"{Path.GetFileName(path)} failed SHA256 verification.");
        }
        progress?.Report($"Downloaded: {Path.GetFileName(path)}");

        File.Move(temp, path, overwrite: true);
    }

    public static async Task<bool> VerifyFileSha256Async(string path, string expectedSha256, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return false;

        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveModelDirectory(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Rag.RerankerModelPath))
            return Path.GetFullPath(settings.Rag.RerankerModelPath);

        var configured = settings.DataManagement.LocalAiAssetsRoot?.Trim();
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hermaeus")
            : Path.GetFullPath(configured);
        var models = ResolveModelsDirectory(root);
        return Path.Combine(models, "rerank", "ms-marco-MiniLM-L6-v2");
    }

    private static string ResolveModelsDirectory(string root)
    {
        var candidates = new[]
        {
            Path.Combine(root, "Models"),
            Path.Combine(root, "models"),
            Path.Combine(root, "gguf")
        };

        var withGguf = candidates
            .Where(Directory.Exists)
            .Select(path => new { Path = path, Count = CountGgufFiles(path) })
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ThenBy(x => string.Equals(Path.GetFileName(x.Path), "Models", StringComparison.Ordinal) ? 0 : 1)
            .FirstOrDefault();

        return withGguf?.Path
            ?? candidates.FirstOrDefault(Directory.Exists)
            ?? Path.Combine(root, "Models");
    }

    private static int CountGgufFiles(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*.gguf", SearchOption.AllDirectories).Count();
        }
        catch
        {
            return 0;
        }
    }

    private static float Sigmoid(float value)
    {
        if (value >= 0)
        {
            var z = MathF.Exp(-value);
            return 1f / (1f + z);
        }

        var neg = MathF.Exp(value);
        return neg / (1f + neg);
    }

    public void Dispose()
    {
        ReleaseLoadedSession();
        _gate.Dispose();
        // HttpClient is static and shared; do not dispose
    }

    private void ReleaseLoadedSession()
    {
        _session?.Dispose();
        _session = null;
        _tokenizer = null;
        _loadedAssetKey = null;
        _resourceCoordinator?.ReleaseAllocation("inprocess-rag.reranker");
    }

    private sealed record EncodedPair(long[] InputIds, long[] AttentionMask, long[] TokenTypeIds);
}
