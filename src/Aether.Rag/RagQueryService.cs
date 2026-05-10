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
    string ModelId         = "");

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

    // In-memory chunk cache per dataset  (dataset_id → chunks)
    private readonly Dictionary<string, List<RagChunk>> _cache = [];

    public RagQueryService(
        SqliteRagStore store,
        IEmbeddingService embed,
        ILlmService llm,
        ISettingsService settings)
    {
        _store = store; _embed = embed; _llm = llm; _settings = settings;
    }

    /// <summary>Warm the in-memory embedding cache for a dataset.</summary>
    public async Task WarmCacheAsync(string datasetId, CancellationToken ct = default)
    {
        var chunks = await _store.GetChunksAsync(datasetId, includeEmbeddings: true, ct);
        _cache[datasetId] = chunks;
    }

    public void ClearCache(string datasetId) => _cache.Remove(datasetId);

    public async Task<List<RagDataset>> GetDatasetsAsync(CancellationToken ct = default)
        => await _store.GetDatasetsAsync(ct);

    public async IAsyncEnumerable<string> StreamQueryAsync(
        string datasetId,
        string question,
        RagQueryOptions? opts = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        opts ??= new RagQueryOptions();
        var sw = Stopwatch.StartNew();

        // ── 1. Ensure cache ───────────────────────────────────────────────
        if (!_cache.TryGetValue(datasetId, out var chunks) || chunks.Count == 0)
            await WarmCacheAsync(datasetId, ct);
        chunks = _cache.GetValueOrDefault(datasetId, []);

        // ── 2. Query expansion (alias map) ────────────────────────────────
        var expandedQuery = await ExpandQueryAsync(datasetId, question, ct);

        // ── 3. Embed query ────────────────────────────────────────────────
        var qEmbed = await _embed.EmbedAsync(expandedQuery, ct);

        // ── 4. Semantic scan (cosine) ─────────────────────────────────────
        var semanticK   = Math.Max(opts.TopK * 10, 50);
        var semantic    = HybridRetriever.CosineScan(qEmbed, chunks, semanticK);

        // ── 5. BM25 on semantic candidates ───────────────────────────────
        var bm25Stats   = await _store.GetBm25StatsAsync(datasetId, ct);
        List<ScoredChunk> bm25 = [];
        if (bm25Stats is not null)
        {
            var scorer = new Bm25Scorer();
            bm25 = scorer.Score(expandedQuery, chunks, bm25Stats)
                .Take(semanticK)
                .ToList();
        }

        // ── 6. RRF fusion ────────────────────────────────────────────────
        var fused = HybridRetriever.Fuse(semantic, bm25, opts.TopK);

        // ── 7. Parent upgrade (parent-child mode) ────────────────────────
        if (opts.UseParentChild)
            fused = await UpgradeToParentsAsync(fused, ct);

        sw.Stop();

        // ── 8. Build context + prompt ────────────────────────────────────
        var context = BuildContext(fused);
        var prompt  = BuildPrompt(question, context);
        var modelId = string.IsNullOrEmpty(opts.ModelId)
            ? _settings.Settings.DefaultModel
            : opts.ModelId;

        // Yield a structured header so the UI can parse sources
        var sourcesJson = JsonSerializer.Serialize(fused.Select((r, i) => new
        {
            rank  = i + 1,
            title = r.Chunk.SourceTitle,
            file  = r.Chunk.SourceFile,
            score = MathF.Round(r.Score, 4)
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
    {
        if (string.IsNullOrWhiteSpace(answer)) return 0f;
        var answerTokens  = Bm25Scorer.Tokenize(answer).ToHashSet();
        var contextTokens = Bm25Scorer.Tokenize(context).ToHashSet();
        if (answerTokens.Count == 0) return 0f;
        return (float)answerTokens.Count(t => contextTokens.Contains(t)) / answerTokens.Count;
    }
}
