using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Rag.Embeddings;
using Aether.Rag.Models;
using Aether.Rag.Retrieval;
using Aether.Rag.Storage;

namespace Aether.Rag;

public record RagQueryOptions(
    int    TopK            = 5,
    bool   UseParentChild  = false,
    bool   StreamAnswer    = true,
    string ModelId         = "",
    RagGroundingMode GroundingMode = RagGroundingMode.TokenOverlap,
    int    ContextTokenBudget = 3200,
    int    MaxContextChunks   = 8,
    int    MaxChunksPerSource = 2,
    float  RefusalThreshold   = 0.08f);

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
    private const long MaxCacheBytes = 128L * 1024L * 1024L;
    private readonly Dictionary<string, List<RagChunk>> _cache = [];
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
        ITraceStore? traces = null)
    {
        _store = store; _embed = embed; _llm = llm; _settings = settings; _reranker = reranker; _logs = logs;
        _traces = traces;
    }

    /// <summary>Warm the in-memory embedding cache for a dataset.</summary>
    public async Task WarmCacheAsync(string datasetId, CancellationToken ct = default)
    {
        var chunks = await _store.GetChunksAsync(datasetId, includeEmbeddings: true, ct);
        StoreCache(datasetId, chunks);
        _logs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
            $"RAG cache warmed for {datasetId}: {chunks.Count} chunk(s), {GetCacheBytes() / 1024 / 1024} MiB cached."));
    }

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

    private void StoreCache(string datasetId, List<RagChunk> chunks)
    {
        lock (_cacheSync)
        {
            if (_cacheSizes.Remove(datasetId, out var oldSize))
                _cacheBytes -= oldSize;
            var size = EstimateCacheSize(chunks);
            if (size > MaxCacheBytes)
            {
                _cache.Remove(datasetId);
                var oldNode = _cacheOrder.Find(datasetId);
                if (oldNode is not null)
                    _cacheOrder.Remove(oldNode);
                return;
            }

            _cache[datasetId] = chunks;
            _cacheSizes[datasetId] = size;
            _cacheBytes += size;
            TouchCacheUnsafe(datasetId);
        }
    }

    private long GetCacheBytes()
    {
        lock (_cacheSync)
            return _cacheBytes;
    }

    private void TouchCache(string datasetId)
    {
        lock (_cacheSync)
            TouchCacheUnsafe(datasetId);
    }

    private void TouchCacheUnsafe(string datasetId)
    {
        // Caller must hold _cacheSync because _cache and _cacheOrder are updated together.
        var existing = _cacheOrder.Find(datasetId);
        if (existing is not null)
            _cacheOrder.Remove(existing);
        _cacheOrder.AddLast(datasetId);

        while ((_cacheOrder.Count > MaxCachedDatasets || (_cacheBytes > MaxCacheBytes && _cacheOrder.Count > 1)) && _cacheOrder.First is not null)
        {
            var oldest = _cacheOrder.First.Value;
            _cache.Remove(oldest);
            if (_cacheSizes.Remove(oldest, out var size))
                _cacheBytes -= size;
            _cacheOrder.RemoveFirst();
        }
    }

    private static long EstimateCacheSize(IEnumerable<RagChunk> chunks) =>
        chunks.Sum(chunk =>
            (long)chunk.Content.Length * sizeof(char)
            + (long)chunk.SourceFile.Length * sizeof(char)
            + (long)chunk.SourcePath.Length * sizeof(char)
            + (long)chunk.SourceTitle.Length * sizeof(char)
            + (long)chunk.Embedding.Length * sizeof(float)
            + 256);

    public async Task<List<RagDataset>> GetDatasetsAsync(CancellationToken ct = default)
        => await _store.GetDatasetsAsync(ct);

    public async Task<List<RagChunk>> GetChunksForDatasetAsync(string datasetId, bool includeEmbeddings = false, CancellationToken ct = default)
        => await _store.GetChunksAsync(datasetId, includeEmbeddings, ct);

    public async Task DeleteDatasetAsync(string datasetId, CancellationToken ct = default)
    {
        ClearCache(datasetId);
        await _store.DeleteDatasetAsync(datasetId, ct);
        _logs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Info, RuntimeLogCategory.Rag,
            $"RAG dataset deleted: {datasetId}"));
    }

    public async Task<RagRetrievalResult> RetrieveAsync(
        string datasetId,
        string question,
        RagQueryOptions? opts = null,
        CancellationToken ct = default)
    {
        opts ??= new RagQueryOptions();
        var sw = Stopwatch.StartNew();

        List<RagChunk> chunks;
        lock (_cacheSync)
        {
            _cache.TryGetValue(datasetId, out chunks!);
        }

        if (chunks is null || chunks.Count == 0)
            await WarmCacheAsync(datasetId, ct);

        lock (_cacheSync)
        {
            chunks = _cache.GetValueOrDefault(datasetId, []);
            TouchCacheUnsafe(datasetId);
        }

        var plan = await BuildQueryPlanAsync(datasetId, question, ct);
        var qEmbed = await _embed.EmbedAsync(plan.PrimaryQuery, ct);
        var semanticK = Math.Max(opts.TopK * 10, 50);
        var semantic = HybridRetriever.CosineScan(qEmbed, chunks, semanticK);

        var bm25Stats = await _store.GetBm25StatsAsync(datasetId, ct);
        List<ScoredChunk> bm25 = [];
        if (bm25Stats is not null)
        {
            bm25 = ScoreQueryVariants(plan.QueryVariants, chunks, bm25Stats)
                .Take(semanticK)
                .ToList();
        }

        // read dataset config to obtain hybrid retriever weights
        var ds = (await _store.GetDatasetsAsync(ct)).FirstOrDefault(d => d.Id == datasetId);
        var topFuse = Math.Max(opts.TopK * 2, opts.TopK);
        var fused = HybridRetriever.Fuse(
            plan.PrimaryQuery,
            semantic,
            bm25,
            topFuse,
            ds?.Config.HybridSemanticWeight ?? 0.7f,
            ds?.Config.HybridBm25Weight ?? 0.3f,
            ds?.Config.HybridRrfK ?? 60f);
        fused = await _reranker.RerankAsync(plan.PrimaryQuery, fused, opts.TopK, ct);
        if (opts.UseParentChild)
            fused = await UpgradeToParentsAsync(fused, ct);

        sw.Stop();
        return new RagRetrievalResult(question, plan.PrimaryQuery, plan.QueryVariants, plan.PlannerNotes, semantic, bm25, fused, sw.ElapsedMilliseconds, ds?.Config);
    }

    public async IAsyncEnumerable<string> StreamQueryAsync(
        string datasetId,
        string question,
        RagQueryOptions? opts = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        opts ??= new RagQueryOptions();
        var totalSw = Stopwatch.StartNew();
        var retrievalSw = Stopwatch.StartNew();

        var retrieval = await RetrieveAsync(datasetId, question, opts, ct);
        var semantic = retrieval.SemanticCandidates;
        var fused = retrieval.Selected;
        var expandedQuery = retrieval.ExpandedQuery;
        retrievalSw.Stop();

        // ── 8. Build context + prompt ────────────────────────────────────
        var contextPack = BuildContext(fused, opts);
        var context = contextPack.Text;
        var prompt  = BuildPrompt(question, context, retrieval.DatasetConfig);
        var modelId = string.IsNullOrEmpty(opts.ModelId)
            ? _settings.Settings.Llm.DefaultModel
            : opts.ModelId;

        var preflightGrounding = ComputeGroundingScore(question, context, opts.GroundingMode);
        if (preflightGrounding < opts.RefusalThreshold)
        {
            var refusal = "I do not have enough grounded context to answer that reliably.";
            yield return refusal;

            totalSw.Stop();
            var refusalTrace = new RagQueryTrace
            {
                DatasetId = datasetId,
                Question = question,
                ExpandedQuestion = expandedQuery,
                QueryVariants = retrieval.QueryVariants,
                PlannerNotes = retrieval.PlannerNotes,
                ModelId = modelId,
                RetrievalLatencyMs = retrievalSw.ElapsedMilliseconds,
                TotalLatencyMs = totalSw.ElapsedMilliseconds,
                GroundingMode = opts.GroundingMode,
                GroundingScore = preflightGrounding,
                Refused = true,
                RefusalReason = $"Preflight grounding {preflightGrounding:F3} below threshold {opts.RefusalThreshold:F3}",
                ContextTokenBudget = opts.ContextTokenBudget,
                ContextPackingSummary = contextPack.Summary,
                RetrievedChunks = semantic.Select((r, i) => ToTraceChunk(r, i + 1)).ToList(),
                SelectedContext = fused.Select((r, i) => ToTraceChunk(r, i + 1)).ToList()
            };
            await PersistTraceAsync(refusalTrace, ct);
            yield return $"__RAG_TRACE__{JsonSerializer.Serialize(new { refusalTrace.Id, refusalTrace.RetrievalLatencyMs, refusalTrace.TotalLatencyMs, refusalTrace.GroundingScore, refusalTrace.Refused, refusalTrace.RefusalReason, mode = refusalTrace.GroundingMode.ToString() })}__END_TRACE__";
            yield break;
        }

        // Yield a structured header so the UI can parse sources
        var sourcesJson = JsonSerializer.Serialize(fused.Select((r, i) => new
        {
            rank  = i + 1,
            title = r.Chunk.SourceTitle,
            file  = r.Chunk.SourceFile,
            path  = r.Chunk.SourcePath,
            score = MathF.Round(r.Score, 4),
            content = r.Chunk.Content
        }));
        yield return $"__RAG_SOURCES__{sourcesJson}__END_SOURCES__";

        // ── 9. Stream LLM answer ─────────────────────────────────────────
        var answer = new StringBuilder();
        await foreach (var token in _llm.StreamChatTextAsync(
            modelId,
            [new ChatMessage("user", prompt)],
            ct: ct))
        {
            answer.Append(token);
            yield return token;
        }

        totalSw.Stop();
        var answerText = answer.ToString();
        var trace = new RagQueryTrace
        {
            DatasetId = datasetId,
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
            RetrievedChunks = semantic.Select((r, i) => ToTraceChunk(r, i + 1)).ToList(),
            SelectedContext = fused.Select((r, i) => ToTraceChunk(r, i + 1)).ToList()
        };
        await PersistTraceAsync(trace, ct);
        yield return $"__RAG_TRACE__{JsonSerializer.Serialize(new { trace.Id, trace.RetrievalLatencyMs, trace.TotalLatencyMs, trace.GroundingScore, mode = trace.GroundingMode.ToString() })}__END_TRACE__";
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
                Operation = trace.Refused ? "rag-refusal" : "rag-query",
                TotalLatencyMs = trace.TotalLatencyMs,
                Error = trace.RefusalReason,
                DetailJson = JsonSerializer.Serialize(trace)
            }, ct);
        }
        catch (Exception ex)
        {
            _logs?.Add(new RuntimeLogEntry(DateTime.UtcNow, RuntimeLogLevel.Warning, RuntimeLogCategory.Rag,
                $"RAG trace persistence failed: {ex.Message}"));
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

    private static List<ScoredChunk> ScoreQueryVariants(IEnumerable<string> variants, List<RagChunk> chunks, Bm25Stats stats)
    {
        var scorer = new Bm25Scorer();
        var best = new Dictionary<string, ScoredChunk>(StringComparer.Ordinal);

        foreach (var variant in variants.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            foreach (var scored in scorer.Score(variant, chunks, stats))
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

    private static ContextPack BuildContext(IReadOnlyList<ScoredChunk> chunks, RagQueryOptions opts)
    {
        var budget = Math.Max(opts.ContextTokenBudget, 128);
        var maxChunks = Math.Max(opts.MaxContextChunks, 1);
        var perSourceLimit = Math.Max(opts.MaxChunksPerSource, 1);

        var sb = new StringBuilder();
        var usedTokens = 0;
        var usedChunks = 0;
        var sourceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();

        foreach (var scored in chunks)
        {
            if (usedChunks >= maxChunks)
            {
                notes.Add($"chunk limit {maxChunks} reached");
                break;
            }

            var chunk = scored.Chunk;
            var sourceKey = string.IsNullOrWhiteSpace(chunk.SourcePath) ? chunk.SourceFile : chunk.SourcePath;
            sourceCounts.TryGetValue(sourceKey, out var sourceCount);
            if (sourceCount >= perSourceLimit)
                continue;

            var tokenCount = Math.Max(chunk.TokenCount, 1);
            var remaining = budget - usedTokens;
            if (remaining <= 0)
            {
                notes.Add($"budget {budget} tokens exhausted");
                break;
            }

            if (tokenCount > remaining)
            {
                var truncated = TruncateContent(chunk.Content, remaining * 4);
                if (string.IsNullOrWhiteSpace(truncated))
                    continue;

                AppendContextChunk(sb, usedChunks + 1, chunk, truncated, truncated: true);
                usedTokens = budget;
                usedChunks++;
                notes.Add($"truncated {chunk.SourceTitle} to fit budget");
                break;
            }

            AppendContextChunk(sb, usedChunks + 1, chunk, chunk.Content, truncated: false);
            usedTokens += tokenCount;
            usedChunks++;
            sourceCounts[sourceKey] = sourceCount + 1;
        }

        if (usedChunks == 0 && chunks.Count > 0)
        {
            var fallback = chunks[0].Chunk;
            var snippet = TruncateContent(fallback.Content, Math.Max(budget * 4, 512));
            AppendContextChunk(sb, 1, fallback, snippet, truncated: true);
            usedChunks = 1;
            usedTokens = Math.Min(budget, Math.Max(fallback.TokenCount, 1));
            notes.Add("fallback chunk used because budget selection was empty");
        }

        return new ContextPack(
            sb.ToString().Trim(),
            usedTokens,
            usedChunks,
            string.Join("; ", notes));
    }

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

    private static string TruncateContent(string content, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(content) || maxChars <= 0)
            return string.Empty;

        return content.Length <= maxChars
            ? content.Trim()
            : content[..maxChars].TrimEnd() + "...";
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

    public static float GroundingScore(string answer, string context)
        => ComputeGroundingScore(answer, context, RagGroundingMode.TokenOverlap);

    public static float ComputeGroundingScore(string answer, string context, RagGroundingMode mode)
        => mode == RagGroundingMode.SemanticPlaceholder
            ? ScoreTokenOverlap(answer, context)
            : ScoreTokenOverlap(answer, context);

    private static float ScoreTokenOverlap(string answer, string context)
    {
        if (string.IsNullOrWhiteSpace(answer)) return 0f;
        var answerTokens = Bm25Scorer.Tokenize(answer).ToHashSet();
        var contextTokens = Bm25Scorer.Tokenize(context).ToHashSet();
        if (answerTokens.Count == 0) return 0f;
        return (float)answerTokens.Count(t => contextTokens.Contains(t)) / answerTokens.Count;
    }

    private static RagTraceChunk ToTraceChunk(ScoredChunk scored, int rank) => new()
    {
        Rank = rank,
        ChunkId = scored.Chunk.Id,
        Title = scored.Chunk.SourceTitle,
        File = scored.Chunk.SourceFile,
        Path = scored.Chunk.SourcePath,
        Score = scored.Score,
        Content = scored.Chunk.Content
    };
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

internal sealed record QueryPlan(string PrimaryQuery, List<string> QueryVariants, string PlannerNotes);

internal sealed record AliasExpansionPlan(string ExpandedQuery, List<string> AliasTerms, string Notes);

internal sealed record ContextPack(string Text, int TokensUsed, int ChunksUsed, string Summary);
