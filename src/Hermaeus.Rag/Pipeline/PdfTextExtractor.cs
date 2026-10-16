using System.Text;
using UglyToad.PdfPig;

namespace Hermaeus.Rag.Pipeline;

public sealed record PdfTextExtractionResult(
    string Text,
    int PageCount,
    bool HasText);

public static class PdfTextExtractor
{
    public static Task<PdfTextExtractionResult> ExtractAsync(string path, CancellationToken ct = default)
        => ExtractAsync(path, RagIngestLimits.Default, null, ct);

    /// <summary>Apply a caller's existing lower source and UTF-8 output budgets during extraction.</summary>
    public static Task<PdfTextExtractionResult> ExtractAsync(
        string path, long maxSourceBytes, int maxExtractedUtf8Bytes, CancellationToken ct = default)
    {
        if (maxSourceBytes <= 0 || maxExtractedUtf8Bytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSourceBytes), "PDF limits must be positive.");
        return ExtractAsync(path, RagIngestLimits.Default with
        {
            MaxSourceBytes = Math.Min(maxSourceBytes, RagIngestLimits.Default.MaxSourceBytes),
            MaxExtractedCharacters = Math.Min(maxExtractedUtf8Bytes, RagIngestLimits.Default.MaxExtractedCharacters)
        }, maxExtractedUtf8Bytes, ct);
    }

    internal static Task<PdfTextExtractionResult> ExtractAsync(
        string path, RagIngestLimits limits, int? maxExtractedUtf8Bytes, CancellationToken ct = default)
    {
        limits.Validate();
        ct.ThrowIfCancellationRequested();
        using var file = File.OpenRead(path);
        using var source = new LimitedSourceStream(file, limits.MaxSourceBytes, path, ct, leaveOpen: true);
        // PdfPig may allocate while opening or constructing a page. These checks
        // bound retained output and source extent, not parser RSS or wall time.
        using var document = PdfDocument.Open(source);
        if (document.NumberOfPages > limits.MaxPdfPages)
            throw new InvalidOperationException($"PDF exceeds the {limits.MaxPdfPages} page ingest limit: {path}");
        var sb = new StringBuilder();
        var pageCount = 0;
        long utf8Bytes = 0;

        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            pageCount++;
            if (pageCount > limits.MaxPdfPages)
                throw new InvalidOperationException($"PDF exceeds the {limits.MaxPdfPages} page ingest limit: {path}");
            var text = page.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var marker = $"[Page {page.Number}]";
            // Match the existing final text exactly: three line breaks between
            // nonempty pages, one after each marker, none at the end.
            var separators = sb.Length == 0 ? string.Empty : string.Concat(Environment.NewLine, Environment.NewLine, Environment.NewLine);
            var additionalCharacters = (long)separators.Length + marker.Length + Environment.NewLine.Length + text.Length;
            if (additionalCharacters > limits.MaxExtractedCharacters - sb.Length)
                throw new InvalidOperationException($"Extracted PDF text exceeds the {limits.MaxExtractedCharacters} character ingest limit: {path}");
            if (maxExtractedUtf8Bytes.HasValue)
            {
                var additionalBytes = (long)Encoding.UTF8.GetByteCount(separators) + Encoding.UTF8.GetByteCount(marker)
                    + Encoding.UTF8.GetByteCount(Environment.NewLine) + Encoding.UTF8.GetByteCount(text);
                if (additionalBytes > maxExtractedUtf8Bytes.Value - utf8Bytes)
                    throw new InvalidOperationException($"Extracted PDF text exceeds the {maxExtractedUtf8Bytes.Value} byte limit: {path}");
                utf8Bytes += additionalBytes;
            }
            sb.Append(separators).Append(marker).Append(Environment.NewLine).Append(text);
        }

        var content = sb.ToString();
        return Task.FromResult(new PdfTextExtractionResult(content, pageCount, !string.IsNullOrWhiteSpace(content)));
    }
}
