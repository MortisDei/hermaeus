using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.PdfHelpers;

namespace Hermaeus.Tests;

/// <summary>r19 5.2: .pdf attachments flow through ChatContextAttachment via PdfPig (already a
/// dependency of this solution through Hermaeus.Rag's ingest pipeline, reused here rather than a
/// new package), with an honest refusal for a PDF with no extractable text.</summary>
public sealed class ChatContextAttachmentPdfTests
{
    [Fact]
    public async Task A_pdf_with_real_text_attaches_with_its_extracted_content()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("paper.pdf");
        WriteSimplePdf(path, "Digital PDF alpha beta");

        var attachments = await ChatContextAttachment.LoadFilesAsync([path]);

        var attachment = Assert.Single(attachments);
        Assert.True(attachment.IsReady, attachment.StatusMessage);
        Assert.Contains("Digital PDF alpha beta", attachment.Content);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(attachment.Content), attachment.SizeBytes);
    }

    [Fact]
    public async Task A_pdf_with_no_extractable_text_is_skipped_with_an_honest_reason()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("scan.pdf");
        WriteSimplePdf(path, string.Empty);

        var attachments = await ChatContextAttachment.LoadFilesAsync([path]);

        var attachment = Assert.Single(attachments);
        Assert.Equal(ChatContextAttachmentStatus.Skipped, attachment.Status);
        Assert.Contains("scanned", attachment.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_garbage_file_named_pdf_is_skipped_not_treated_as_an_error()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("not-a-real.pdf");
        await File.WriteAllTextAsync(path, "this is not a pdf file at all");

        var attachments = await ChatContextAttachment.LoadFilesAsync([path]);

        var attachment = Assert.Single(attachments);
        Assert.Equal(ChatContextAttachmentStatus.Skipped, attachment.Status);
        Assert.NotEmpty(attachment.StatusMessage);
    }
}
