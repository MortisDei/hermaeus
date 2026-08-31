using Hermaeus.Rag.Chunking;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Core.Services;
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
    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

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
        // instead of the directory-wide .txt/.md/.pdf scan below.
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

        var existingChunks = await _store.GetStoredChunksAsync(dataset.Id, includeEmbeddings: true, ct);
        var existingHashes = await _store.GetSourceHashesAsync(dataset.Id, files, ct);

        var health = new RagIngestHealth { FileCount = files.Count };
        AddChunkSizeGuardWarning(health, dataset.Config);
        var duplicateKeys = new HashSet<string>(StringComparer.Ordinal);
        var totalChunksSeen = 0;

        var newChunks = new List<RagChunk>();
        var changedSourcePaths = new HashSet<string>(PathComparer);
        var sourceDescriptors = new Dictionary<string, RagSourceDescriptor>(StringComparer.Ordinal);
        var sourceRevisions = new Dictionary<string, RagSourceRevision>(StringComparer.Ordinal);

        // ── 1. Chunk and embed in file batches, but do not publish yet ─────
        // Every database write waits for the complete generation. The old
        // current pointer therefore remains query-visible through failure or
        // cancellation during this whole phase.
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
                        report.Documents.Add(new DocumentIngestReport { Path = document.SourcePath, Status = DocumentIngestStatus.Replaced, Message = "Will replace existing source" });
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

                if (options.DuplicatePolicy == IngestDuplicatePolicy.ReportOnly)
                    continue;

                var watched = FindWatchedSource(dataset, document.SourcePath);
                var existingForSource = existingChunks.FirstOrDefault(c => PathsEqual(c.SourcePath, document.SourcePath));
                var sourceId = string.IsNullOrWhiteSpace(existingForSource?.SourceId)
                    ? RagSourceIdentity.ForSource(dataset.Id, watched, document.SourcePath)
                    : existingForSource.SourceId;
                var previousRevisionId = existingForSource?.SourceRevisionId;
                var revisionId = existingForSource is not null
                    && string.Equals(existingForSource.SourceHash, document.SourceHash, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(existingForSource.SourceRevisionId)
                    ? existingForSource.SourceRevisionId
                    : $"revision:{Guid.NewGuid():N}";
                sourceDescriptors[sourceId] = new RagSourceDescriptor(
                    sourceId, dataset.Id, watched?.WatchRootId,
                    RagSourceIdentity.RelativeLocator(watched, document.SourcePath), RagSourceKind.LocalFile,
                    watched?.LastConfirmedRootIdentity);
                sourceRevisions[revisionId] = new RagSourceRevision(
                    revisionId, sourceId, document.SourceHash,
                    $"local file {RagSourceIdentity.RelativeLocator(watched, document.SourcePath)}",
                    EmbeddingIdentity(dataset), RagSourceRevisionState.Staged, DateTime.UtcNow,
                    string.Equals(revisionId, previousRevisionId, StringComparison.Ordinal) ? null : previousRevisionId,
                    document.ModifiedUtc);
                changedSourcePaths.Add(document.SourcePath);

                var textChunks = _chunker.Chunk(document.Text, file, document.Title, dataset.Config);

                foreach (var tc in textChunks)
                {
                    if (string.IsNullOrWhiteSpace(tc.Content))
                        health.EmptyChunkCount++;

                    var chunk = CreateChunk(dataset.Id, document.SourceFile, document.SourcePath, document.SourceHash, document.ModifiedUtc, document.Title, tc);
                    chunk.SourceId = sourceId;
                    chunk.SourceRevisionId = revisionId;

                    if (!duplicateKeys.Add($"{chunk.SourcePath}\n{chunk.Content}"))
                        health.DuplicateChunkCount++;

                    if (tc.ParentContent is not null)
                    {
                        // Parent chunk (stored but not embedded for indexing)
                        var parentId = Guid.NewGuid().ToString();
                        var parent = CreateChunk(dataset.Id, document.SourceFile, document.SourcePath, document.SourceHash, document.ModifiedUtc, document.Title, tc, tc.ParentContent, parentId, null);
                        parent.SourceId = sourceId;
                        parent.SourceRevisionId = revisionId;
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
            newChunks.AddRange(batchParents);
            newChunks.AddRange(batchChunks);
        }

        AddHealthWarnings(health);

        // ── 2. If dry-run/report-only, skip embedding and storage ─────────
        if (options.DryRun || options.DuplicatePolicy == IngestDuplicatePolicy.ReportOnly)
        {
            report.Health = health;
            progress?.Report(new IngestProgress("Done", totalChunksSeen, totalChunksSeen, $"Dry-run complete. {report.Summary()}"));
            return report;
        }

        progress?.Report(new IngestProgress("Storing", files.Count, files.Count,
            "Publishing complete dataset generation...", overallTotal - 1, overallTotal, "Atomic publication"));
        var retained = existingChunks.Where(c => !changedSourcePaths.Contains(c.SourcePath)).ToList();
        await PublishSnapshotAsync(dataset, retained.Concat(newChunks).ToList(), sourceDescriptors, sourceRevisions, progress, overallTotal, ct);
        var indexedChunkCount = retained.Concat(newChunks).Count(c => !c.IsParent);

        progress?.Report(new IngestProgress("Done", indexedChunkCount, indexedChunkCount,
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
        var chunks = await _store.GetStoredChunksAsync(dataset.Id, includeEmbeddings: false, ct);
        if (chunks.Count == 0)
        {
            progress?.Report(new IngestProgress("Done", 0, 0, "Nothing to reindex."));
            return 0;
        }

        progress?.Report(new IngestProgress("Embedding", 0, chunks.Count, $"Reindexing {chunks.Count} chunk(s)..."));

        var workingChunks = chunks.Select(CloneChunkForWorking).ToList();
        var embeddedChunks = workingChunks.Where(c => !c.IsParent).ToList();
        dataset.Config.EmbeddingDimensions = 0;
        await EmbedChunksAsync(dataset, embeddedChunks, progress, ct);
        progress?.Report(new IngestProgress("Indexing", 0, 1, "Building BM25 stats..."));
        ct.ThrowIfCancellationRequested();
        await PublishSnapshotAsync(dataset, workingChunks,
            new Dictionary<string, RagSourceDescriptor>(),
            new Dictionary<string, RagSourceRevision>(), progress, 1, ct);

        progress?.Report(new IngestProgress("Done", embeddedChunks.Count, embeddedChunks.Count, $"Reindex complete: {embeddedChunks.Count} chunk(s)."));
        return embeddedChunks.Count;
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
        var sourcePaths = new HashSet<string>(PathComparer);
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
        var existing = await _store.GetStoredChunksAsync(dataset.Id, includeEmbeddings: true, ct);
        AssignLineage(dataset, allChunks, existing, sourceKind: RagSourceKind.WebUrl);
        AssignLineage(dataset, parentChunks, existing, sourceKind: RagSourceKind.WebUrl);
        await EmbedChunksAsync(dataset, allChunks, progress, ct);
        await PublishSnapshotAsync(
            dataset,
            existing.Where(c => !changedSourcePaths.Contains(c.SourcePath)).Concat(parentChunks).Concat(allChunks).ToList(),
            new Dictionary<string, RagSourceDescriptor>(),
            new Dictionary<string, RagSourceRevision>(), progress, 1, ct);
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

    private async Task PublishSnapshotAsync(
        RagDataset dataset,
        List<RagChunk> chunks,
        IReadOnlyDictionary<string, RagSourceDescriptor> suppliedSources,
        IReadOnlyDictionary<string, RagSourceRevision> suppliedRevisions,
        IProgress<IngestProgress>? progress,
        int overallTotal,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        NormalizeWatchedSources(dataset);
        var existing = await _store.GetStoredChunksAsync(dataset.Id, includeEmbeddings: true, ct);
        AssignLineage(dataset, chunks, existing, RagSourceKind.LocalFile);

        var sources = suppliedSources.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var revisions = suppliedRevisions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var group in chunks.GroupBy(c => c.SourceId, StringComparer.Ordinal))
        {
            var sample = group.First();
            var watched = FindWatchedSource(dataset, sample.SourcePath);
            var isWeb = sample.SourcePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || sample.SourcePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            sources[group.Key] = new RagSourceDescriptor(
                group.Key, dataset.Id, isWeb ? null : watched?.WatchRootId,
                isWeb ? sample.SourcePath : RagSourceIdentity.RelativeLocator(watched, sample.SourcePath),
                isWeb ? RagSourceKind.WebUrl : RagSourceKind.LocalFile,
                isWeb ? null : watched?.LastConfirmedRootIdentity);

            var revision = group.First().SourceRevisionId;
            if (!revisions.ContainsKey(revision))
            {
                var previous = existing.FirstOrDefault(c => c.SourceId == group.Key)?.SourceRevisionId;
                revisions[revision] = new RagSourceRevision(
                    revision, group.Key, sample.SourceHash, "Indexed source content",
                    EmbeddingIdentity(dataset), RagSourceRevisionState.Staged, DateTime.UtcNow,
                    string.Equals(previous, revision, StringComparison.Ordinal) ? null : previous,
                    sample.SourceModifiedUtc);
            }
        }

        var prepared = CloneForGeneration(chunks);
        var embedded = prepared.Where(c => !c.IsParent).ToList();
        var dimensions = embedded.Count == 0 ? 0 : embedded[0].Embedding.Length;
        dataset.Config.EmbeddingDimensions = dimensions;
        var stats = Bm25Scorer.BuildStats(embedded);
        await ValidateStagedLocalSourcesAsync(dataset, chunks, ct);
        progress?.Report(new IngestProgress("Indexing", 0, 1,
            "Building BM25 stats and publishing atomically...", overallTotal - 1, overallTotal, "Atomic publication"));
        await _store.PublishGenerationAsync(
            dataset, prepared, stats, [.. sources.Values], [.. revisions.Values],
            EmbeddingIdentity(dataset), dimensions, ct);
    }

    private void AssignLineage(
        RagDataset dataset,
        IReadOnlyList<RagChunk> chunks,
        IReadOnlyList<RagChunk> existing,
        RagSourceKind sourceKind)
    {
        foreach (var chunk in chunks)
        {
            var existingForSource = existing.FirstOrDefault(c => PathsEqual(c.SourcePath, chunk.SourcePath));
            var watched = sourceKind == RagSourceKind.LocalFile
                ? FindWatchedSource(dataset, chunk.SourcePath)
                : null;
            if (string.IsNullOrWhiteSpace(chunk.SourceId))
            {
                chunk.SourceId = existingForSource is not null && !string.IsNullOrWhiteSpace(existingForSource.SourceId)
                    ? existingForSource.SourceId
                    : RagSourceIdentity.ForSource(dataset.Id, watched, chunk.SourcePath);
            }

            if (string.IsNullOrWhiteSpace(chunk.SourceRevisionId))
            {
                chunk.SourceRevisionId = existingForSource is not null
                    && string.Equals(existingForSource.SourceHash, chunk.SourceHash, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(existingForSource.SourceRevisionId)
                    ? existingForSource.SourceRevisionId
                    : $"revision:{Guid.NewGuid():N}";
            }
        }
    }

    private static List<RagChunk> CloneForGeneration(IReadOnlyList<RagChunk> chunks)
    {
        var ids = chunks.ToDictionary(c => c.Id, _ => Guid.NewGuid().ToString(), StringComparer.Ordinal);
        return chunks.Select(chunk => new RagChunk
        {
            Id = ids[chunk.Id],
            DatasetId = chunk.DatasetId,
            SourceFile = chunk.SourceFile,
            SourcePath = chunk.SourcePath,
            SourceHash = chunk.SourceHash,
            SourceId = chunk.SourceId,
            SourceRevisionId = chunk.SourceRevisionId,
            GenerationId = string.Empty,
            SourceModifiedUtc = chunk.SourceModifiedUtc,
            SourceTitle = chunk.SourceTitle,
            Content = chunk.Content,
            ChunkIndex = chunk.ChunkIndex,
            ChunkTotal = chunk.ChunkTotal,
            ParentId = chunk.ParentId is not null && ids.TryGetValue(chunk.ParentId, out var parentId) ? parentId : null,
            IsParent = chunk.IsParent,
            TokenCount = chunk.TokenCount,
            Embedding = [.. chunk.Embedding],
            CreatedAt = chunk.CreatedAt,
            ChunkKind = chunk.ChunkKind,
            HeadingPath = chunk.HeadingPath,
            CodeSymbolInfo = chunk.CodeSymbolInfo,
            PageNumber = chunk.PageNumber,
            EventType = chunk.EventType,
            SourceUrl = chunk.SourceUrl
        }).ToList();
    }

    private static RagChunk CloneChunkForWorking(RagChunk chunk) => new()
    {
        Id = chunk.Id,
        DatasetId = chunk.DatasetId,
        SourceFile = chunk.SourceFile,
        SourcePath = chunk.SourcePath,
        SourceHash = chunk.SourceHash,
        SourceId = chunk.SourceId,
        SourceRevisionId = chunk.SourceRevisionId,
        GenerationId = chunk.GenerationId,
        SourceModifiedUtc = chunk.SourceModifiedUtc,
        SourceTitle = chunk.SourceTitle,
        Content = chunk.Content,
        ChunkIndex = chunk.ChunkIndex,
        ChunkTotal = chunk.ChunkTotal,
        ParentId = chunk.ParentId,
        IsParent = chunk.IsParent,
        TokenCount = chunk.TokenCount,
        Embedding = [],
        CreatedAt = chunk.CreatedAt,
        ChunkKind = chunk.ChunkKind,
        HeadingPath = chunk.HeadingPath,
        CodeSymbolInfo = chunk.CodeSymbolInfo,
        PageNumber = chunk.PageNumber,
        EventType = chunk.EventType,
        SourceUrl = chunk.SourceUrl
    };

    private static RagWatchedSource? FindWatchedSource(RagDataset dataset, string sourcePath)
    {
        foreach (var watched in dataset.Config.WatchedSources)
        {
            if (!PathRootValidator.TryValidate(watched.Root, out var root, out _))
                continue;
            var relative = Path.GetRelativePath(root, sourcePath);
            if (!relative.StartsWith("..", StringComparison.Ordinal)
                && !Path.IsPathRooted(relative))
                return watched;
        }

        return null;
    }

    private static bool PathsEqual(string left, string right) =>
        PathComparer.Equals(left, right);

    private static async Task ValidateStagedLocalSourcesAsync(
        RagDataset dataset, IReadOnlyList<RagChunk> chunks, CancellationToken ct)
    {
        var staged = chunks
            .Where(chunk => string.IsNullOrWhiteSpace(chunk.GenerationId)
                && !chunk.SourcePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !chunk.SourcePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .GroupBy(chunk => chunk.SourcePath, PathComparer);

        foreach (var group in staged)
        {
            ct.ThrowIfCancellationRequested();
            var sourcePath = group.Key;
            var watched = FindWatchedSource(dataset, sourcePath);
            if (watched is not null)
            {
                if (!PathRootValidator.TryValidate(watched.Root, out var root, out var rootError))
                    throw new InvalidOperationException($"Cannot publish {sourcePath}: {rootError}");
                var currentIdentity = RagSourceIdentity.TryGetRootIdentity(root);
                if (currentIdentity is null || string.IsNullOrWhiteSpace(watched.LastConfirmedRootIdentity))
                    throw new InvalidOperationException($"Cannot publish {sourcePath}: watched root identity is Unknown.");
                if (!string.Equals(currentIdentity, watched.LastConfirmedRootIdentity, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Cannot publish {sourcePath}: watched root identity changed.");
                if (!IsSafeFileUnderRoot(root, sourcePath))
                    throw new InvalidOperationException($"Cannot publish {sourcePath}: symbolic-link or reparse ancestor rejected.");
            }

            var document = await LoadLocalDocumentAsync(sourcePath, new RagIngestHealth(), ct)
                ?? throw new InvalidOperationException($"Cannot publish {sourcePath}: source is no longer readable.");
            if (group.Any(chunk => !string.Equals(chunk.SourceHash, document.SourceHash, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Cannot publish {sourcePath}: source content changed during ingest.");
        }
    }

    private static bool IsSafeFileUnderRoot(string root, string file)
    {
        var fullFile = Path.GetFullPath(file);
        var relative = Path.GetRelativePath(root, fullFile);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return false;

        var current = root;
        foreach (var segment in relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        return true;
    }

    private static string EmbeddingIdentity(RagDataset dataset) =>
        string.IsNullOrWhiteSpace(dataset.Config.EmbeddingModel) ? "Unknown" : dataset.Config.EmbeddingModel.Trim();

    private static void NormalizeWatchedSources(RagDataset dataset)
    {
        foreach (var watched in dataset.Config.WatchedSources)
        {
            if (string.IsNullOrWhiteSpace(watched.WatchRootId))
                watched.WatchRootId = RagSourceIdentity.ForWatchedRoot(dataset.Id, watched.Root);
            if (string.IsNullOrWhiteSpace(watched.LastConfirmedRootIdentity))
                watched.LastConfirmedRootIdentity = RagSourceIdentity.TryGetRootIdentity(watched.Root);
        }
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
                var embeddings = await _embed.EmbedBatchAsync(texts, ct);
                if (embeddings.Count != batch.Count)
                    throw new InvalidOperationException($"Embedding service returned {embeddings.Count} vectors for {batch.Count} requested chunks.");
                if (embeddings.Any(vector => vector.Length == 0 || vector.Any(value => !float.IsFinite(value))))
                    throw new InvalidOperationException("Embedding service returned an empty or non-finite vector.");
                var dimensions = embeddings.Select(vector => vector.Length).Distinct().ToList();
                if (dimensions.Count != 1)
                    throw new InvalidOperationException("Embedding service returned mixed vector dimensions in one batch.");
                if (cfg.EmbeddingDimensions > 0 && cfg.EmbeddingDimensions != dimensions[0])
                    throw new InvalidOperationException($"Embedding service returned dimension {dimensions[0]}, expected {cfg.EmbeddingDimensions}.");
                return embeddings;
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
