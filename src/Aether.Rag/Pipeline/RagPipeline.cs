using Aether.Rag.Chunking;
using Aether.Rag.Embeddings;
using Aether.Rag.Models;
using Aether.Rag.Retrieval;
using Aether.Rag.Storage;
using System.Security.Cryptography;
using System.Net;
using System.Text.RegularExpressions;

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
    private readonly HttpClient _http;

    private const int EmbedBatchSize = 10;
    private const int MaxWebPageBytes = 2 * 1024 * 1024;

    public RagPipeline(SqliteRagStore store, IEmbeddingService embed)
        : this(store, embed, new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
    {
    }

    public RagPipeline(SqliteRagStore store, IEmbeddingService embed, HttpClient http)
    {
        _store = store;
        _embed = embed;
        _http = http;
    }

    public async Task<IngestReport> IngestDirectoryAsync(
        RagDataset dataset,
        string directory,
        IProgress<IngestProgress>? progress = null,
        CancellationToken ct = default,
        IngestOptions? options = null)
    {
        options ??= new IngestOptions();
        ValidateIngestConfig(dataset.Config);
        if (dataset.Config.ExtractionMode is not RagExtractionMode.TextMarkdown)
            throw new NotSupportedException($"{dataset.Config.ExtractionMode} is configured but not yet implemented. The provider slot is ready; install a concrete extractor before ingesting this profile.");

        var files = Directory.GetFiles(directory, "*.txt", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(directory, "*.md", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(directory, "*.pdf", SearchOption.AllDirectories))
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
            throw new InvalidOperationException($"No .txt, .md, or .pdf files found in {directory}");

        progress?.Report(new IngestProgress("Chunking", 0, files.Count, $"Found {files.Count} files"));

        var report = new IngestReport();

        // lookup existing source hashes to decide skip/replace
        var existingHashes = await _store.GetSourceHashesAsync(dataset.Id, files, ct);

        // ── 1. Chunk all files ────────────────────────────────────────────
        var allChunks = new List<RagChunk>();
        var parentChunks = new List<RagChunk>(); // parent-child mode
        var changedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var health = new RagIngestHealth { FileCount = files.Count };

        for (int fi = 0; fi < files.Count; fi++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[fi];
            var document = await LoadLocalDocumentAsync(file, health, ct);
            if (document is null)
            {
                progress?.Report(new IngestProgress("Chunking", fi + 1, files.Count, $"Skipped {Path.GetFileName(file)}"));
                continue;
            }

            // decide planned action for this source
            var existingHash = existingHashes.TryGetValue(file, out var h) ? h : null;
            if (!string.IsNullOrWhiteSpace(existingHash) && existingHash == document.SourceHash)
            {
                if (options.DuplicatePolicy == IngestDuplicatePolicy.SkipIfUnchanged)
                {
                    report.Documents.Add(new DocumentIngestReport { Path = document.SourcePath, Status = DocumentIngestStatus.SkippedUnchanged, Message = "Source unchanged, skipping" });
                    progress?.Report(new IngestProgress("Chunking", fi + 1, files.Count, $"Skipped unchanged: {Path.GetFileName(file)}"));
                    continue;
                }
                else if (options.DuplicatePolicy == IngestDuplicatePolicy.ReportOnly)
                {
                    report.Documents.Add(new DocumentIngestReport { Path = document.SourcePath, Status = DocumentIngestStatus.ReportOnly, Message = "Would replace (report-only)" });
                }
                else
                {
                    // Replace path - will be treated as replace later
                    report.Documents.Add(new DocumentIngestReport { Path = document.SourcePath, Status = DocumentIngestStatus.Replaced, Message = "Will replace existing source" });
                }
            }
            else
            {
                // new or changed
                if (string.IsNullOrWhiteSpace(existingHash))
                    report.Documents.Add(new DocumentIngestReport { Path = document.SourcePath, Status = DocumentIngestStatus.Added, Message = "New source" });
                else
                    report.Documents.Add(new DocumentIngestReport { Path = document.SourcePath, Status = DocumentIngestStatus.Replaced, Message = "Changed source - will replace" });
            }

            if (new FileInfo(file).Length > 50 * 1024 * 1024)
            {
                health.OversizedFileCount++;
                health.Warnings.Add($"Large file: {document.SourcePath}");
            }

            var textChunks = _chunker.Chunk(document.Text, file, document.Title, dataset.Config);
            changedSourcePaths.Add(document.SourcePath);

            foreach (var tc in textChunks)
            {
                if (string.IsNullOrWhiteSpace(tc.Content))
                    health.EmptyChunkCount++;

                var chunk = new RagChunk
                {
                    DatasetId   = dataset.Id,
                    SourceFile  = document.SourceFile,
                    SourcePath  = document.SourcePath,
                    SourceHash  = document.SourceHash,
                    SourceModifiedUtc = document.ModifiedUtc,
                    SourceTitle = document.Title,
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

            progress?.Report(new IngestProgress("Chunking", fi + 1, files.Count, $"{document.Title} -> {textChunks.Count} chunks"));
        }

        health.DuplicateChunkCount = allChunks
            .GroupBy(c => $"{c.SourcePath}\n{c.Content}", StringComparer.Ordinal)
            .Sum(g => Math.Max(0, g.Count() - 1));
        if (health.DuplicateChunkCount > 0)
            health.Warnings.Add($"{health.DuplicateChunkCount} duplicate chunks detected.");
        if (health.EmptyChunkCount > 0)
            health.Warnings.Add($"{health.EmptyChunkCount} empty chunks detected.");

        // ── 2. If dry-run, skip embedding and storage, return report and health
        if (options.DryRun)
        {
            if (health.DuplicateChunkCount > 0)
                health.Warnings.Add($"{health.DuplicateChunkCount} duplicate chunks detected.");
            if (health.EmptyChunkCount > 0)
                health.Warnings.Add($"{health.EmptyChunkCount} empty chunks detected.");

            report.Documents.Add(new DocumentIngestReport { Path = "__health__", Status = DocumentIngestStatus.ReportOnly, Message = BuildHealthSummary(health) });
            progress?.Report(new IngestProgress("Done", allChunks.Count, allChunks.Count, $"Dry-run complete. {report.Summary()}"));
            return report;
        }

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

        await _store.SaveDatasetAsync(dataset, ct);
        // if policy is SkipIfUnchanged already skipped unchanged files earlier; for replace and report-only default to delete of changed paths
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

        report.Documents.Add(new DocumentIngestReport { Path = "__health__", Status = DocumentIngestStatus.ReportOnly, Message = BuildHealthSummary(health) });
        return report;
    }

    public async Task<IngestReport> IngestWebAsync(
        RagDataset dataset,
        IProgress<IngestProgress>? progress = null,
        CancellationToken ct = default,
        IngestOptions? options = null)
    {
        options ??= new IngestOptions();
        ValidateIngestConfig(dataset.Config);
        if (!dataset.Config.EnableWebLoader)
            throw new InvalidOperationException("Web loader is disabled for this dataset.");
        if (dataset.Config.ExtractionMode is not RagExtractionMode.WebUrl)
            throw new InvalidOperationException("Web ingest requires WebUrl extraction mode.");

        var urls = ParseWebUrls(dataset.Config);
        if (urls.Count == 0)
            throw new InvalidOperationException("Add at least one http:// or https:// URL before running web ingest.");

        progress?.Report(new IngestProgress("Fetching", 0, urls.Count, $"Fetching {urls.Count} web page(s)"));
        var documents = new List<WebDocument>();
        for (var i = 0; i < urls.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var doc = await FetchWebDocumentAsync(urls[i], ct);
            documents.Add(doc);
            progress?.Report(new IngestProgress("Fetching", i + 1, urls.Count, doc.Title));
        }

        var allChunks = new List<RagChunk>();
        var parentChunks = new List<RagChunk>();
        var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        progress?.Report(new IngestProgress("Chunking", 0, documents.Count, $"Chunking {documents.Count} web page(s)"));

        for (var i = 0; i < documents.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var doc = documents[i];
            var sourceHash = ComputeHash(doc.Content);
            var textChunks = _chunker.Chunk(doc.Content, doc.Url.ToString(), doc.Title, dataset.Config);
            sourcePaths.Add(doc.Url.ToString());

            foreach (var tc in textChunks)
            {
                var chunk = new RagChunk
                {
                    DatasetId = dataset.Id,
                    SourceFile = doc.Url.Host,
                    SourcePath = doc.Url.ToString(),
                    SourceHash = sourceHash,
                    SourceModifiedUtc = DateTime.UtcNow,
                    SourceTitle = doc.Title,
                    Content = tc.Content,
                    ChunkIndex = tc.Index,
                    ChunkTotal = tc.Total,
                    TokenCount = ParagraphChunker.EstimateTokens(tc.Content)
                };

                if (tc.ParentContent is not null)
                {
                    var parentId = Guid.NewGuid().ToString();
                    parentChunks.Add(new RagChunk
                    {
                        Id = parentId,
                        DatasetId = dataset.Id,
                        SourceFile = chunk.SourceFile,
                        SourcePath = chunk.SourcePath,
                        SourceHash = chunk.SourceHash,
                        SourceModifiedUtc = chunk.SourceModifiedUtc,
                        SourceTitle = chunk.SourceTitle,
                        Content = tc.ParentContent,
                        ChunkIndex = tc.Index,
                        ChunkTotal = tc.Total,
                        TokenCount = ParagraphChunker.EstimateTokens(tc.ParentContent)
                    });
                    chunk.ParentId = parentId;
                }

                allChunks.Add(chunk);
            }

            progress?.Report(new IngestProgress("Chunking", i + 1, documents.Count, $"{doc.Title} -> {textChunks.Count} chunks"));
        }

        if (options.DryRun)
        {
            var report = new IngestReport();
            report.Documents.Add(new DocumentIngestReport { Path = "__health__", Status = DocumentIngestStatus.ReportOnly, Message = $"Dry-run: {allChunks.Count} chunks from {documents.Count} pages" });
            progress?.Report(new IngestProgress("Done", allChunks.Count, allChunks.Count, $"Dry-run complete. {report.Summary()}"));
            return report;
        }

        await EmbedAndStoreAsync(dataset, allChunks, parentChunks, sourcePaths, progress, ct);
        progress?.Report(new IngestProgress("Done", allChunks.Count, allChunks.Count,
            $"{allChunks.Count} chunks indexed from {documents.Count} web page(s)."));

        var finalReport = new IngestReport();
        finalReport.Documents.Add(new DocumentIngestReport { Path = "__health__", Status = DocumentIngestStatus.ReportOnly, Message = $"{allChunks.Count} chunks indexed from {documents.Count} web page(s)." });
        return finalReport;
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

    public static IReadOnlyList<Uri> ParseWebUrls(RagDatasetConfig config)
    {
        if (!config.EnableWebLoader)
            return [];

        var max = Math.Clamp(config.WebMaxPages <= 0 ? 5 : config.WebMaxPages, 1, 20);
        var urls = new List<Uri>();
        foreach (var line in (config.WebUrlList ?? string.Empty)
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(line, UriKind.Absolute, out var uri))
                throw new InvalidOperationException($"Invalid web URL: {line}");
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unsupported web URL scheme: {uri.Scheme}");
            if (!urls.Any(u => Uri.Compare(u, uri, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0))
                urls.Add(uri);
            if (urls.Count >= max)
                break;
        }

        return urls;
    }

    public static string ExtractTextFromHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = Regex.Replace(html, @"<script\b[^>]*>.*?</script>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<style\b[^>]*>.*?</style>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private async Task<WebDocument> FetchWebDocumentAsync(Uri url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(mediaType)
            && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
            && !mediaType.Contains("text", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported content type for {url}: {mediaType}");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length > MaxWebPageBytes)
            throw new InvalidOperationException($"Web page is too large: {url}");

        var raw = System.Text.Encoding.UTF8.GetString(bytes);
        var text = mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
            ? ExtractTextFromHtml(raw)
            : raw.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"No readable text found at {url}");

        var title = ExtractTitle(raw) ?? url.Host;
        return new WebDocument(url, title, text);
    }

    private static string? ExtractTitle(string html)
    {
        var match = Regex.Match(html, @"<title\b[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success) return null;
        var title = WebUtility.HtmlDecode(Regex.Replace(match.Groups[1].Value, @"\s+", " ").Trim());
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private async Task EmbedAndStoreAsync(
        RagDataset dataset,
        List<RagChunk> allChunks,
        List<RagChunk> parentChunks,
        HashSet<string> changedSourcePaths,
        IProgress<IngestProgress>? progress,
        CancellationToken ct)
    {
        int total = allChunks.Count;
        progress?.Report(new IngestProgress("Embedding", 0, total, $"Embedding {total} chunks..."));

        for (int i = 0; i < allChunks.Count; i += EmbedBatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = allChunks.Skip(i).Take(EmbedBatchSize).ToList();
            var texts = batch.Select(c => BuildEmbeddingText(c, dataset.Config)).ToList();
            var embeddings = await _embed.EmbedBatchAsync(texts, ct);

            for (int j = 0; j < batch.Count; j++)
                batch[j].Embedding = embeddings[j];

            progress?.Report(new IngestProgress("Embedding", Math.Min(i + EmbedBatchSize, total), total,
                $"Batch {i / EmbedBatchSize + 1}"));
        }

        progress?.Report(new IngestProgress("Storing", 0, total, "Writing to SQLite..."));
        ct.ThrowIfCancellationRequested();
        await _store.SaveDatasetAsync(dataset, ct);
        ct.ThrowIfCancellationRequested();
        await _store.DeleteChunksForSourcesAsync(dataset.Id, changedSourcePaths, ct);

        if (parentChunks.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            await _store.SaveChunksBatchAsync(parentChunks, ct);
        }

        ct.ThrowIfCancellationRequested();
        await _store.SaveChunksBatchAsync(allChunks, ct);

        progress?.Report(new IngestProgress("Indexing", 0, 1, "Building BM25 stats..."));
        ct.ThrowIfCancellationRequested();
        var stats = Bm25Scorer.BuildStats(allChunks);
        ct.ThrowIfCancellationRequested();
        await _store.SaveBm25StatsAsync(dataset.Id, stats, ct);

        dataset.ChunkCount = allChunks.Count;
        ct.ThrowIfCancellationRequested();
        await _store.SaveDatasetAsync(dataset, ct);
    }

    private static async Task<LocalDocument?> LoadLocalDocumentAsync(string file, RagIngestHealth health, CancellationToken ct)
    {
        var sourcePath = Path.GetFullPath(file);
        var title = Path.GetFileNameWithoutExtension(file);
        var extension = Path.GetExtension(file);
        string text;

        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var extracted = await PdfTextExtractor.ExtractAsync(file, ct);
            if (!extracted.HasText)
            {
                health.UnsupportedFileCount++;
                health.Warnings.Add($"No extractable PDF text: {sourcePath}");
                return null;
            }

            text = extracted.Text;
        }
        else
        {
            text = await File.ReadAllTextAsync(file, ct);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            health.EmptyChunkCount++;
            health.Warnings.Add($"No readable text: {sourcePath}");
            return null;
        }

        return new LocalDocument(
            Path.GetFileName(file),
            sourcePath,
            title,
            text,
            ComputeHash(text),
            File.GetLastWriteTimeUtc(file));
    }

    private sealed record WebDocument(Uri Url, string Title, string Content);

    private sealed record LocalDocument(
        string SourceFile,
        string SourcePath,
        string Title,
        string Text,
        string SourceHash,
        DateTime ModifiedUtc);

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
