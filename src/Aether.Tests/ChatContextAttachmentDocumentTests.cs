using System.IO.Compression;
using System.Text;
using Aether.ViewModels;
using Xunit;

namespace Aether.Tests;

/// <summary>r19 5.1: .docx attachments flow through ChatContextAttachment.LoadFilesAsync using the extracted text's byte count for budget math, not the raw (compressed) file size.</summary>
public sealed class ChatContextAttachmentDocumentTests
{
    private static void WriteMinimalDocx(string path, string paragraphText)
    {
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("word/document.xml");
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
            $"<w:p><w:r><w:t>{paragraphText}</w:t></w:r></w:p>" +
            "</w:body></w:document>");
    }

    [Fact]
    public async Task A_docx_file_attaches_with_its_extracted_text_as_content()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("notes.docx");
        WriteMinimalDocx(path, "Hello from a real docx paragraph.");

        var attachments = await ChatContextAttachment.LoadFilesAsync([path]);

        var attachment = Assert.Single(attachments);
        Assert.True(attachment.IsReady, attachment.StatusMessage);
        Assert.Contains("Hello from a real docx paragraph.", attachment.Content);
        // SizeBytes reflects the extracted text, not the (much smaller, compressed) file on disk.
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(attachment.Content), attachment.SizeBytes);
    }

    [Fact]
    public async Task A_malformed_docx_is_skipped_with_a_reason_not_treated_as_an_error()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("broken.docx");
        await File.WriteAllTextAsync(path, "not actually a zip");

        var attachments = await ChatContextAttachment.LoadFilesAsync([path]);

        var attachment = Assert.Single(attachments);
        Assert.Equal(ChatContextAttachmentStatus.Skipped, attachment.Status);
        Assert.NotEmpty(attachment.StatusMessage);
    }
}
