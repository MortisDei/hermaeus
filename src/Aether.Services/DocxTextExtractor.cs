using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Aether.Services;

public enum FileTextExtractionStatus
{
    Success,
    Skipped
}

public sealed record FileTextExtractionResult(FileTextExtractionStatus Status, string Text, string Reason)
{
    public static FileTextExtractionResult Ok(string text) => new(FileTextExtractionStatus.Success, text, string.Empty);
    public static FileTextExtractionResult Skip(string reason) => new(FileTextExtractionStatus.Skipped, string.Empty, reason);
}

/// <summary>
/// Extracts plain text from a .docx file (a zip containing word/document.xml)
/// using only the BCL (r19 5.1 - no new NuGet packages). Paragraphs and tabs/
/// breaks become plain text with paragraph breaks; tables become tab-separated
/// rows. Headers, footnotes, and images are ignored in v1.
/// </summary>
public static class DocxTextExtractor
{
    /// <summary>Guards against a decompression bomb: a legitimate document.xml is small
    /// even for a large document, since formatting/media live in separate zip entries.</summary>
    private const long MaxDocumentXmlBytes = 8 * 1024 * 1024;

    public static FileTextExtractionResult Extract(Stream docxStream)
    {
        try
        {
            using var archive = new ZipArchive(docxStream, ZipArchiveMode.Read, leaveOpen: true);
            var entry = archive.GetEntry("word/document.xml");
            if (entry is null)
                return FileTextExtractionResult.Skip("Not a valid .docx file (no word/document.xml entry).");
            if (entry.Length > MaxDocumentXmlBytes)
                return FileTextExtractionResult.Skip($"document.xml is larger than {MaxDocumentXmlBytes / (1024 * 1024)} MB uncompressed.");

            using var entryStream = entry.Open();
            var doc = XDocument.Load(entryStream);
            var body = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "body");
            if (body is null)
                return FileTextExtractionResult.Skip("document.xml has no body element.");

            var sb = new StringBuilder();
            foreach (var block in body.Elements())
                AppendBlock(sb, block);

            return FileTextExtractionResult.Ok(sb.ToString().Trim());
        }
        catch (InvalidDataException)
        {
            return FileTextExtractionResult.Skip("Not a valid zip/docx archive.");
        }
        catch (Exception ex)
        {
            return FileTextExtractionResult.Skip($"Could not parse document.xml: {ex.Message}");
        }
    }

    private static void AppendBlock(StringBuilder sb, XElement element)
    {
        switch (element.Name.LocalName)
        {
            case "p":
                AppendParagraphText(sb, element);
                sb.Append('\n').Append('\n');
                break;
            case "tbl":
                AppendTable(sb, element);
                break;
        }
    }

    private static void AppendParagraphText(StringBuilder sb, XElement paragraph)
    {
        foreach (var node in paragraph.Descendants())
        {
            switch (node.Name.LocalName)
            {
                case "t":
                    sb.Append(node.Value);
                    break;
                case "tab":
                    sb.Append('\t');
                    break;
                case "br":
                case "cr":
                    sb.Append('\n');
                    break;
            }
        }
    }

    private static void AppendTable(StringBuilder sb, XElement table)
    {
        foreach (var row in table.Elements().Where(e => e.Name.LocalName == "tr"))
        {
            var cells = row.Elements().Where(e => e.Name.LocalName == "tc").Select(cell =>
            {
                var cellText = new StringBuilder();
                foreach (var p in cell.Elements().Where(e => e.Name.LocalName == "p"))
                    AppendParagraphText(cellText, p);
                return cellText.ToString().Trim();
            });
            sb.Append(string.Join('\t', cells)).Append('\n');
        }
        sb.Append('\n');
    }
}
