using System.Text.RegularExpressions;
using Aether.Rag.Models;

namespace Aether.Rag.Retrieval;

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
    {
        if (stats.TotalDocuments == 0 || candidates.Count == 0)
            return candidates.Select(c => new ScoredChunk(c, 0f, ScoreSource.Bm25)).ToList();

        var queryTerms = Tokenize(query);
        var avgDl      = stats.AverageDocumentLength;

        return candidates
            .Select(chunk =>
            {
                var tf    = ComputeTf(chunk.Content);
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
                return new ScoredChunk(chunk, score, ScoreSource.Bm25);
            })
            .OrderByDescending(s => s.Score)
            .ToList();
    }

    /// <summary>
    /// Compute global document frequencies from the full corpus.
    /// Called once during index build and cached in SQLite.
    /// </summary>
    public static Bm25Stats BuildStats(IReadOnlyList<RagChunk> allChunks)
    {
        var df    = new Dictionary<string, int>();
        long totalLen = 0;

        foreach (var chunk in allChunks)
        {
            var terms = Tokenize(chunk.Content).ToHashSet();
            totalLen += terms.Sum(t => t.Length);  // rough length proxy
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

    private static Dictionary<string, int> ComputeTf(string text)
    {
        var tf = new Dictionary<string, int>();
        foreach (var t in TokenizeAll(text))
            tf[t] = tf.GetValueOrDefault(t) + 1;
        return tf;
    }
}
