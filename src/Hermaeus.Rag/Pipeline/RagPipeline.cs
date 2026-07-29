using Hermaeus.Rag.Chunking;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using System.Security.Cryptography;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Hermaeus.Rag.Pipeline;

public record IngestProgress(
    string Stage,
    int Done,
    int Total,
    string Detail = "",
    int OverallDone = 0,
    int OverallTotal = 0,
    string OverallDetail = "");

/// <summary>
/// Orchestrates the full ingest pipeline:
/// Load → Chunk → Embed (batched) → Store → BM25 stats
/// </summary>
public sealed class RagPipeline
{
    private static readonly HttpClient _defaultHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly SqliteRagStore   _store;
    private readonly IEmbeddingService _embed;
    private readonly ParagraphChunker  _chunker = new();
    private readonly HttpClient _http;

    private const int EmbedBatchSize = 10;
    private const int DirectoryFileBatchSize = 50;
    private const int MaxWebPageBytes = 2 * 1024 * 1024;

    // r10 02-rag-quality.md 2.1: raised from 192 so a default 1600-char chunk
    // (plus its metadata header) fits inside one embedding call; the retry
    // ladder still steps down for servers with small physical batches.
    private const int MaxEmbeddingInputTokens = 512;
    private static readonly int[] EmbeddingInputRetryTokenLimits = [MaxEmbeddingInputTokens, 256, 128];

    /// <summary>Header (title/path/heading/etc.) must not crowd out chunk content inside the embedding budget.</summary>
    private const int MaxHeaderTokens = 48;

    public RagPipeline(SqliteRagStore store, IEmbeddingService embed)
        : this(store, embed, null)
    {
    }

    public RagPipeline(SqliteRagStore store, IEmbeddingService embed, HttpClient? http)
    {
        _store = store;
        _embed = embed;
        _http = http ?? _defaultHttp;
    }

    public async Task<IngestReport> IngestDirectoryAsync(
        RagDataset dataset,
        string directory,
        IProgress<IngestProgress>? progress = null,
        CancellationToken ct = default,
        IngestOptions? options = null,
        IReadOnlyList<string>? explicitFiles = null)
    {
        options ??= new IngestOptions();
        ValidateIngestConfig(dataset.Config);
        if (dataset.Config.ExtractionMode is not RagExtractionMode.TextMarkdown)
            throw new NotSupportedException($"{dataset.Config.ExtractionMode} is configured but not yet implemented. The provider slot is ready; install a concrete extractor before ingesting this profile.");

        // r24 doc 03 3.3: a watched-source refresh passes its own computed
        // new+changed file list (already filtered by include/exclude globs)
        // instead of the directory-wide .txt/.md/.pdf scan below, so the same
        // pipeline - chunking, embedding, BM25 rebuild, cache invalidation -
        // runs for both a manual ingest and a refresh with no parallel path.
        var files = explicitFiles?.OrderBy(f => f).ToList() ?? Directory.GetFiles(directory, "*.txt", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(directory, "*.md", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(directory, "*.pdf", SearchOption.AllDirectories))
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
            throw new InvalidOperationException($"No .txt, .md, or .pdf files found in {directory}");

        var fileBatchCount = (int)Math.Ceiling(files.Count / (double)DirectoryFileBatchSize);
        var overallTotal = Math.Max(1, fileBatchCount * 3 + 2);
        progress?.Report(new IngestProgress("Chunking", 0, files.Count, $"Found {files.Count} files", 0, overallTotal, $"File batch 0 of {fileBatchCount}"));

        var report = new IngestReport();

        // lookup existing source hashes to decide skip/replace
        var existingHashes = await _store.GetSourceHashesAsync(dataset.Id, files, ct);

        var health = new RagIngestHealth { FileCount = files.Count };
        AddChunkSizeGuardWarning(health, dataset.Config);
        var duplicateKeys = new HashSet<string>(StringComparer.Ordinal);
        var totalChunksSeen = 0;

        // ── 1. Chunk, embed, and store in file batches ────────────────────
        // r10 01-rag-correctness.md 1.6: a dry run must not create or
        // update the dataset row; the final save at the end of this method
        // is already skipped by the dry-run early return below.
        if (!options.DryRun)
            await _store.SaveDatasetAsync(dataset, ct);
        for (int batchStart = 0; batchStart < files.Count; batchStart += DirectoryFileBatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batchIndex = batchStart / DirectoryFileBatchSize;
            var batchNumber = batchIndex + 1;
            var batchStepBase = batchIndex * 3;
            var batchLabel = $"File batch {batchNumber} of {fileBatchCount}";
            var fileBatch = files.Skip(batchStart).Take(DirectoryFileBatchSize).ToList();
            var batchChunks = new List<RagChunk>();
            var batchParents = new List<RagChunk>();
            var changedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int fi = 0; fi < fileBatch.Count; fi++)
            {
                ct.ThrowIfCancellationRequested();
                var file = fileBatch[fi];
                var absoluteFileIndex = batchStart + fi;
                var document = await LoadLocalDocumentAsync(file, health, ct);
                if (document is null)
                {
                    progress?.Report(new IngestProgress("Chunking", absoluteFileIndex + 1, files.Count, $"Skipped {Path.GetFileName(file)}", batchStepBase, overallTotal, batchLabel));
                    continue;
                }

                // decide planned action for this source
                var existingHash = existingHashes.TryGetValue(file, out var h) ? h : null;
                if (!string.IsNullOrWhiteSpace(existingHash) && existingHash == document.SourceHash)
                {
                    if (options.DuplicatePolicy == IngestDuplicatePolicy.SkipIfUnchanged)
                    {
                        report.Documents.Add(new DocumentIngestReport { Path = document.SourcePath, Status = DocumentIngestStatus.SkippedUnchanged, Message = "Source unchanged, skipping" });
                        progress?.Report(new IngestProgress("Chunking", absoluteFileIndex + 1, files.Count, $"Skipped unchanged: {Path.GetFileName(file)}", batchStepBase, overallTotal, batchLabel));
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

                    var chunk = CreateChunk(dataset.Id, document.SourceFile, document.SourcePath, document.SourceHash, document.ModifiedUtc, document.Title, tc);

                    if (!duplicateKeys.Add($"{chunk.SourcePath}\n{chunk.Content}"))
                        health.DuplicateChunkCount++;

                    if (tc.ParentContent is not null)
                    {
                        // Parent chunk (stored but not embedded for indexing)
                        var parentId = Guid.NewGuid().ToString();
                        var parent = CreateChunk(dataset.Id, document.SourceFile, document.SourcePath, document.SourceHash, document.ModifiedUtc, document.Title, tc, tc.ParentContent, parentId, null);
                        parent.IsParent = true;
                        batchParents.Add(parent);
                        chunk.ParentId = parentId;
                    }

                    batchChunks.Add(chunk);
                }

                totalChunksSeen += textChunks.Count;
                progress?.Report(new IngestProgress("Chunking", absoluteFileIndex + 1, files.Count, $"{document.Title} -> {textChunks.Count} chunks", batchStepBase, overallTotal, batchLabel));
            }

            if (options.DryRun || batchChunks.Count == 0)
                continue;

            await EmbedChunksAsync(dataset, batchChunks, progress, ct, batchStepBase + 1, overallTotal, batchLabel);

            progress?.Report(new IngestProgress("Storing", Math.Min(batchStart + fileBatch.Count, files.Count), files.Count,
                $"Writing {batchLabel.ToLowerInvariant()} to SQLite...", batchStepBase + 2, overallTotal, batchLabel));
            ct.ThrowIfCancellationRequested();
            await _store.DeleteChunksForSourcesAsync(dataset.Id, changedSourcePaths, ct);

            ct.ThrowIfCancellationRequested();
            if (batchParents.Count > 0)
                await _store.SaveChunksBatchAsync(batchParents, ct);

            ct.ThrowIfCancellationRequested();
            await _store.SaveChunksBatchAsync(batchChunks, ct);
        }

        AddHealthWarnings(health);

        // ── 2. If dry-run, skip embedding and storage, return report and health
        if (options.DryRun)
        {
            report.Health = health;
            progress?.Report(new IngestProgress("Done", totalChunksSeen, totalChunksSeen, $"Dry-run complete. {report.Summary()}"));
            return report;
        }

        // ── 3. BM25 stats ─────────────────────────────────────────────────
        progress?.Report(new IngestProgress("Indexing", 0, 1, "Building BM25 stats...", overallTotal - 1, overallTotal, "Final index"));
        var allStoredChunks = await _store.GetChunksAsync(dataset.Id, includeEmbeddings: false, ct);
        var stats = Bm25Scorer.BuildStats(allStoredChunks);
        await _store.SaveBm25StatsAsync(dataset.Id, stats, ct);

        // ── 4. Update dataset chunk count ─────────────────────────────────
        dataset.ChunkCount = allStoredChunks.Count;
        await _store.SaveDatasetAsync(dataset, ct);

        progress?.Report(new IngestProgress("Done", allStoredChunks.Count, allStoredChunks.Count,
            $"{totalChunksSeen} chunks processed from {files.Count} files. Health: {BuildHealthSummary(health)}", overallTotal, overallTotal, "Complete"));

        report.Health = health;
        return report;
    }

    /// <summary>
    /// Re-embeds every stored chunk of a dataset with its current
    /// <see cref="RagDatasetConfig.EmbeddingModel"/> (caller sets that field
    /// to the new model before calling), rebuilds BM25 stats, and updates
    /// the dataset row (r10 01-rag-correctness.md 1.4). Works from stored
    /// chunk content only; the original source files are never touched.
    /// </summary>
    public async Task<int> ReindexDatasetAsync(
        RagDataset dataset,
        IProgress<IngestProgress>? progress = null,
        CancellationToken ct = default)
    {
        var chunks = await _store.GetChunksAsync(dataset.Id, includeEmbeddings: false, ct);
        if (chunks.Count == 0)
        {
            progress?.Report(new IngestProgress("Done", 0, 0, "Nothing to reindex."));
            return 0;
        }

        progress?.Report(new IngestProgress("Embedding", 0, chunks.Count, $"Reindexing {chunks.Count} chunk(s)..."));

        for (var i = 0; i < chunks.Count; i += EmbedBatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = chunks.Skip(i).Take(EmbedBatchSize).ToList();
            var embeddings = await EmbedBatchWithRetryAsync(batch, dataset.Config, ct);
            if (embeddings.Count > 0)
                dataset.Config.EmbeddingDimensions = embeddings[0].Length;

            for (var j = 0; j < batch.Count; j++)
                batch[j].Embedding = embeddings[j];

            ct.ThrowIfCancellationRequested();
            await _store.SaveChunksBatchAsync(batch, ct);

            var done = Math.Min(i + EmbedBatchSize, chunks.Count);
            progress?.Report(new IngestProgress("Embedding", done, chunks.Count, $"Reindexed {done} of {chunks.Count} chunk(s)"));
        }

        progress?.Report(new IngestProgress("Indexing", 0, 1, "Rebuilding BM25 stats..."));
        ct.ThrowIfCancellationRequested();
        var statsSource = await _store.GetChunksAsync(dataset.Id, includeEmbeddings: false, ct);
        var stats = Bm25Scorer.BuildStats(statsSource);
        await _store.SaveBm25StatsAsync(dataset.Id, stats, ct);

        ct.ThrowIfCancellationRequested();
        await _store.SaveDatasetAsync(dataset, ct);

        progress?.Report(new IngestProgress("Done", chunks.Count, chunks.Count, $"Reindex complete: {chunks.Count} chunk(s)."));
        return chunks.Count;
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
        var health = new RagIngestHealth { FileCount = documents.Count };
        AddChunkSizeGuardWarning(health, dataset.Config);
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
                if (string.IsNullOrWhiteSpace(tc.Content))
                    health.EmptyChunkCount++;

                var chunk = CreateChunk(dataset.Id, doc.Url.Host, doc.Url.ToString(), sourceHash, DateTime.UtcNow, doc.Title, tc);

                if (tc.ParentContent is not null)
                {
                    var parentId = Guid.NewGuid().ToString();
                    var parent = CreateChunk(dataset.Id, doc.Url.Host, doc.Url.ToString(), sourceHash, DateTime.UtcNow, doc.Title, tc, tc.ParentContent, parentId, null);
                    parent.IsParent = true;
                    parentChunks.Add(parent);
                    chunk.ParentId = parentId;
                }

                allChunks.Add(chunk);
            }

            progress?.Report(new IngestProgress("Chunking", i + 1, documents.Count, $"{doc.Title} -> {textChunks.Count} chunks"));
        }

        health.DuplicateChunkCount = allChunks
            .GroupBy(c => $"{c.SourcePath}\n{c.Content}", StringComparer.Ordinal)
            .Sum(g => Math.Max(0, g.Count() - 1));
        if (health.DuplicateChunkCount > 0)
            health.Warnings.Add($"{health.DuplicateChunkCount} duplicate chunks detected.");
        if (health.EmptyChunkCount > 0)
            health.Warnings.Add($"{health.EmptyChunkCount} empty chunks detected.");

        if (options.DryRun)
        {
            var report = new IngestReport();
            report.Health = health;
            progress?.Report(new IngestProgress("Done", allChunks.Count, allChunks.Count, $"Dry-run complete. {report.Summary()}"));
            return report;
        }

        await EmbedAndStoreAsync(dataset, allChunks, parentChunks, sourcePaths, progress, ct);
        progress?.Report(new IngestProgress("Done", allChunks.Count, allChunks.Count,
            $"{allChunks.Count} chunks indexed from {documents.Count} web page(s)."));

        var finalReport = new IngestReport { Health = health };
        return finalReport;
    }

    /// <summary>
    /// r10 02-rag-quality.md 2.1: the metadata header used to be prepended
    /// inside the same token budget as the content with no cap of its own,
    /// so a long source path could crowd out most of a chunk. The header is
    /// capped at <see cref="MaxHeaderTokens"/>; if it doesn't fit, the path
    /// (the most variable, least front-loaded-informative line) is
    /// truncated from the head, keeping the distinctive tail. Content is
    /// never truncated here.
    /// </summary>
    private static string BuildEmbeddingText(RagChunk chunk, RagDatasetConfig cfg)
    {
        var header = ComposeEmbeddingHeader(chunk, cfg, chunk.SourcePath);
        if (header.Length > 0 && ParagraphChunker.EstimateTokens(header) > MaxHeaderTokens && !string.IsNullOrWhiteSpace(chunk.SourcePath))
        {
            var withoutPathTokens = ParagraphChunker.EstimateTokens(ComposeEmbeddingHeader(chunk, cfg, string.Empty));
            var pathBudgetChars = Math.Max(16, (MaxHeaderTokens - withoutPathTokens) * 4);
            var truncatedPath = chunk.SourcePath.Length > pathBudgetChars
                ? chunk.SourcePath[^pathBudgetChars..]
                : chunk.SourcePath;
            header = ComposeEmbeddingHeader(chunk, cfg, truncatedPath);
        }

        return header.Length == 0 ? chunk.Content : header + "\n\n" + chunk.Content;
    }

    private static string ComposeEmbeddingHeader(RagChunk chunk, RagDatasetConfig cfg, string sourcePath)
    {
        var builder = new StringBuilder();

        if (cfg.PrependTitleToEmbedding && !string.IsNullOrWhiteSpace(chunk.SourceTitle))
            builder.AppendLine($"Title: {chunk.SourceTitle}");

        if (!string.IsNullOrWhiteSpace(sourcePath))
            builder.AppendLine($"Source: {sourcePath}");

        if (chunk.ChunkKind != RagChunkKind.PlainText)
            builder.AppendLine($"Chunk kind: {chunk.ChunkKind}");

        if (!string.IsNullOrWhiteSpace(chunk.HeadingPath))
            builder.AppendLine($"Heading: {chunk.HeadingPath}");

        if (!string.IsNullOrWhiteSpace(chunk.CodeSymbolInfo))
            builder.AppendLine($"Symbol: {chunk.CodeSymbolInfo}");

        if (chunk.PageNumber.HasValue)
            builder.AppendLine($"Page: {chunk.PageNumber}");

        if (!string.IsNullOrWhiteSpace(chunk.EventType))
            builder.AppendLine($"Event: {chunk.EventType}");

        if (!string.IsNullOrWhiteSpace(chunk.SourceUrl))
            builder.AppendLine($"Url: {chunk.SourceUrl}");

        return builder.ToString().TrimEnd('\r', '\n');
    }

    private static void AddHealthWarnings(RagIngestHealth health)
    {
        if (health.DuplicateChunkCount > 0)
            health.Warnings.Add($"{health.DuplicateChunkCount} duplicate chunks detected.");
        if (health.EmptyChunkCount > 0)
            health.Warnings.Add($"{health.EmptyChunkCount} empty chunks detected.");
    }

    /// <summary>r10 02-rag-quality.md 2.1: an oversized custom chunk config should be visible, not silently truncated.</summary>
    private static void AddChunkSizeGuardWarning(RagIngestHealth health, RagDatasetConfig cfg)
    {
        var maxChunkCharsForClamp = MaxEmbeddingInputTokens * 4;
        if (cfg.TargetChunkChars > maxChunkCharsForClamp)
            health.Warnings.Add(
                $"Chunk size ({cfg.TargetChunkChars} chars) exceeds the embedding input clamp " +
                $"({MaxEmbeddingInputTokens} tokens, ~{maxChunkCharsForClamp} chars); the end of oversized chunks may not be embedded.");
    }

    private static RagChunk CreateChunk(
        string datasetId,
        string sourceFile,
        string sourcePath,
        string sourceHash,
        DateTime modifiedUtc,
        string sourceTitle,
        TextChunk textChunk,
        string? contentOverride = null,
        string? chunkId = null,
        string? parentId = null)
    {
        return new RagChunk
        {
            Id = chunkId ?? Guid.NewGuid().ToString(),
            DatasetId = datasetId,
            SourceFile = sourceFile,
            SourcePath = sourcePath,
            SourceHash = sourceHash,
            SourceModifiedUtc = modifiedUtc,
            SourceTitle = sourceTitle,
            Content = contentOverride ?? textChunk.Content,
            ChunkIndex = textChunk.Index,
            ChunkTotal = textChunk.Total,
            ParentId = parentId,
            TokenCount = ParagraphChunker.EstimateTokens(contentOverride ?? textChunk.Content),
            ChunkKind = textChunk.ChunkKind,
            HeadingPath = textChunk.HeadingPath,
            CodeSymbolInfo = textChunk.CodeSymbolInfo,
            PageNumber = textChunk.PageNumber,
            EventType = textChunk.EventType,
            SourceUrl = textChunk.SourceUrl
        };
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
        await EmbedChunksAsync(dataset, allChunks, progress, ct);
        await StoreChunksAsync(dataset, allChunks, parentChunks, changedSourcePaths, progress, ct);
    }

    private async Task EmbedChunksAsync(
        RagDataset dataset,
        List<RagChunk> allChunks,
        IProgress<IngestProgress>? progress,
        CancellationToken ct,
        int overallDone = 0,
        int overallTotal = 0,
        string overallDetail = "")
    {
        int total = allChunks.Count;
        var embeddingBatchTotal = (int)Math.Ceiling(total / (double)EmbedBatchSize);
        progress?.Report(new IngestProgress("Embedding", 0, total, $"Embedding {total} chunks...", overallDone, overallTotal, overallDetail));

        for (int i = 0; i < allChunks.Count; i += EmbedBatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = allChunks.Skip(i).Take(EmbedBatchSize).ToList();
            var embeddings = await EmbedBatchWithRetryAsync(batch, dataset.Config, ct);
            if (embeddings.Count > 0)
                dataset.Config.EmbeddingDimensions = embeddings[0].Length;

            for (int j = 0; j < batch.Count; j++)
                batch[j].Embedding = embeddings[j];

            var embeddingBatchNumber = i / EmbedBatchSize + 1;
            progress?.Report(new IngestProgress("Embedding", Math.Min(i + EmbedBatchSize, total), total,
                string.IsNullOrWhiteSpace(overallDetail)
                    ? $"Embedding batch {embeddingBatchNumber} of {embeddingBatchTotal}"
                    : $"{overallDetail}: embedding batch {embeddingBatchNumber} of {embeddingBatchTotal}",
                overallDone,
                overallTotal,
                overallDetail));
        }
    }

    private async Task StoreChunksAsync(
        RagDataset dataset,
        List<RagChunk> allChunks,
        List<RagChunk> parentChunks,
        HashSet<string> changedSourcePaths,
        IProgress<IngestProgress>? progress,
        CancellationToken ct)
    {
        int total = allChunks.Count;
        progress?.Report(new IngestProgress("Storing", 0, total, "Writing to SQLite..."));

        ct.ThrowIfCancellationRequested();
        await _store.SaveDatasetAsync(dataset, ct);

        ct.ThrowIfCancellationRequested();
        await _store.DeleteChunksForSourcesAsync(dataset.Id, changedSourcePaths, ct);

        if (parentChunks.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            // save parent chunks in smaller batches to allow cancellation responsiveness
            const int saveBatch = 1000;
            for (int i = 0; i < parentChunks.Count; i += saveBatch)
            {
                ct.ThrowIfCancellationRequested();
                var batch = parentChunks.Skip(i).Take(saveBatch).ToList();
                await _store.SaveChunksBatchAsync(batch, ct);
                progress?.Report(new IngestProgress("Storing", Math.Min(i + saveBatch, total), total, "Writing parent chunks"));
            }
        }

        ct.ThrowIfCancellationRequested();
        // save main chunks in batches for responsiveness
        const int mainSaveBatch = 1000;
        for (int i = 0; i < allChunks.Count; i += mainSaveBatch)
        {
            ct.ThrowIfCancellationRequested();
            var batch = allChunks.Skip(i).Take(mainSaveBatch).ToList();
            await _store.SaveChunksBatchAsync(batch, ct);
            progress?.Report(new IngestProgress("Storing", Math.Min(i + mainSaveBatch, total), total, $"Writing chunks {i}-{Math.Min(i + mainSaveBatch, total)}"));
        }

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

    private async Task<List<float[]>> EmbedBatchWithRetryAsync(IReadOnlyList<RagChunk> batch, RagDatasetConfig cfg, CancellationToken ct)
    {
        Exception? lastError = null;
        foreach (var tokenLimit in EmbeddingInputRetryTokenLimits)
        {
            var texts = batch.Select(c => BuildEmbeddingInput(c, cfg, tokenLimit)).ToList();
            try
            {
                return await _embed.EmbedBatchAsync(texts, ct);
            }
            catch (InvalidOperationException ex) when (LooksLikeOversizedEmbeddingInput(ex))
            {
                lastError = ex;
            }
        }

        throw lastError ?? new InvalidOperationException("Embedding failed for an unknown reason.");
    }

    private static bool LooksLikeOversizedEmbeddingInput(Exception ex)
    {
        var message = ex.Message;
        return message.Contains("too large", StringComparison.OrdinalIgnoreCase)
            || message.Contains("physical batch size", StringComparison.OrdinalIgnoreCase)
            || message.Contains("context size", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildEmbeddingInput(RagChunk chunk, RagDatasetConfig cfg, int maxTokens = MaxEmbeddingInputTokens)
    {
        var text = BuildEmbeddingText(chunk, cfg);
        return ClampEmbeddingInput(text, maxTokens);
    }

    private static string ClampEmbeddingInput(string text, int maxTokens)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        if (ParagraphChunker.EstimateTokens(text) <= maxTokens)
            return text;

        var maxChars = Math.Max(128, maxTokens * 4);
        if (text.Length <= maxChars)
            return text;

        var trimmed = text[..maxChars];
        var boundary = trimmed.LastIndexOfAny(['.', '!', '?', '\n', ' ']);
        if (boundary > maxChars / 2)
            trimmed = trimmed[..boundary];

        return trimmed.Trim();
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
