using System.Text;
using System.Text.RegularExpressions;
using Aether.Rag.Models;

namespace Aether.Rag.Chunking;

/// <summary>
/// Splits documents into overlapping paragraph-based chunks.
/// Optionally produces parent-child pairs: small child chunks are indexed;
/// larger parent chunks are delivered to the LLM for richer context.
/// </summary>
public sealed class ParagraphChunker
{
    // Approx 4 chars per token for English text
    private const int CharsPerToken = 4;

    private static readonly Regex ParaSplit =
        new(@"\n\s*\n", RegexOptions.Compiled);

    private static readonly Regex SentenceEnd =
        new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    public List<TextChunk> Chunk(
        string text,
        string sourceFile,
        string sourceTitle,
        RagDatasetConfig cfg)
    {
        var paragraphs = ParaSplit
            .Split(text)
            .Select(p => p.Trim())
            .Where(p => p.Length >= 20)
            .ToList();

        return cfg.UseParentChild
            ? ChunkParentChild(paragraphs, sourceFile, sourceTitle, cfg)
            : ChunkFlat(paragraphs, sourceFile, sourceTitle, cfg);
    }

    // ── Flat chunking ────────────────────────────────────────────────────────

    private static List<TextChunk> ChunkFlat(
        List<string> paras, string file, string title, RagDatasetConfig cfg)
    {
        var chunks = new List<string>();
        var buffer = new StringBuilder();

        foreach (var para in paras)
        {
            if (buffer.Length > 0 && buffer.Length + para.Length > cfg.TargetChunkChars)
            {
                chunks.Add(buffer.ToString().Trim());
                // Overlap: keep last few sentences that fit in overlap window
                var overlap = GetOverlapSuffix(buffer.ToString(), cfg.OverlapChars);
                buffer.Clear();
                if (!string.IsNullOrEmpty(overlap))
                    buffer.Append(overlap).Append("\n\n");
            }
            if (buffer.Length > 0) buffer.Append("\n\n");
            buffer.Append(para);
        }
        if (buffer.Length > 0) chunks.Add(buffer.ToString().Trim());

        return chunks
            .Select((c, i) => new TextChunk(c, file, title, i, chunks.Count))
            .ToList();
    }

    // ── Parent-child chunking ────────────────────────────────────────────────

    private static List<TextChunk> ChunkParentChild(
        List<string> paras, string file, string title, RagDatasetConfig cfg)
    {
        // Build parent chunks first (larger context windows)
        var parents = BuildChunks(paras, cfg.ParentChunkChars, cfg.OverlapChars);

        var result = new List<TextChunk>();
        int childIdx = 0;
        int total = parents.Sum(p =>
            BuildChunks(SplitSentences(p), cfg.TargetChunkChars, cfg.OverlapChars / 2).Count);

        foreach (var parent in parents)
        {
            // Build smaller child chunks within this parent
            var sentences = SplitSentences(parent);
            var children  = BuildChunks(sentences, cfg.TargetChunkChars, cfg.OverlapChars / 2);
            foreach (var child in children)
            {
                result.Add(new TextChunk(child, file, title, childIdx++, total, parent));
            }
        }

        return result;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static List<string> BuildChunks(List<string> units, int targetChars, int overlapChars)
    {
        var chunks = new List<string>();
        var buf = new StringBuilder();

        foreach (var unit in units)
        {
            if (buf.Length > 0 && buf.Length + unit.Length > targetChars)
            {
                chunks.Add(buf.ToString().Trim());
                var overlap = GetOverlapSuffix(buf.ToString(), overlapChars);
                buf.Clear();
                if (!string.IsNullOrEmpty(overlap))
                    buf.Append(overlap).Append(' ');
            }
            if (buf.Length > 0) buf.Append(' ');
            buf.Append(unit);
        }
        if (buf.Length > 0) chunks.Add(buf.ToString().Trim());
        return chunks;
    }

    private static List<string> SplitSentences(string text) =>
        SentenceEnd.Split(text)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

    private static string GetOverlapSuffix(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        var sentences = SentenceEnd.Split(text);
        var buf = new StringBuilder();
        foreach (var s in sentences.Reverse())
        {
            if (buf.Length + s.Length > maxChars) break;
            buf.Insert(0, s + " ");
        }
        return buf.ToString().Trim();
    }

    public static int EstimateTokens(string text) => text.Length / CharsPerToken;
}
