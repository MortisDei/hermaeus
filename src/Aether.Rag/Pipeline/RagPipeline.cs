using Aether.Rag.Chunking;
using Aether.Rag.Embeddings;
using Aether.Rag.Models;
using Aether.Rag.Retrieval;
using Aether.Rag.Storage;
using System.Security.Cryptography;

namespace Aether.Rag.Pipeline;

public record IngestProgress(string Stage, int Done, int Total, string Detail = "");

/// <summary>
/// Orchestrates the full ingest pipeline:
/// Load → Chunk → Embed (batched) → Store → BM25 stats
/// </summary>
public sealed class RagPipeline
{
    private readonly SqliteRagStore   _store;
    private readonly IEmbeddingService _embed;
    private readonly ParagraphChunker  _chunker = new();

    private const int EmbedBatchSize = 10;

    public RagPipeline(SqliteRagStore store, IEmbeddingService embed)
    {
        _store = store;
        _embed = embed;
    }

    public async Task IngestDirectoryAsync(
        RagDataset dataset,
        string directory,
        IProgress<IngestProgress>? progress = null,
        CancellationToken ct = default)
    {
        ValidateIngestConfig(dataset.Config);
        if (dataset.Config.ExtractionMode is not RagExtractionMode.TextMarkdown)
            throw new NotSupportedException($"{dataset.Config.ExtractionMode} is configured but not yet implemented. The provider slot is ready; install a concrete extractor before ingesting this profile.");

        var files = Directory.GetFiles(directory, "*.txt", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(directory, "*.md", SearchOption.AllDirectories))
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
            throw new InvalidOperationException($"No .txt or .md files found in {directory}");

        progress?.Report(new IngestProgress("Chunking", 0, files.Count, $"Found {files.Count} files"));

        // ── 1. Chunk all files ────────────────────────────────────────────
        var allChunks = new List<RagChunk>();
        var parentChunks = new List<RagChunk>(); // parent-child mode
        var changedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var health = new RagIngestHealth { FileCount = files.Count };

        for (int fi = 0; fi < files.Count; fi++)
        {
            ct.ThrowIfCancellationRequested();
            var file  = files[fi];
            var title = Path.GetFileNameWithoutExtension(file);
            var text  = await File.ReadAllTextAsync(file, ct);
            var sourcePath = Path.GetFullPath(file);
            var sourceHash = ComputeHash(text);
            var modifiedUtc = File.GetLastWriteTimeUtc(file);

            if (new FileInfo(file).Length > 50 * 1024 * 1024)
            {
                health.OversizedFileCount++;
                health.Warnings.Add($"Large file: {sourcePath}");
            }

            var textChunks = _chunker.Chunk(text, file, title, dataset.Config);
            changedSourcePaths.Add(sourcePath);

            foreach (var tc in textChunks)
            {
                if (string.IsNullOrWhiteSpace(tc.Content))
                    health.EmptyChunkCount++;

                var chunk = new RagChunk
                {
                    DatasetId   = dataset.Id,
                    SourceFile  = Path.GetFileName(file),
                    SourcePath  = sourcePath,
                    SourceHash  = sourceHash,
                    SourceModifiedUtc = modifiedUtc,
                    SourceTitle = title,
                    Content     = tc.Content,
                    ChunkIndex  = tc.Index,
                    ChunkTotal  = tc.Total,
                    TokenCount  = ParagraphChunker.EstimateTokens(tc.Content)
                };

                if (tc.ParentContent is not null)
                {
                    // Parent chunk (stored but not embedded for indexing)
                    var parentId = Guid.NewGuid().ToString();
                    var parent = new RagChunk
                    {
                        Id          = parentId,
                        DatasetId   = dataset.Id,
                        SourceFile  = chunk.SourceFile,
                        SourcePath  = chunk.SourcePath,
                        SourceHash  = chunk.SourceHash,
                        SourceModifiedUtc = chunk.SourceModifiedUtc,
                        SourceTitle = chunk.SourceTitle,
                        Content     = tc.ParentContent,
                        ChunkIndex  = tc.Index,
                        ChunkTotal  = tc.Total,
                        TokenCount  = ParagraphChunker.EstimateTokens(tc.ParentContent)
                    };
                    parentChunks.Add(parent);
                    chunk.ParentId = parentId;
                }

                allChunks.Add(chunk);
            }

            progress?.Report(new IngestProgress("Chunking", fi + 1, files.Count, $"{title} → {textChunks.Count} chunks"));
        }

        health.DuplicateChunkCount = allChunks
            .GroupBy(c => $"{c.SourcePath}\n{c.Content}", StringComparer.Ordinal)
            .Sum(g => Math.Max(0, g.Count() - 1));
        if (health.DuplicateChunkCount > 0)
            health.Warnings.Add($"{health.DuplicateChunkCount} duplicate chunks detected.");
        if (health.EmptyChunkCount > 0)
            health.Warnings.Add($"{health.EmptyChunkCount} empty chunks detected.");

        // ── 2. Embed in batches ───────────────────────────────────────────
        int total = allChunks.Count;
        progress?.Report(new IngestProgress("Embedding", 0, total, $"Embedding {total} chunks..."));

        for (int i = 0; i < allChunks.Count; i += EmbedBatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch  = allChunks.Skip(i).Take(EmbedBatchSize).ToList();
            var texts  = batch.Select(c => BuildEmbeddingText(c, dataset.Config)).ToList();
            var embeddings = await _embed.EmbedBatchAsync(texts, ct);

            for (int j = 0; j < batch.Count; j++)
                batch[j].Embedding = embeddings[j];

            progress?.Report(new IngestProgress("Embedding", Math.Min(i + EmbedBatchSize, total), total,
                $"Batch {i / EmbedBatchSize + 1}"));
        }

        // ── 3. Store ──────────────────────────────────────────────────────
        progress?.Report(new IngestProgress("Storing", 0, total, "Writing to SQLite..."));

        await _store.DeleteChunksForSourcesAsync(dataset.Id, changedSourcePaths, ct);

        if (parentChunks.Count > 0)
            await _store.SaveChunksBatchAsync(parentChunks, ct);

        await _store.SaveChunksBatchAsync(allChunks, ct);

        // ── 4. BM25 stats ─────────────────────────────────────────────────
        progress?.Report(new IngestProgress("Indexing", 0, 1, "Building BM25 stats..."));
        var stats = Bm25Scorer.BuildStats(allChunks);
        await _store.SaveBm25StatsAsync(dataset.Id, stats, ct);

        // ── 5. Update dataset chunk count ─────────────────────────────────
        dataset.ChunkCount = allChunks.Count;
        await _store.SaveDatasetAsync(dataset, ct);

        progress?.Report(new IngestProgress("Done", total, total,
            $"{allChunks.Count} chunks indexed from {files.Count} files. Health: {BuildHealthSummary(health)}"));
    }

    private static string BuildEmbeddingText(RagChunk chunk, RagDatasetConfig cfg)
    {
        if (!cfg.PrependTitleToEmbedding || string.IsNullOrWhiteSpace(chunk.SourceTitle))
            return chunk.Content;

        return $"Title: {chunk.SourceTitle}\nSource: {chunk.SourceFile}\n\n{chunk.Content}";
    }

    private static void ValidateIngestConfig(RagDatasetConfig config)
    {
        var template = config.PromptTemplate ?? string.Empty;
        if (!template.Contains("{context}", StringComparison.OrdinalIgnoreCase)
            || !template.Contains("{question}", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("RAG prompt template must include both {context} and {question}.");
        }

        if (template.Split("{context}", StringSplitOptions.None).Length > 2)
            throw new InvalidOperationException("RAG prompt template includes {context} more than once.");
    }

    private static string ComputeHash(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string BuildHealthSummary(RagIngestHealth health)
    {
        if (health.Warnings.Count == 0) return "ok";
        return string.Join("; ", health.Warnings.Take(3));
    }
}
