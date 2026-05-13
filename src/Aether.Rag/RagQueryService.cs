using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
    RagGroundingMode GroundingMode = RagGroundingMode.TokenOverlap);

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

    // In-memory chunk cache per dataset  (dataset_id → chunks)
    private const int MaxCachedDatasets = 8;
    private readonly Dictionary<string, List<RagChunk>> _cache = [];
    private readonly Queue<string> _cacheOrder = [];

    public RagQueryService(
        SqliteRagStore store,
        IEmbeddingService embed,
        ILlmService llm,
        ISettingsService settings,
        IReranker reranker)
    {
        _store = store; _embed = embed; _llm = llm; _settings = settings; _reranker = reranker;
    }

    /// <summary>Warm the in-memory embedding cache for a dataset.</summary>
    public async Task WarmCacheAsync(string datasetId, CancellationToken ct = default)
    {
        var chunks = await _store.GetChunksAsync(datasetId, includeEmbeddings: true, ct);
        StoreCache(datasetId, chunks);
    }

    public void ClearCache(string datasetId)
    {
        _cache.Remove(datasetId);
        if (_cacheOrder.Count == 0)
            return;

        var remaining = new Queue<string>();
        while (_cacheOrder.Count > 0)
        {
            var current = _cacheOrder.Dequeue();
            if (!string.Equals(current, datasetId, StringComparison.OrdinalIgnoreCase))
                remaining.Enqueue(current);
        }

        while (remaining.Count > 0)
            _cacheOrder.Enqueue(remaining.Dequeue());
    }

    private void StoreCache(string datasetId, List<RagChunk> chunks)
    {
        _cache[datasetId] = chunks;
        TouchCache(datasetId);

        while (_cacheOrder.Count > MaxCachedDatasets)
        {
            var oldest = _cacheOrder.Dequeue();
            _cache.Remove(oldest);
        }
    }

    private void TouchCache(string datasetId)
    {
        if (_cacheOrder.Count == 0)
        {
            _cacheOrder.Enqueue(datasetId);
            return;
        }

        var entries = new Queue<string>();
        var touched = false;
        while (_cacheOrder.Count > 0)
        {
            var current = _cacheOrder.Dequeue();
            if (string.Equals(current, datasetId, StringComparison.OrdinalIgnoreCase))
            {
                touched = true;
                continue;
            }
            entries.Enqueue(current);
        }

        while (entries.Count > 0)
            _cacheOrder.Enqueue(entries.Dequeue());

        _cacheOrder.Enqueue(datasetId);
        if (!touched && _cacheOrder.Count > MaxCachedDatasets)
        {
            var oldest = _cacheOrder.Dequeue();
            if (!string.Equals(oldest, datasetId, StringComparison.OrdinalIgnoreCase))
                _cache.Remove(oldest);
        }
    }

    public async Task<List<RagDataset>> GetDatasetsAsync(CancellationToken ct = default)
        => await _store.GetDatasetsAsync(ct);

    public async Task<RagRetrievalResult> RetrieveAsync(
        string datasetId,
        string question,
        RagQueryOptions? opts = null,
        CancellationToken ct = default)
    {
        opts ??= new RagQueryOptions();
        var sw = Stopwatch.StartNew();

        if (!_cache.TryGetValue(datasetId, out var chunks) || chunks.Count == 0)
            await WarmCacheAsync(datasetId, ct);
        chunks = _cache.GetValueOrDefault(datasetId, []);
        TouchCache(datasetId);

        var expandedQuery = await ExpandQueryAsync(datasetId, question, ct);
        var qEmbed = await _embed.EmbedAsync(expandedQuery, ct);
        var semanticK = Math.Max(opts.TopK * 10, 50);
        var semantic = HybridRetriever.CosineScan(qEmbed, chunks, semanticK);

        var bm25Stats = await _store.GetBm25StatsAsync(datasetId, ct);
        List<ScoredChunk> bm25 = [];
        if (bm25Stats is not null)
        {
            var scorer = new Bm25Scorer();
            bm25 = scorer.Score(expandedQuery, chunks, bm25Stats)
                .Take(semanticK)
                .ToList();
        }

        var fused = HybridRetriever.Fuse(semantic, bm25, Math.Max(opts.TopK * 2, opts.TopK));
        fused = await _reranker.RerankAsync(expandedQuery, fused, opts.TopK, ct);
        if (opts.UseParentChild)
            fused = await UpgradeToParentsAsync(fused, ct);

        sw.Stop();
        return new RagRetrievalResult(question, expandedQuery, semantic, bm25, fused, sw.ElapsedMilliseconds);
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
        var context = BuildContext(fused);
        var prompt  = BuildPrompt(question, context);
        var modelId = string.IsNullOrEmpty(opts.ModelId)
            ? _settings.Settings.Llm.DefaultModel
            : opts.ModelId;

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
        await foreach (var token in _llm.StreamChatAsync(
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
            ModelId = modelId,
            RetrievalLatencyMs = retrievalSw.ElapsedMilliseconds,
            TotalLatencyMs = totalSw.ElapsedMilliseconds,
            GroundingMode = opts.GroundingMode,
            GroundingScore = ComputeGroundingScore(answerText, context, opts.GroundingMode),
            RetrievedChunks = semantic.Select((r, i) => ToTraceChunk(r, i + 1)).ToList(),
            SelectedContext = fused.Select((r, i) => ToTraceChunk(r, i + 1)).ToList()
        };
        await _store.SaveRagQueryTraceAsync(trace, ct);
        yield return $"__RAG_TRACE__{JsonSerializer.Serialize(new { trace.Id, trace.RetrievalLatencyMs, trace.TotalLatencyMs, trace.GroundingScore, mode = trace.GroundingMode.ToString() })}__END_TRACE__";
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<string> ExpandQueryAsync(string datasetId, string question, CancellationToken ct)
    {
        try
        {
            var ds = (await _store.GetDatasetsAsync(ct)).FirstOrDefault(d => d.Id == datasetId);
            if (ds?.Config.AliasFilePath is { Length: > 0 } path && File.Exists(path))
            {
                var json    = await File.ReadAllTextAsync(path, ct);
                var aliases = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                if (aliases is null) return question;

                var expansion = new StringBuilder(question);
                foreach (var (term, expansions) in aliases)
                {
                    if (question.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        expansion.Append(' ');
                        expansion.AppendJoin(' ', expansions);
                    }
                }
                return expansion.ToString();
            }
        }
        catch { /* alias expansion is best-effort */ }
        return question;
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

    private static string BuildContext(IReadOnlyList<ScoredChunk> chunks)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < chunks.Count; i++)
        {
            sb.Append($"[{i + 1}] Source: {chunks[i].Chunk.SourceTitle}\n");
            sb.AppendLine(chunks[i].Chunk.Content);
            if (i < chunks.Count - 1) sb.AppendLine("\n---");
        }
        return sb.ToString();
    }

    private static string BuildPrompt(string question, string context) =>
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
    List<ScoredChunk> SemanticCandidates,
    List<ScoredChunk> Bm25Candidates,
    List<ScoredChunk> Selected,
    long LatencyMs);
