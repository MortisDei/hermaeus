using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;

namespace Hermaeus.Rag;

public record RagQueryOptions(
    int    TopK            = 5,
    bool   UseParentChild  = false,
    bool   StreamAnswer    = true,
    string ModelId         = "",
    RagGroundingMode GroundingMode = RagGroundingMode.TokenOverlap,
    int    ContextTokenBudget = 3200,
    int    MaxContextChunks   = 8,
    int    MaxChunksPerSource = 2,
    // r10 02-rag-quality.md 2.2: this is now a cosine-score floor for the
    // refusal preflight (retrieval strength), not a token-overlap ratio.
    float  RefusalThreshold   = 0.35f);

/// <summary>
/// Main entry point for RAG queries.
/// Embed query → semantic scan → BM25 → RRF fusion → parent upgrade
/// → prompt → LLM stream → grounding check.
/// </summary>
public sealed class RagQueryService
{
    private readonly SqliteRagStore    _store;
    private readonly IEmbeddingService _embed;
    private readonly ILlmService       _llm;
    private readonly ISettingsService  _settings;
    private readonly IReranker         _reranker;
    private readonly IRuntimeLogService? _logs;
    private readonly ITraceStore?      _traces;

    // In-memory chunk cache per dataset  (dataset_id → chunks)
    private const int MaxCachedDatasets = 8;
    public const long DefaultMaxCacheBytes = 128L * 1024L * 1024L;
    private readonly long _maxCacheBytes;
    // r27 2.3: the cache holds a scan index (ids + one contiguous embedding
    // block), not documents. Content, paths and titles stay in SQLite and are
    // read for the handful of chunks that survive ranking (2.5).
    private readonly Dictionary<string, RagScanIndex> _cache = [];
    private readonly Dictionary<string, long> _cacheSizes = [];
    private readonly LinkedList<string> _cacheOrder = new();
    private long _cacheBytes;
    private readonly object _cacheSync = new();

    public RagQueryService(
        SqliteRagStore store,
        IEmbeddingService embed,
        ILlmService llm,
        ISettingsService settings,
        IReranker reranker,
        IRuntimeLogService? logs = null,
        ITraceStore? traces = null,
        // r27 2.1: the cache budget is a policy of this service, not a constant
        // of the universe. Injectable so the over-budget path can be exercised
        // without a 128 MiB fixture; production always takes the default.
        long? maxCacheBytes = null)
    {
        _store = store; _embed = embed; _llm = llm; _settings = settings; _reranker = reranker; _logs = logs;
        _traces = traces;
        _maxCacheBytes = maxCacheBytes is > 0 ? maxCacheBytes.Value : DefaultMaxCacheBytes;
    }

    /// <summary>Warm the in-memory scan index for a dataset.</summary>
    public async Task WarmCacheAsync(string datasetId, CancellationToken ct = default)
        => await LoadScanIndexAsync(datasetId, ct);

    /// <summary>
    /// r27 02-retrieval-that-scales.md 2.1: reads a dataset's scan index and
    /// caches it when it fits. An index over the cache budget is still returned
    /// to the caller so the dataset can be queried; before this item,
    /// <see cref="StoreCache"/> dropped it and the query silently scanned an
    /// empty list and returned nothing, forever.
    /// </summary>
    private async Task<ScanLoad> LoadScanIndexAsync(string datasetId, CancellationToken ct)
    {
        var index = await _store.GetScanIndexAsync(datasetId, _settings.Settings.Rag.EmbeddingModel, ct);
        var cached = StoreCache(datasetId, index);
        _logs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
            cached
                ? $"RAG scan index warmed for {datasetId}: {index.Count} chunk(s), {index.ByteSize / 1024 / 1024} MiB."
                : $"RAG dataset {datasetId} is too large to cache ({index.Count} chunk(s), {index.ByteSize / 1024 / 1024} MiB over a {_maxCacheBytes / 1024 / 1024} MiB budget); queries scan from storage."));
        return new ScanLoad(index, cached);
    }

    /// <summary>A scan index, and whether it reached (or came from) the cache.</summary>
    private readonly record struct ScanLoad(RagScanIndex Index, bool Cached);

    /// <summary>
    /// r27 2.7: per-dataset scan-index state for the RAG panel, so the cache
    /// ceiling is visible before a query runs into it.
    /// </summary>
    public RagScanIndexInfo GetScanIndexInfo(string datasetId)
    {
        lock (_cacheSync)
        {
            var cached = _cache.TryGetValue(datasetId, out var index);
            return new RagScanIndexInfo(
                datasetId,
                cached,
                cached ? _cacheSizes.GetValueOrDefault(datasetId) : 0,
                cached ? index!.Count : 0,
                _maxCacheBytes);
        }
    }

    /// <summary>r27 2.7: the budget a dataset's scan index is measured against.</summary>
    public long ScanIndexBudgetBytes => _maxCacheBytes;

    public void ClearCache(string datasetId)
    {
        lock (_cacheSync)
        {
            _cache.Remove(datasetId);
            if (_cacheSizes.Remove(datasetId, out var size))
                _cacheBytes -= size;
            var node = _cacheOrder.Find(datasetId);
            if (node is not null)
                _cacheOrder.Remove(node);
        }
    }

    /// <summary>Returns true when the index fitted the budget and is now cached.</summary>
    private bool StoreCache(string datasetId, RagScanIndex index)
    {
        lock (_cacheSync)
        {
            if (_cacheSizes.Remove(datasetId, out var oldSize))
                _cacheBytes -= oldSize;
            var size = index.ByteSize;
            if (size > _maxCacheBytes)
            {
                _cache.Remove(datasetId);
                var oldNode = _cacheOrder.Find(datasetId);
                if (oldNode is not null)
                    _cacheOrder.Remove(oldNode);
                return false;
            }

            _cache[datasetId] = index;
            _cacheSizes[datasetId] = size;
            _cacheBytes += size;
            TouchCacheUnsafe(datasetId);
            return _cache.ContainsKey(datasetId);
        }
    }

    private long GetCacheBytes()
    {
        lock (_cacheSync)
            return _cacheBytes;
    }

    private void TouchCacheUnsafe(string datasetId)
    {
        // Caller must hold _cacheSync because _cache and _cacheOrder are updated together.
        var existing = _cacheOrder.Find(datasetId);
        if (existing is not null)
            _cacheOrder.Remove(existing);
        _cacheOrder.AddLast(datasetId);

        while ((_cacheOrder.Count > MaxCachedDatasets || (_cacheBytes > _maxCacheBytes && _cacheOrder.Count > 1)) && _cacheOrder.First is not null)
        {
            var oldest = _cacheOrder.First.Value;
            _cache.Remove(oldest);
            if (_cacheSizes.Remove(oldest, out var size))
                _cacheBytes -= size;
            _cacheOrder.RemoveFirst();
        }
    }

    public async Task<List<RagDataset>> GetDatasetsAsync(CancellationToken ct = default)
        => await _store.GetDatasetsAsync(ct);

    /// <summary>
    /// r21 2.3: single-dataset read seam so callers that need one dataset's
    /// name/config (chat's per-send injection) do not have to duplicate
    /// <see cref="RetrieveAsync"/>'s own internal list read. Not cached; the
    /// dataset table is tiny.
    /// </summary>
    public async Task<RagDataset?> GetDatasetAsync(string datasetId, CancellationToken ct = default)
        => (await _store.GetDatasetsAsync(ct)).FirstOrDefault(d => d.Id == datasetId);

    public async Task<List<RagDatasetGeneration>> GetGenerationHistoryAsync(
        string datasetId, CancellationToken ct = default)
        => await _store.GetGenerationHistoryAsync(datasetId, ct);

    public async Task<List<RagChunk>> GetChunksForDatasetAsync(string datasetId, bool includeEmbeddings = false, CancellationToken ct = default)
        => await _store.GetChunksAsync(datasetId, includeEmbeddings, ct);

    /// <summary>doc 03 3.1: save seam for dataset-config-only edits (adding/removing a
    /// watched source) that do not go through the ingest pipeline.</summary>
    public async Task SaveDatasetAsync(RagDataset dataset, CancellationToken ct = default)
        => await _store.SaveDatasetAsync(dataset, ct);

    /// <summary>r10 02-rag-quality.md 2.5: the lightweight projection RagDatasetHealthService actually needs.</summary>
    public async Task<List<RagChunkHealthInfo>> GetChunkHealthInfoForDatasetAsync(string datasetId, CancellationToken ct = default)
        => await _store.GetChunkHealthInfoAsync(datasetId, ct);

    public async Task DeleteDatasetAsync(string datasetId, CancellationToken ct = default)
    {
        ClearCache(datasetId);
        await _store.DeleteDatasetAsync(datasetId, ct);
        _logs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
            $"RAG dataset deleted: {datasetId}"));
    }

    /// <summary>
    /// Removes chunks belonging to source files that no longer exist on
    /// disk (r10 01-rag-correctness.md 1.5). User-clicked only: a
    /// temporarily unmounted drive must not silently shred a dataset, so
    /// this is never called from ingest or any background path.
    /// </summary>
    public async Task<int> RemoveMissingSourcesAsync(string datasetId, IReadOnlyList<string> sourcePaths, CancellationToken ct = default)
    {
        if (sourcePaths.Count == 0)
            return 0;

        var remainingCount = await _store.RemoveSourcesByPublishingGenerationAsync(datasetId, sourcePaths, ct);

        ClearCache(datasetId);
        _logs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
            $"RAG removed {sourcePaths.Count} missing source(s) from dataset {datasetId}; {remainingCount} chunk(s) remain."));

        return remainingCount;
    }

    public async Task<RagRetrievalResult> RetrieveAsync(
        string datasetId,
        string question,
        RagQueryOptions? opts = null,
        CancellationToken ct = default,
        string? operationId = null)
    {
        opts ??= new RagQueryOptions();
        operationId ??= OperationCorrelation.NewId();
        var sw = Stopwatch.StartNew();
        var ds = (await _store.GetDatasetsAsync(ct)).FirstOrDefault(d => d.Id == datasetId);
        var currentGenerationId = ds?.CurrentGenerationId ?? string.Empty;

        // r27 2.1: "absent from the cache" and "cached and genuinely empty" are
        // different states. Only the first one needs a load, and a dataset that
        // does not fit the budget is scanned from the loaded list rather than
        // from a cache entry that was never written.
        RagScanIndex scanIndex;
        bool cacheHit;
        lock (_cacheSync)
        {
            cacheHit = _cache.TryGetValue(datasetId, out scanIndex!)
                && string.Equals(scanIndex.GenerationId, currentGenerationId, StringComparison.Ordinal);
            if (!cacheHit)
            {
                _cache.Remove(datasetId);
                if (_cacheSizes.Remove(datasetId, out var staleSize))
                    _cacheBytes -= staleSize;
                var staleNode = _cacheOrder.Find(datasetId);
                if (staleNode is not null)
                    _cacheOrder.Remove(staleNode);
            }
            if (cacheHit)
                TouchCacheUnsafe(datasetId);
        }

        var scannedUncached = false;
        if (!cacheHit)
        {
            var loaded = await LoadScanIndexAsync(datasetId, ct);
            scanIndex = loaded.Index;
            scannedUncached = !loaded.Cached;
        }

        var plan = await BuildQueryPlanAsync(datasetId, question, ct);

        // read dataset config to obtain hybrid retriever weights and check the embedding model
        // r10 01-rag-correctness.md 1.4: a dataset embedded with one model
        // queried under a different current model produces either a raw
        // exception (mismatched dimensions) or silent garbage rankings
        // (same dimensions, different model). Skip the semantic scan
        // entirely and fall back to BM25-only rather than either.
        var currentEmbeddingModel = _settings.Settings.Rag.EmbeddingModel;
        var embeddingModelMismatch = !string.IsNullOrWhiteSpace(ds?.Config.EmbeddingModel)
            && !string.IsNullOrWhiteSpace(currentEmbeddingModel)
            && !string.Equals(ds!.Config.EmbeddingModel, currentEmbeddingModel, StringComparison.OrdinalIgnoreCase);

        var semanticK = Math.Max(opts.TopK * 10, 50);
        List<ScoredChunkId> semanticIds = [];
        var plannerNotes = plan.PlannerNotes;
        if (scannedUncached)
        {
            plannerNotes = AppendNote(plannerNotes,
                $"dataset too large to cache ({scanIndex.Count} chunk(s), budget {_maxCacheBytes / 1024 / 1024} MiB); queries scan from storage and will be slower");
        }

        if (embeddingModelMismatch)
        {
            var note = $"semantic search skipped: dataset embedded with {ds!.Config.EmbeddingModel}, current model is {currentEmbeddingModel}; reindex to re-enable";
            plannerNotes = AppendNote(plannerNotes, note);
        }
        else
        {
            try
            {
                var qEmbed = await _embed.EmbedAsync(plan.PrimaryQuery, ct);
                // r27 2.4: one pass over the contiguous block with a bounded
                // min-heap, instead of allocating a ScoredChunk per chunk and
                // sorting the whole corpus to select fifty of them.
                semanticIds = HybridRetriever.CosineScan(qEmbed, scanIndex, semanticK);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // r21 2.1: an unreachable/stopped embedding server must degrade
                // to keyword-only search, not kill the query. Never cached -
                // the next query probes again in case the server came back.
                var oneLine = ex.Message.Replace('\r', ' ').Replace('\n', ' ');
                var note = $"semantic search unavailable: {oneLine}; used keyword search only";
                plannerNotes = AppendNote(plannerNotes, note);
                _logs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Rag,
                    $"RAG semantic search failed for dataset {datasetId}: {ex.GetType().Name}; keyword fallback selected.", operationId));
            }
        }

        // r27 2.2: BM25 candidates come from the FTS index rather than from
        // tokenising every chunk in the dataset once per query variant. FTS5
        // generates candidates; Bm25Scorer still scores them, so ranking among
        // the chunks that matter does not move. The only chunks that stop being
        // scored are ones FTS5 did not match at all, which share no query term
        // and therefore scored essentially zero.
        var bm25Stats = await _store.GetBm25StatsAsync(datasetId, ct);
        var candidates = await LoadBm25CandidatesAsync(datasetId, plan.QueryVariants, ct);
        List<ScoredChunk> bm25 = [];
        if (bm25Stats is not null && candidates.Count > 0)
        {
            bm25 = ScoreQueryVariants(plan.QueryVariants, candidates, bm25Stats)
                .Take(semanticK)
                .ToList();
        }

        // r27 2.5: content is loaded for the ids that survived scanning, in one
        // query, not for the corpus. Everything downstream of fusion (parent
        // upgrade, the reranker, the context packer, citations, the trace) still
        // reads Chunk.Content and is untouched by this.
        var semantic = await AttachContentAsync(semanticIds, candidates, ct);

        var topFuse = Math.Max(opts.TopK * 2, opts.TopK);
        var fused = HybridRetriever.Fuse(
            plan.PrimaryQuery,
            semantic,
            bm25,
            topFuse,
            ds?.Config.HybridSemanticWeight ?? 0.7f,
            ds?.Config.HybridBm25Weight ?? 0.3f,
            ds?.Config.HybridRrfK ?? 60f);
        var preRerankCount = fused.Count;
        fused = await _reranker.RerankAsync(plan.PrimaryQuery, fused, opts.TopK, ct);
        if (opts.UseParentChild)
            fused = await UpgradeToParentsAsync(fused, ct);

        sw.Stop();
        _logs?.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            RuntimeLogLevel.Info,
            RuntimeLogCategory.Rag,
            $"RAG retrieval completed; dataset={datasetId}, semantic={semantic.Count}, keyword={bm25.Count}, pre_rerank={preRerankCount}, selected={fused.Count}, cache_hit={cacheHit}, latency_ms={sw.ElapsedMilliseconds}.",
            operationId));
        return new RagRetrievalResult(question, plan.PrimaryQuery, plan.QueryVariants, plannerNotes, semantic, bm25, fused, sw.ElapsedMilliseconds, ds?.Config);
    }

    public IAsyncEnumerable<RagStreamEvent> StreamQueryAsync(
        string datasetId,
        string question,
        RagQueryOptions? opts = null,
        CancellationToken ct = default) =>
        StreamQueryCoreAsync([datasetId], question, opts, ct);

    /// <summary>
    /// Queries several explicitly selected datasets as one grounded context.
    /// Retrieval remains isolated per dataset, then the bounded results are
    /// merged using their retrieval ranking. This preserves each dataset's
    /// embedding/configuration checks and avoids silently querying datasets
    /// the user did not select.
    /// </summary>
    public IAsyncEnumerable<RagStreamEvent> StreamQueryAsync(
        IReadOnlyList<string> datasetIds,
        string question,
        RagQueryOptions? opts = null,
        CancellationToken ct = default) =>
        StreamQueryCoreAsync(datasetIds, question, opts, ct);

    private async Task<RagRetrievalResult> RetrieveManyAsync(
        IReadOnlyList<string> datasetIds,
        string question,
        RagQueryOptions opts,
        CancellationToken ct,
        string operationId)
    {
        var ids = datasetIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
            throw new InvalidOperationException("At least one RAG dataset must be selected.");
        if (ids.Length == 1)
            return await RetrieveAsync(ids[0], question, opts, ct, operationId);

        var retrievals = new List<RagRetrievalResult>(ids.Length);
        foreach (var id in ids)
            retrievals.Add(await RetrieveAsync(id, question, opts, ct, operationId));

        var semantic = retrievals.SelectMany(result => result.SemanticCandidates)
            .GroupBy(scored => ChunkKey(scored.Chunk), StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(scored => scored.Score).First())
            .OrderByDescending(scored => scored.Score)
            .Take(Math.Max(opts.TopK * 10, 50))
            .ToList();
        var bm25 = retrievals.SelectMany(result => result.Bm25Candidates)
            .GroupBy(scored => ChunkKey(scored.Chunk), StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(scored => scored.Score).First())
            .OrderByDescending(scored => scored.Score)
            .Take(Math.Max(opts.TopK * 10, 50))
            .ToList();
        var selected = retrievals.SelectMany(result => result.Selected)
            .GroupBy(scored => ChunkKey(scored.Chunk), StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(scored => scored.Score).First())
            .OrderByDescending(scored => scored.Score)
            .Take(opts.TopK)
            .ToList();

        return new RagRetrievalResult(
            question,
            retrievals[0].ExpandedQuery,
            retrievals.SelectMany(result => result.QueryVariants).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            string.Join("; ", retrievals.Where(result => !string.IsNullOrWhiteSpace(result.PlannerNotes)).Select(result => result.PlannerNotes)),
            semantic,
            bm25,
            selected,
            retrievals.Sum(result => result.LatencyMs),
            DatasetConfig: null);
    }

    private async IAsyncEnumerable<RagStreamEvent> StreamQueryCoreAsync(
        IReadOnlyList<string> datasetIds,
        string question,
        RagQueryOptions? opts = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        opts ??= new RagQueryOptions();
        var operationId = OperationCorrelation.NewId();
        _logs?.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            RuntimeLogLevel.Info,
            RuntimeLogCategory.Rag,
            $"RAG query started; datasets={string.Join(',', datasetIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))}, configured_model={(string.IsNullOrWhiteSpace(opts.ModelId) ? _settings.Settings.Llm.DefaultModel : opts.ModelId)}.",
            operationId));
        var totalSw = Stopwatch.StartNew();
        var retrievalSw = Stopwatch.StartNew();

        var retrieval = await RetrieveManyAsync(datasetIds, question, opts, ct, operationId);
        var traceDatasetId = string.Join(",", datasetIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal));
        var semantic = retrieval.SemanticCandidates;
        var bm25 = retrieval.Bm25Candidates;
        var fused = retrieval.Selected;
        var expandedQuery = retrieval.ExpandedQuery;
        retrievalSw.Stop();

        var semanticById = semantic.GroupBy(s => ChunkKey(s.Chunk)).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var bm25ById = bm25.GroupBy(s => ChunkKey(s.Chunk)).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var queryTerms = Bm25Scorer.Tokenize(retrieval.ExpandedQuery);

        // ── 8. Build context + prompt ────────────────────────────────────
        var contextPack = BuildContext(fused, opts);
        var context = contextPack.Text;
        _logs?.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            RuntimeLogLevel.Info,
            RuntimeLogCategory.Rag,
            $"RAG context prepared; datasets={datasetIds.Count}, semantic={semantic.Count}, keyword={bm25.Count}, merged={fused.Count}, packed={contextPack.PackedChunks.Count}, dropped={Math.Max(0, fused.Count - contextPack.PackedChunks.Count)}, retrieval_ms={retrievalSw.ElapsedMilliseconds}.",
            operationId));
        var prompt  = BuildPrompt(question, context, retrieval.DatasetConfig);
        var modelId = string.IsNullOrEmpty(opts.ModelId)
            ? _settings.Settings.Llm.DefaultModel
            : opts.ModelId;

        // Yield a structured event so consumers can bind sources without
        // parsing the answer stream themselves; computed before the refusal
        // check too so a refusal still shows the closest sources instead of
        // a bare sentence (r10 02-rag-quality.md 2.2).
        var sourceChunks = fused.Select((r, i) => ToTraceChunk(r, i + 1, fused.Count, semanticById, bm25ById, queryTerms)).ToList();

        var bestSemanticScore = semantic.Count > 0 ? semantic.Max(s => s.Score) : 0f;
        var bestBm25Score = bm25.Count > 0 ? bm25.Max(s => s.Score) : 0f;
        var shouldRefuse = WouldRefuse(bestSemanticScore, bestBm25Score, opts.RefusalThreshold);

        if (shouldRefuse)
        {
            yield return RagStreamEvent.ForSources(sourceChunks);

            var reason = sourceChunks.Count == 0
                ? "the dataset has no retrievable chunks"
                : $"the best semantic score ({bestSemanticScore:F3}) was below the {opts.RefusalThreshold:F3} confidence threshold and no keyword matched either";
            var refusal = sourceChunks.Count > 0
                ? $"I do not have enough grounded context to answer that reliably. The closest sources are shown above, but I did not trust them: {reason}."
                : "I do not have enough grounded context to answer that reliably.";
            yield return RagStreamEvent.ForToken(refusal);

            totalSw.Stop();
            var refusalTrace = new RagQueryTrace
            {
                OperationId = operationId,
                DatasetId = traceDatasetId,
                Question = question,
                ExpandedQuestion = expandedQuery,
                QueryVariants = retrieval.QueryVariants,
                PlannerNotes = retrieval.PlannerNotes,
                ModelId = modelId,
                RetrievalLatencyMs = retrievalSw.ElapsedMilliseconds,
                TotalLatencyMs = totalSw.ElapsedMilliseconds,
                GroundingMode = opts.GroundingMode,
                GroundingScore = bestSemanticScore,
                Refused = true,
                RefusalReason = reason,
                ContextTokenBudget = opts.ContextTokenBudget,
                ContextPackingSummary = contextPack.Summary,
                RetrievedChunks = semantic.Select((r, i) => ToTraceChunk(r, i + 1, semantic.Count, semanticById, bm25ById, queryTerms)).ToList(),
                SelectedContext = sourceChunks
            };
            await PersistTraceAsync(refusalTrace, ct);
            _logs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
                "RAG query completed with a grounded-context refusal.", operationId));
            yield return RagStreamEvent.ForTrace(new RagTraceSummary(
                refusalTrace.Id,
                refusalTrace.RetrievalLatencyMs,
                refusalTrace.TotalLatencyMs,
                refusalTrace.GroundingScore,
                refusalTrace.GroundingMode.ToString(),
                ExpandedQuery: refusalTrace.ExpandedQuestion,
                QueryVariants: string.Join("\n", refusalTrace.QueryVariants),
                PlannerNotes: refusalTrace.PlannerNotes,
                ContextPackingSummary: refusalTrace.ContextPackingSummary,
                Refused: refusalTrace.Refused,
                RefusalReason: refusalTrace.RefusalReason,
                DatasetId: refusalTrace.DatasetId));
            yield break;
        }

        yield return RagStreamEvent.ForSources(sourceChunks);

        // ── 9. Stream LLM answer ─────────────────────────────────────────
        var answer = new StringBuilder();
        if (string.IsNullOrWhiteSpace(modelId))
        {
            // An empty model id used to be handed to the composite LLM service
            // anyway. It cannot resolve a provider for "", so the user got
            // "Could not determine which provider serves model ''. Refresh the
            // model list and try again." - which names the wrong problem and
            // gives advice that cannot fix it, because refreshing does not set
            // a default model. Reported against a fresh dataset on an install
            // where Llm.DefaultModel had never been set.
            //
            // Deliberately placed here rather than earlier: retrieval really
            // did run, the sources above are real, and the trace below is still
            // worth recording. Only the answer is missing.
            const string noModel =
                "No chat model is selected, so there is nothing to write the answer with. "
                + "The sources above are real, and the retrieval ran normally. Pick a model in Chat, "
                + "or set a default under Settings > LLM, then ask again.";
            answer.Append(noModel);
            yield return RagStreamEvent.ForToken(noModel);
        }
        else
        {
            await foreach (var token in _llm.StreamChatTextAsync(
                modelId,
                [new ChatMessage("user", prompt)],
                ct: ct))
            {
                answer.Append(token);
                yield return RagStreamEvent.ForToken(token);
            }
        }

        totalSw.Stop();
        var answerText = answer.ToString();
        var trace = new RagQueryTrace
        {
            OperationId = operationId,
            DatasetId = traceDatasetId,
            Question = question,
            ExpandedQuestion = expandedQuery,
            QueryVariants = retrieval.QueryVariants,
            PlannerNotes = retrieval.PlannerNotes,
            ModelId = modelId,
            RetrievalLatencyMs = retrievalSw.ElapsedMilliseconds,
            TotalLatencyMs = totalSw.ElapsedMilliseconds,
            GroundingMode = opts.GroundingMode,
            GroundingScore = ComputeGroundingScore(answerText, context, opts.GroundingMode),
            Refused = false,
            RefusalReason = string.Empty,
            ContextTokenBudget = opts.ContextTokenBudget,
            ContextPackingSummary = contextPack.Summary,
            RetrievedChunks = semantic.Select((r, i) => ToTraceChunk(r, i + 1, semantic.Count, semanticById, bm25ById, queryTerms)).ToList(),
            SelectedContext = fused.Select((r, i) => ToTraceChunk(r, i + 1, fused.Count, semanticById, bm25ById, queryTerms)).ToList()
        };
        await PersistTraceAsync(trace, ct);
        _logs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
            $"RAG query completed; refused=false, total_ms={totalSw.ElapsedMilliseconds}, citations={trace.SelectedContext.Count}.", operationId));
        yield return RagStreamEvent.ForTrace(new RagTraceSummary(
            trace.Id,
            trace.RetrievalLatencyMs,
            trace.TotalLatencyMs,
            trace.GroundingScore,
            trace.GroundingMode.ToString(),
            ExpandedQuery: trace.ExpandedQuestion,
            QueryVariants: string.Join("\n", trace.QueryVariants),
            PlannerNotes: trace.PlannerNotes,
            ContextPackingSummary: trace.ContextPackingSummary,
            DatasetId: trace.DatasetId));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// r27 2.2: at most a few hundred chunk ids per query, matched by FTS5
    /// across every query variant, then read once with their content. This is
    /// the candidate set BM25 scores.
    /// </summary>
    private const int Bm25CandidateCap = 400;

    private async Task<List<RagChunk>> LoadBm25CandidatesAsync(
        string datasetId, IReadOnlyList<string> variants, CancellationToken ct)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in variants.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            foreach (var id in await _store.SearchChunkIdsAsync(datasetId, variant, Bm25CandidateCap, ct))
            {
                if (ids.Count >= Bm25CandidateCap)
                    break;
                ids.Add(id);
            }
        }

        return ids.Count == 0 ? [] : await _store.GetChunksByIdsAsync(ids, ct);
    }

    /// <summary>
    /// r27 2.5: turns scored ids into scored chunks, reusing content already
    /// read for the BM25 candidate set and fetching only what is genuinely
    /// missing. An id whose row has since been deleted is dropped rather than
    /// carried forward as an empty chunk.
    /// </summary>
    private async Task<List<ScoredChunk>> AttachContentAsync(
        IReadOnlyList<ScoredChunkId> scored, IReadOnlyList<RagChunk> alreadyLoaded, CancellationToken ct)
    {
        if (scored.Count == 0)
            return [];

        var byId = alreadyLoaded.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var missing = scored.Where(s => !byId.ContainsKey(s.ChunkId)).Select(s => s.ChunkId).ToHashSet(StringComparer.Ordinal);
        if (missing.Count > 0)
        {
            foreach (var chunk in await _store.GetChunksByIdsAsync(missing, ct))
                byId[chunk.Id] = chunk;
        }

        var result = new List<ScoredChunk>(scored.Count);
        foreach (var entry in scored)
        {
            if (byId.TryGetValue(entry.ChunkId, out var chunk))
                result.Add(new ScoredChunk(chunk, entry.Score, ScoreSource.Semantic));
        }

        return result;
    }

    /// <summary>Appends one planner note to the running list, keeping the existing "; " separator.</summary>
    private static string AppendNote(string notes, string note) =>
        string.IsNullOrWhiteSpace(notes) ? note : $"{notes}; {note}";

    private async Task PersistTraceAsync(RagQueryTrace trace, CancellationToken ct)
    {
        if (_traces is null)
            return;

        try
        {
            await _traces.AppendAsync(new TraceRecord
            {
                Id = trace.Id,
                Kind = TraceKind.Rag,
                CreatedAt = trace.CreatedAt,
                SourceId = trace.DatasetId,
                ModelId = trace.ModelId,
                Operation = $"{(trace.Refused ? "rag-refusal" : "rag-query")}:{trace.OperationId}",
                TotalLatencyMs = trace.TotalLatencyMs,
                Error = trace.RefusalReason,
                DetailJson = JsonSerializer.Serialize(trace)
            }, ct);
        }
        catch (Exception ex)
        {
            _logs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Rag,
                $"RAG trace persistence failed: {ex.Message}", trace.OperationId));
        }
    }

    private async Task<QueryPlan> BuildQueryPlanAsync(string datasetId, string question, CancellationToken ct)
    {
        try
        {
            var ds = (await _store.GetDatasetsAsync(ct)).FirstOrDefault(d => d.Id == datasetId);
            var aliasPlan = await BuildAliasExpansionAsync(question, ds?.Config.AliasFilePath, ct);
            var variants = BuildQueryVariants(question, aliasPlan.ExpandedQuery, aliasPlan.AliasTerms);
            return new QueryPlan(aliasPlan.ExpandedQuery, variants, aliasPlan.Notes);
        }
        catch
        {
            var fallback = NormalizeQuery(question);
            return new QueryPlan(fallback, BuildQueryVariants(fallback, fallback, []), string.Empty);
        }
    }

    private async Task<AliasExpansionPlan> BuildAliasExpansionAsync(string question, string? aliasPath, CancellationToken ct)
    {
        var expanded = NormalizeQuery(question);
        var aliasTerms = new List<string>();
        var notes = new List<string>();

        try
        {
            if (aliasPath is { Length: > 0 } path && File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path, ct);
                var aliases = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                if (aliases is not null)
                {
                    foreach (var (term, expansions) in aliases)
                    {
                        if (!question.Contains(term, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var clean = expansions.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        if (clean.Count == 0)
                            continue;

                        aliasTerms.Add(term);
                        expanded = string.Join(' ', new[] { expanded }.Concat(clean)).Trim();
                        notes.Add($"Alias {term} -> {string.Join(", ", clean)}");
                    }
                }
            }
        }
        catch
        {
            // best-effort only
        }

        return new AliasExpansionPlan(expanded, aliasTerms, notes.Count == 0 ? string.Empty : string.Join("; ", notes));
    }

    private static List<string> BuildQueryVariants(string original, string expanded, IReadOnlyCollection<string> aliasTerms)
    {
        var variants = new List<string>
        {
            NormalizeQuery(expanded),
            NormalizeQuery(original)
        };

        var keywordVariant = BuildKeywordQuery(original, aliasTerms);
        if (!string.IsNullOrWhiteSpace(keywordVariant))
            variants.Add(keywordVariant);

        return variants
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

    private static string BuildKeywordQuery(string question, IReadOnlyCollection<string> aliasTerms)
    {
        var tokens = Bm25Scorer.Tokenize(question)
            .Where(t => t.Length >= 4)
            .Where(t => !aliasTerms.Contains(t, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        return tokens.Count == 0 ? string.Empty : string.Join(' ', tokens);
    }

    private static string NormalizeQuery(string query) => Regex.Replace(query ?? string.Empty, "\\s+", " ").Trim();

    private static List<ScoredChunk> ScoreQueryVariants(IEnumerable<string> variants, IReadOnlyList<RagChunk> chunks, Bm25Stats stats)
    {
        var scorer = new Bm25Scorer();
        var best = new Dictionary<string, ScoredChunk>(StringComparer.Ordinal);

        // r10 02-rag-quality.md 2.4: tokenize every candidate's content once
        // per query, not once per variant (up to 3x on the same corpus).
        var tfIndex = Bm25Scorer.BuildTfIndex(chunks);

        foreach (var variant in variants.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            foreach (var scored in scorer.Score(variant, chunks, stats, tfIndex))
            {
                if (best.TryGetValue(scored.Chunk.Id, out var existing))
                {
                    if (scored.Score > existing.Score)
                        best[scored.Chunk.Id] = scored;
                }
                else
                {
                    best[scored.Chunk.Id] = scored;
                }
            }
        }

        return best.Values.OrderByDescending(s => s.Score).ToList();
    }

    private async Task<List<ScoredChunk>> UpgradeToParentsAsync(
        List<ScoredChunk> fused, CancellationToken ct)
    {
        var upgraded = new List<ScoredChunk>();
        var seen = new HashSet<string>();

        foreach (var scored in fused)
        {
            var chunk = scored.Chunk;
            if (!string.IsNullOrWhiteSpace(chunk.ParentId))
                chunk = await _store.GetParentChunkAsync(chunk.ParentId, ct) ?? chunk;

            if (!seen.Add(chunk.Id)) continue;
            upgraded.Add(scored with { Chunk = chunk });
        }

        return upgraded;
    }

    private static RagContextPack BuildContext(IReadOnlyList<ScoredChunk> chunks, RagQueryOptions opts)
    {
        var candidates = chunks.Select(scored => new ContextPart(
            "rag-chunk",
            scored.Chunk.SourceTitle,
            scored.Chunk.Content,
            GroupKey: string.IsNullOrWhiteSpace(scored.Chunk.SourcePath) ? scored.Chunk.SourceFile : scored.Chunk.SourcePath,
            Tokens: Math.Max(scored.Chunk.TokenCount, 1),
            Data: scored.Chunk)).ToList();

        var packed = ContextPackBuilder.Pack(
            candidates,
            opts.ContextTokenBudget,
            maxParts: opts.MaxContextChunks,
            maxPerGroup: opts.MaxChunksPerSource);

        var sb = new StringBuilder();
        var rank = 0;
        var packedChunks = new List<RagContextPackedChunk>();
        foreach (var part in packed.Parts)
        {
            if (part.Data is RagChunk chunk)
            {
                AppendContextChunk(sb, ++rank, chunk, part.Content, part.Truncated);
                packedChunks.Add(new RagContextPackedChunk(chunk, part.Content, part.Truncated));
            }
        }

        return new RagContextPack(sb.ToString().Trim(), packed.Summary, packedChunks);
    }

    /// <summary>
    /// r21 1.4: public seam so chat's per-turn Knowledge injection reuses the
    /// exact same budget-aware, per-source-capped packing
    /// <see cref="StreamQueryAsync"/> uses instead of reimplementing it. The
    /// returned <see cref="RagContextPack.PackedChunks"/> is the list that
    /// actually survived packing (post budget cuts), so citation pills match
    /// what was truly injected, not the pre-pack candidate list.
    /// </summary>
    public RagContextPack BuildContextPack(IReadOnlyList<ScoredChunk> selected, RagQueryOptions opts) => BuildContext(selected, opts);

    private static void AppendContextChunk(StringBuilder sb, int rank, RagChunk chunk, string content, bool truncated)
    {
        if (sb.Length > 0)
            sb.AppendLine("\n---");

        sb.AppendLine($"[{rank}] Source: {chunk.SourceTitle}");
        if (!string.IsNullOrWhiteSpace(chunk.HeadingPath))
            sb.AppendLine($"Heading: {chunk.HeadingPath}");
        if (!string.IsNullOrWhiteSpace(chunk.CodeSymbolInfo))
            sb.AppendLine($"Symbol: {chunk.CodeSymbolInfo}");
        if (chunk.PageNumber.HasValue)
            sb.AppendLine($"Page: {chunk.PageNumber.Value}");
        if (!string.IsNullOrWhiteSpace(chunk.EventType))
            sb.AppendLine($"Event: {chunk.EventType}");
        if (truncated)
            sb.AppendLine("Note: truncated to respect context budget");
        sb.AppendLine(content.Trim());
    }

    private static string BuildPrompt(string question, string context, RagDatasetConfig? config)
    {
        var template = config?.PromptTemplate?.Trim();
        if (string.IsNullOrWhiteSpace(template))
            return BuildDefaultPrompt(question, context);

        var hasContext = template.Contains("{context}", StringComparison.OrdinalIgnoreCase);
        var hasQuestion = template.Contains("{question}", StringComparison.OrdinalIgnoreCase);
        if (!hasContext || !hasQuestion)
            return BuildDefaultPrompt(question, context);

        return template
            .Replace("{context}", context, StringComparison.OrdinalIgnoreCase)
            .Replace("{question}", question, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildDefaultPrompt(string question, string context) =>
        $"You are a helpful assistant. Answer the question using ONLY the information " +
        $"in the provided context. If the context does not contain enough information " +
        $"to answer clearly, say so. Do not invent information not present in the context.\n\n" +
        $"Context:\n{context}\n\n" +
        $"Question: {question}\n\nAnswer:";

    /// <summary>
    /// r10 02-rag-quality.md 2.2/2.6: the refusal preflight gate, based on
    /// retrieval strength rather than question/context token overlap.
    /// Refuse only when nothing matched either way: the best semantic
    /// candidate's cosine score is below <paramref name="refusalThreshold"/>
    /// AND no BM25 candidate matched any term at all. Shared by
    /// StreamQueryAsync and RagEvalService's retrieval-only mode so both
    /// evaluate the exact same gate.
    /// </summary>
    public static bool WouldRefuse(float bestSemanticScore, float bestBm25Score, float refusalThreshold) =>
        bestSemanticScore < refusalThreshold && bestBm25Score <= 0f;

    public static bool WouldRefuse(IReadOnlyList<ScoredChunk> semantic, IReadOnlyList<ScoredChunk> bm25, float refusalThreshold) =>
        WouldRefuse(
            semantic.Count > 0 ? semantic.Max(s => s.Score) : 0f,
            bm25.Count > 0 ? bm25.Max(s => s.Score) : 0f,
            refusalThreshold);

    public static float GroundingScore(string answer, string context)
        => ComputeGroundingScore(answer, context, RagGroundingMode.TokenOverlap);

    /// <summary>
    /// Post-answer grounding (answer vs context token overlap) is a
    /// legitimate check regardless of mode; collapsed to one path since
    /// SemanticPlaceholder never had a distinct implementation
    /// (r10 02-rag-quality.md 2.2). Kept as a public single-path method so
    /// existing callers keep compiling.
    /// </summary>
    public static float ComputeGroundingScore(string answer, string context, RagGroundingMode mode)
        => ScoreTokenOverlap(answer, context);

    private static float ScoreTokenOverlap(string answer, string context)
    {
        if (string.IsNullOrWhiteSpace(answer)) return 0f;
        var answerTokens = Bm25Scorer.Tokenize(answer).ToHashSet();
        var contextTokens = Bm25Scorer.Tokenize(context).ToHashSet();
        if (answerTokens.Count == 0) return 0f;
        return (float)answerTokens.Count(t => contextTokens.Contains(t)) / answerTokens.Count;
    }

    /// <summary>
    /// Builds a trace chunk with the per-signal breakdown ("why did
    /// retrieval choose this chunk", r6 01-first-five-minutes.md 1.6):
    /// vector score from the semantic candidate list, keyword score from
    /// the BM25 candidate list (both null if this chunk id is not present
    /// there, e.g. a parent-upgraded chunk), and rerank score only when the
    /// final scored chunk's source is actually <see cref="ScoreSource.Reranker"/>
    /// (the reranker leaves the source untouched when it is disabled).
    /// </summary>
    private static RagTraceChunk ToTraceChunk(
        ScoredChunk scored,
        int rank,
        int outOf,
        IReadOnlyDictionary<string, ScoredChunk> semanticById,
        IReadOnlyDictionary<string, ScoredChunk> bm25ById,
        IReadOnlyCollection<string> queryTerms)
    {
        var id = scored.Chunk.Id;
        var key = ChunkKey(scored.Chunk);
        var (matchedTerm, matchedCount) = FindDominantMatchedTerm(scored.Chunk.Content, queryTerms);

        return new RagTraceChunk
        {
            Rank = rank,
            OutOfCount = outOf,
            ChunkId = id,
            Title = scored.Chunk.SourceTitle,
            File = scored.Chunk.SourceFile,
            Path = scored.Chunk.SourcePath,
            SourceId = scored.Chunk.SourceId,
            SourceRevisionId = scored.Chunk.SourceRevisionId,
            ContentHash = scored.Chunk.SourceHash,
            GenerationId = scored.Chunk.GenerationId,
            Score = scored.Score,
            Content = scored.Chunk.Content,
            VectorScore = semanticById.TryGetValue(key, out var sem) ? sem.Score : null,
            KeywordScore = bm25ById.TryGetValue(key, out var kw) ? kw.Score : null,
            RerankScore = scored.Source == ScoreSource.Reranker ? scored.Score : null,
            MatchedTerm = matchedTerm,
            MatchedTermCount = matchedCount
        };
    }

    private static string ChunkKey(RagChunk chunk) =>
        $"{chunk.DatasetId}\u001f{chunk.Id}";

    /// <summary>The query term that appears most often in the chunk, for the "term 'x' matched N times" summary phrase.</summary>
    private static (string Term, int Count) FindDominantMatchedTerm(string content, IReadOnlyCollection<string> queryTerms)
    {
        var bestTerm = string.Empty;
        var bestCount = 0;
        foreach (var term in queryTerms)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Length < 2) continue;
            var count = CountOccurrences(content, term);
            if (count > bestCount)
            {
                bestCount = count;
                bestTerm = term;
            }
        }

        return (bestTerm, bestCount);
    }

    private static int CountOccurrences(string content, string term)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += term.Length;
        }

        return count;
    }
}

public sealed record RagRetrievalResult(
    string Question,
    string ExpandedQuery,
    List<string> QueryVariants,
    string PlannerNotes,
    List<ScoredChunk> SemanticCandidates,
    List<ScoredChunk> Bm25Candidates,
    List<ScoredChunk> Selected,
    long LatencyMs,
    RagDatasetConfig? DatasetConfig);

/// <summary>
/// r27 2.7: the scan-index state of one dataset, so the RAG panel can show the
/// cache ceiling as a fact rather than the user discovering it as a slow query.
/// </summary>
public sealed record RagScanIndexInfo(
    string DatasetId,
    bool Cached,
    long IndexBytes,
    int ChunkCount,
    long BudgetBytes);

internal sealed record QueryPlan(string PrimaryQuery, List<string> QueryVariants, string PlannerNotes);

internal sealed record AliasExpansionPlan(string ExpandedQuery, List<string> AliasTerms, string Notes);

public sealed record RagContextPack(string Text, string Summary, IReadOnlyList<RagContextPackedChunk> PackedChunks);

public sealed record RagContextPackedChunk(RagChunk Chunk, string Content, bool Truncated);
