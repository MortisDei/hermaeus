using System.Text;
using System.Text.RegularExpressions;
using Aether.Rag.Models;

namespace Aether.Rag.Chunking;

/// <summary>
/// Splits documents into structure-aware chunks.
/// Markdown headings, code symbols, PDF page markers, log events and web pages
/// are separated before the standard overlapping chunking pass.
/// </summary>
public sealed class ParagraphChunker
{
    private const int CharsPerToken = 4;

    private static readonly Regex ParaSplit = new(@"\n\s*\n", RegexOptions.Compiled);
    private static readonly Regex SentenceEnd = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);
    private static readonly Regex MarkdownHeading = new(@"^(#{1,6})\s+(?<title>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex MarkdownFence = new(@"^```", RegexOptions.Compiled);
    private static readonly Regex CodeNamespace = new(@"^\s*namespace\s+(?<name>[A-Za-z_][\w.]*)\s*$", RegexOptions.Compiled);
    private static readonly Regex CodeType = new(@"^\s*(?:public|internal|private|protected|static|partial|sealed|abstract|record|new|unsafe|extern|async|virtual|override|readonly|\s)*(?:class|struct|interface|enum|record)\s+(?<name>[A-Za-z_][\w]*)", RegexOptions.Compiled);
    private static readonly Regex CodeSymbol = new(@"^\s*(?:public|internal|private|protected|static|partial|sealed|abstract|virtual|override|async|unsafe|extern|new|readonly|\s)+[\w<>,\[\]\?\s]+\s+(?<name>[A-Za-z_][\w]*)\s*\(", RegexOptions.Compiled);
    private static readonly Regex LogLine = new(@"^(?<timestamp>\d{4}-\d{2}-\d{2}[T\s]\d{2}:\d{2}:\d{2}(?:[.,]\d+)?(?:Z|[+-]\d{2}:?\d{2})?)?\s*(?<level>TRACE|DEBUG|INFO|WARN|WARNING|ERROR|FATAL)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PageMarker = new(@"^\[Page\s+(?<page>\d+)\]\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public List<TextChunk> Chunk(string text, string sourceFile, string sourceTitle, RagDatasetConfig cfg)
    {
        var units = DetectUnits(text, sourceFile, sourceTitle);
        return cfg.UseParentChild
            ? ChunkParentChild(units, cfg)
            : BuildChunks(units, cfg.TargetChunkChars, cfg.OverlapChars, null);
    }

    private static List<TextChunk> ChunkParentChild(List<ChunkUnit> units, RagDatasetConfig cfg)
    {
        var parentChunks = BuildChunks(units, cfg.ParentChunkChars, cfg.OverlapChars, null);
        var result = new List<TextChunk>();

        foreach (var parent in parentChunks)
        {
            var parentUnit = new ChunkUnit(
                parent.Content,
                parent.ChunkKind,
                parent.SourceFile,
                parent.SourceTitle,
                parent.HeadingPath,
                parent.CodeSymbolInfo,
                parent.PageNumber,
                parent.EventType,
                parent.SourceUrl);

            var childUnits = SplitChildUnits(parentUnit);
            result.AddRange(BuildChunks(childUnits, cfg.TargetChunkChars, cfg.OverlapChars / 2, parent.Content));
        }

        return Normalize(result);
    }

    private static List<TextChunk> BuildChunks(List<ChunkUnit> units, int targetChars, int overlapChars, string? parentContent)
    {
        if (units.Count == 0)
            return [];

        var result = new List<TextChunk>();
        var buffer = new List<ChunkUnit>();
        var builder = new StringBuilder();

        foreach (var unit in units)
        {
            if (buffer.Count > 0 && builder.Length + unit.Content.Length > targetChars)
            {
                result.Add(CreateChunk(buffer, builder.ToString().Trim(), parentContent));
                var overlap = GetOverlapSuffix(builder.ToString(), overlapChars);
                buffer.Clear();
                builder.Clear();

                if (!string.IsNullOrWhiteSpace(overlap))
                    builder.Append(overlap);
            }

            if (builder.Length > 0)
                builder.AppendLine().AppendLine();

            builder.Append(unit.Content);
            buffer.Add(unit);
        }

        if (buffer.Count > 0 && !string.IsNullOrWhiteSpace(builder.ToString()))
            result.Add(CreateChunk(buffer, builder.ToString().Trim(), parentContent));

        return Normalize(result);
    }

    private static TextChunk CreateChunk(List<ChunkUnit> buffer, string content, string? parentContent)
    {
        var first = buffer[0];
        return new TextChunk(
            content,
            first.SourceFile,
            first.SourceTitle,
            0,
            0,
            parentContent,
            first.ChunkKind,
            MergePath(buffer.Select(b => b.HeadingPath)),
            MergePath(buffer.Select(b => b.CodeSymbolInfo)),
            buffer.Select(b => b.PageNumber).FirstOrDefault(v => v.HasValue),
            MergePath(buffer.Select(b => b.EventType)),
            MergePath(buffer.Select(b => b.SourceUrl)));
    }

    private static List<TextChunk> Normalize(List<TextChunk> chunks)
    {
        for (var i = 0; i < chunks.Count; i++)
            chunks[i] = chunks[i] with { Index = i, Total = chunks.Count };

        return chunks;
    }

    private static string? MergePath(IEnumerable<string?> values)
    {
        var list = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return list.Count == 0 ? null : string.Join(" / ", list);
    }

    private static List<ChunkUnit> DetectUnits(string text, string sourceFile, string sourceTitle)
    {
        if (LooksLikeWebSource(sourceFile))
            return SplitWeb(text, sourceFile, sourceTitle);

        var extension = Path.GetExtension(sourceFile).ToLowerInvariant();
        if (extension is ".md" or ".markdown" or ".mdown" or ".mkdn" || LooksLikeMarkdown(text))
            return SplitMarkdown(text, sourceFile, sourceTitle);

        if (extension == ".pdf")
            return SplitPdf(text, sourceFile, sourceTitle);

        if (extension is ".cs" or ".fs" or ".ts" or ".js" or ".jsx" or ".tsx" or ".java" or ".py" or ".go" or ".rs" or ".cpp" or ".c" or ".h" or ".hpp" or ".rb" or ".php")
            return SplitCode(text, sourceFile, sourceTitle);

        if (LooksLikeLog(text))
            return SplitLogs(text, sourceFile, sourceTitle);

        return SplitPlain(text, sourceFile, sourceTitle);
    }

    private static List<ChunkUnit> SplitPlain(string text, string sourceFile, string sourceTitle)
    {
        var paragraphs = ParaSplit.Split(text)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (paragraphs.Count == 0)
            return [new ChunkUnit(text.Trim(), RagChunkKind.PlainText, sourceFile, sourceTitle)];

        return paragraphs.Select(p => new ChunkUnit(p, RagChunkKind.PlainText, sourceFile, sourceTitle)).ToList();
    }

    private static List<ChunkUnit> SplitWeb(string text, string sourceFile, string sourceTitle)
    {
        var paragraphs = ParaSplit.Split(text)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (paragraphs.Count == 0)
            return [new ChunkUnit(text.Trim(), RagChunkKind.WebPageSection, sourceFile, sourceTitle, sourceTitle, null, null, null, sourceFile)];

        return paragraphs
            .Select(p => new ChunkUnit(p, RagChunkKind.WebPageSection, sourceFile, sourceTitle, sourceTitle, null, null, null, sourceFile))
            .ToList();
    }

    private static List<ChunkUnit> SplitPdf(string text, string sourceFile, string sourceTitle)
    {
        var units = new List<ChunkUnit>();
        var buffer = new StringBuilder();
        var pageNumber = (int?)null;
        var seenPages = 0;

        foreach (var line in text.Split('\n'))
        {
            var marker = PageMarker.Match(line.Trim());
            if (marker.Success)
            {
                FlushBuffer(units, buffer, sourceFile, sourceTitle, RagChunkKind.PdfPageSection, pageNumber is null ? null : $"Page {pageNumber}", null, pageNumber, null, null);
                pageNumber = int.TryParse(marker.Groups["page"].Value, out var parsed) ? parsed : null;
                seenPages++;
                continue;
            }

            if (buffer.Length > 0)
                buffer.AppendLine();
            buffer.Append(line);
        }

        FlushBuffer(units, buffer, sourceFile, sourceTitle, RagChunkKind.PdfPageSection, pageNumber is null ? null : $"Page {pageNumber}", null, pageNumber, null, null);

        if (units.Count == 0)
            return [new ChunkUnit(text.Trim(), RagChunkKind.PdfPageSection, sourceFile, sourceTitle, sourceTitle, null, seenPages > 0 ? seenPages : null, null, null)];

        return units;
    }

    private static List<ChunkUnit> SplitLogs(string text, string sourceFile, string sourceTitle)
    {
        var units = new List<ChunkUnit>();
        var buffer = new StringBuilder();
        string? severity = null;

        foreach (var line in text.Split('\n'))
        {
            var match = LogLine.Match(line.Trim());
            if (match.Success && buffer.Length > 0)
            {
                FlushBuffer(units, buffer, sourceFile, sourceTitle, RagChunkKind.LogEvent, null, null, null, severity, null);
            }

            if (match.Success)
                severity = match.Groups["level"].Value.ToUpperInvariant();

            if (buffer.Length > 0)
                buffer.AppendLine();
            buffer.Append(line);
        }

        FlushBuffer(units, buffer, sourceFile, sourceTitle, RagChunkKind.LogEvent, null, null, null, severity, null);

        if (units.Count == 0)
            return [new ChunkUnit(text.Trim(), RagChunkKind.LogEvent, sourceFile, sourceTitle, null, null, null, null, null)];

        return units;
    }

    private static List<ChunkUnit> SplitCode(string text, string sourceFile, string sourceTitle)
    {
        var units = new List<ChunkUnit>();
        var buffer = new StringBuilder();
        var namespaceName = string.Empty;
        var className = string.Empty;
        var symbolName = string.Empty;

        foreach (var line in text.Split('\n'))
        {
            var namespaceMatch = CodeNamespace.Match(line);
            var typeMatch = CodeType.Match(line);
            var symbolMatch = CodeSymbol.Match(line);

            if (namespaceMatch.Success || typeMatch.Success || symbolMatch.Success)
            {
                FlushCodeBuffer(units, buffer, sourceFile, sourceTitle, namespaceName, className, symbolName);

                if (namespaceMatch.Success)
                {
                    namespaceName = namespaceMatch.Groups["name"].Value;
                    className = string.Empty;
                    symbolName = string.Empty;
                }
                else if (typeMatch.Success)
                {
                    className = typeMatch.Groups["name"].Value;
                    symbolName = string.Empty;
                }
                else
                {
                    symbolName = symbolMatch.Groups["name"].Value;
                }
            }

            if (buffer.Length > 0)
                buffer.AppendLine();
            buffer.Append(line);
        }

        FlushCodeBuffer(units, buffer, sourceFile, sourceTitle, namespaceName, className, symbolName);

        if (units.Count == 0)
            return [new ChunkUnit(text.Trim(), RagChunkKind.CodeSymbol, sourceFile, sourceTitle, null, BuildCodeInfo(namespaceName, className, symbolName), null, null, null)];

        return units;
    }

    private static List<ChunkUnit> SplitMarkdown(string text, string sourceFile, string sourceTitle)
    {
        var units = new List<ChunkUnit>();
        var buffer = new StringBuilder();
        var headingStack = new List<(int Level, string Title)>();
        var inFence = false;
        string? headingPath = null;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (MarkdownFence.IsMatch(trimmed))
            {
                inFence = !inFence;
                if (buffer.Length > 0)
                    buffer.AppendLine();
                buffer.Append(line);
                continue;
            }

            if (!inFence)
            {
                var headingMatch = MarkdownHeading.Match(trimmed);
                if (headingMatch.Success)
                {
                    FlushBuffer(units, buffer, sourceFile, sourceTitle, RagChunkKind.MarkdownSection, headingPath ?? sourceTitle, null, null, null, null);
                    var level = headingMatch.Groups[1].Value.Length;
                    var title = headingMatch.Groups["title"].Value.Trim();
                    headingStack.RemoveAll(h => h.Level >= level);
                    headingStack.Add((level, title));
                    headingPath = string.Join(" / ", headingStack.Select(h => $"{new string('#', h.Level)} {h.Title}"));
                }
            }

            if (buffer.Length > 0)
                buffer.AppendLine();
            buffer.Append(line);
        }

        FlushBuffer(units, buffer, sourceFile, sourceTitle, RagChunkKind.MarkdownSection, headingPath ?? sourceTitle, null, null, null, null);

        if (units.Count == 0)
            return [new ChunkUnit(text.Trim(), RagChunkKind.MarkdownSection, sourceFile, sourceTitle, sourceTitle, null, null, null, null)];

        return units;
    }

    private static List<ChunkUnit> SplitChildUnits(ChunkUnit unit)
    {
        return unit.ChunkKind switch
        {
            RagChunkKind.LogEvent => SplitByLines(unit),
            RagChunkKind.PdfPageSection or RagChunkKind.MarkdownSection or RagChunkKind.CodeSymbol or RagChunkKind.WebPageSection => SplitByParagraphs(unit),
            _ => SplitByParagraphs(unit)
        };
    }

    private static List<ChunkUnit> SplitByParagraphs(ChunkUnit unit)
    {
        var paragraphs = ParaSplit.Split(unit.Content)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (paragraphs.Count == 0)
            return [unit];

        return paragraphs
            .Select(p => unit with { Content = p })
            .ToList();
    }

    private static List<ChunkUnit> SplitByLines(ChunkUnit unit)
    {
        var lines = unit.Content.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0)
            return [unit];

        return lines.Select(l => unit with { Content = l }).ToList();
    }

    private static void FlushBuffer(List<ChunkUnit> units, StringBuilder buffer, string sourceFile, string sourceTitle, RagChunkKind kind, string? headingPath, string? codeInfo, int? pageNumber, string? eventType, string? sourceUrl)
    {
        var content = buffer.ToString().Trim();
        if (string.IsNullOrWhiteSpace(content))
            return;

        units.Add(new ChunkUnit(content, kind, sourceFile, sourceTitle, headingPath, codeInfo, pageNumber, eventType, sourceUrl));
        buffer.Clear();
    }

    private static void FlushCodeBuffer(List<ChunkUnit> units, StringBuilder buffer, string sourceFile, string sourceTitle, string namespaceName, string className, string symbolName)
    {
        var content = buffer.ToString().Trim();
        if (string.IsNullOrWhiteSpace(content))
            return;

        units.Add(new ChunkUnit(content, RagChunkKind.CodeSymbol, sourceFile, sourceTitle, null, BuildCodeInfo(namespaceName, className, symbolName), null, null, null));
        buffer.Clear();
    }

    private static string? BuildCodeInfo(string namespaceName, string className, string symbolName)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(namespaceName)) parts.Add($"Namespace: {namespaceName}");
        if (!string.IsNullOrWhiteSpace(className)) parts.Add($"Class: {className}");
        if (!string.IsNullOrWhiteSpace(symbolName)) parts.Add($"Symbol: {symbolName}");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static bool LooksLikeWebSource(string sourceFile)
    {
        return Uri.TryCreate(sourceFile, UriKind.Absolute, out var uri)
            && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeMarkdown(string text)
    {
        return text.Split('\n').Take(20).Any(line => MarkdownHeading.IsMatch(line.Trim()) || MarkdownFence.IsMatch(line.Trim()) || line.TrimStart().StartsWith("- ", StringComparison.Ordinal) || line.TrimStart().StartsWith("* ", StringComparison.Ordinal));
    }

    private static bool LooksLikeLog(string text)
    {
        return text.Split('\n').Take(20).Any(line => LogLine.IsMatch(line.Trim()));
    }

    private static string GetOverlapSuffix(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text) || maxChars <= 0)
            return string.Empty;

        if (text.Length <= maxChars)
            return text;

        var sentences = SentenceEnd.Split(text);
        var builder = new StringBuilder();
        foreach (var sentence in sentences.Reverse())
        {
            var trimmed = sentence.Trim();
            if (trimmed.Length == 0)
                continue;

            if (builder.Length + trimmed.Length > maxChars)
                break;

            builder.Insert(0, trimmed + " ");
        }

        return builder.ToString().Trim();
    }

    public static int EstimateTokens(string text) => text.Length / CharsPerToken;

    private sealed record ChunkUnit(
        string Content,
        RagChunkKind ChunkKind,
        string SourceFile,
        string SourceTitle,
        string? HeadingPath = null,
        string? CodeSymbolInfo = null,
        int? PageNumber = null,
        string? EventType = null,
        string? SourceUrl = null);
}