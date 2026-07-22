using System.Text.RegularExpressions;
using Hermaeus.Rag.Models;

namespace Hermaeus.Rag.Retrieval;

/// <summary>
/// BM25 Okapi scoring. k1=1.5, b=0.75 (standard values).
/// Global stats (DF, corpus size) are precomputed at index time.
/// Per-document TF is computed on-the-fly for candidate chunks.
/// </summary>
public sealed class Bm25Scorer
{
    private const float K1 = 1.5f;
    private const float B  = 0.75f;

    private static readonly Regex TokenRe = new(@"[a-z0-9]+", RegexOptions.Compiled);

    public List<ScoredChunk> Score(
        string query,
        IReadOnlyList<RagChunk> candidates,
        Bm25Stats stats)
        => Score(query, candidates, stats, BuildTfIndex(candidates));

    /// <summary>
    /// r10 02-rag-quality.md 2.4: scoring several query variants against the
    /// same candidate set used to re-tokenize every chunk's full content
    /// once per variant (up to 3x per query). Callers scoring multiple
    /// variants should build the TF index once with <see cref="BuildTfIndex"/>
    /// and reuse it across calls.
    /// </summary>
    public List<ScoredChunk> Score(
        string query,
        IReadOnlyList<RagChunk> candidates,
        Bm25Stats stats,
        IReadOnlyDictionary<string, Dictionary<string, int>> tfByChunkId)
    {
        if (stats.TotalDocuments == 0 || candidates.Count == 0)
            return candidates.Select(c => new ScoredChunk(c, 0f, ScoreSource.Bm25)).ToList();

        var queryTerms = Tokenize(query);
        var avgDl      = stats.AverageDocumentLength;

        return candidates
            .Select(chunk =>
            {
                var tf = tfByChunkId.TryGetValue(chunk.Id, out var precomputed) ? precomputed : ComputeTf(chunk.Content);
                var docLen = tf.Values.Sum();
                var score = 0f;

                foreach (var term in queryTerms)
                {
                    if (!stats.DocumentFrequencies.TryGetValue(term, out var df)) continue;
                    var f   = tf.GetValueOrDefault(term, 0);
                    var idf = MathF.Log((stats.TotalDocuments - df + 0.5f) / (df + 0.5f) + 1f);
                    var tfn = f * (K1 + 1f) / (f + K1 * (1f - B + B * docLen / avgDl));
                    score  += idf * tfn;
                }

                // r10 02-rag-quality.md 2.3: the metadata boost that used to
                // live here added the same 0.008-0.020 constants to raw BM25
                // scores of 1-10, making it a no-op noise term. The
                // structural signal belongs in one place: HybridRetriever's
                // proportional fusion boost.
                return new ScoredChunk(chunk, score, ScoreSource.Bm25);
            })
            .OrderByDescending(s => s.Score)
            .ToList();
    }

    /// <summary>Tokenizes every candidate's content exactly once; reuse across query variants instead of recomputing per variant.</summary>
    public static Dictionary<string, Dictionary<string, int>> BuildTfIndex(IReadOnlyList<RagChunk> candidates) =>
        candidates.ToDictionary(c => c.Id, c => ComputeTf(c.Content));

    /// <summary>
    /// Compute global document frequencies from the full corpus.
    /// Called once during index build and cached in SQLite.
    /// </summary>
    public static Bm25Stats BuildStats(IReadOnlyList<RagChunk> allChunks)
    {
        var df = new Dictionary<string, int>();

        foreach (var chunk in allChunks)
        {
            var terms = Tokenize(chunk.Content).ToHashSet();
            foreach (var t in terms)
                df[t] = df.GetValueOrDefault(t) + 1;
        }

        // More accurate: use word count as doc length
        long totalWords = allChunks.Sum(c => (long)TokenizeAll(c.Content).Count);

        return new Bm25Stats
        {
            TotalDocuments        = allChunks.Count,
            AverageDocumentLength = allChunks.Count > 0
                ? (float)totalWords / allChunks.Count
                : 0f,
            DocumentFrequencies = df
        };
    }

    public static List<string> Tokenize(string text) =>
        TokenRe.Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(t => t.Length > 1)
            .Distinct()
            .ToList();

    private static List<string> TokenizeAll(string text) =>
        TokenRe.Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(t => t.Length > 1)
            .ToList();

    /// <summary>
    /// Test-only instrumentation (r10 02-rag-quality.md 2.4): counts how many
    /// times a chunk's content is actually tokenized, so a test can prove
    /// each candidate is tokenized at most once per query regardless of how
    /// many query variants are scored.
    /// </summary>
    internal static int TfComputations;

    private static Dictionary<string, int> ComputeTf(string text)
    {
        TfComputations++;
        var tf = new Dictionary<string, int>();
        foreach (var t in TokenizeAll(text))
            tf[t] = tf.GetValueOrDefault(t) + 1;
        return tf;
    }

}
