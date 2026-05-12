using System.Text;
using UglyToad.PdfPig;

namespace Aether.Rag.Pipeline;

public sealed record PdfTextExtractionResult(
    string Text,
    int PageCount,
    bool HasText);

public static class PdfTextExtractor
{
    public static Task<PdfTextExtractionResult> ExtractAsync(string path, CancellationToken ct = default)
    {
        using var document = PdfDocument.Open(path);
        var sb = new StringBuilder();
        var pageCount = 0;

        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            pageCount++;
            var text = page.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (sb.Length > 0)
                sb.AppendLine().AppendLine();
            sb.AppendLine($"[Page {page.Number}]");
            sb.AppendLine(text);
        }

        var content = sb.ToString().Trim();
        return Task.FromResult(new PdfTextExtractionResult(content, pageCount, !string.IsNullOrWhiteSpace(content)));
    }
}
