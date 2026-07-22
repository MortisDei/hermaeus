using System.IO.Compression;
using System.Text;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>r19 5.1: .docx text extraction, built entirely from in-memory zip archives (no fixture binaries in the repo).</summary>
public sealed class DocxTextExtractorTests
{
    private const string NsPrefix = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>""";
    private const string NsSuffix = "</w:body></w:document>";

    private static MemoryStream BuildDocx(string documentXmlBody)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(NsPrefix + documentXmlBody + NsSuffix);
        }
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void Extract_reads_paragraph_text_with_paragraph_breaks()
    {
        using var docx = BuildDocx("""
            <w:p><w:r><w:t>First paragraph.</w:t></w:r></w:p>
            <w:p><w:r><w:t>Second paragraph.</w:t></w:r></w:p>
            """);

        var result = DocxTextExtractor.Extract(docx);

        Assert.Equal(FileTextExtractionStatus.Success, result.Status);
        Assert.Contains("First paragraph.", result.Text);
        Assert.Contains("Second paragraph.", result.Text);
        // Distinct paragraphs must not be glued into one run of text.
        Assert.True(result.Text.IndexOf("First paragraph.", StringComparison.Ordinal)
            < result.Text.IndexOf("Second paragraph.", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_joins_split_runs_within_one_paragraph()
    {
        using var docx = BuildDocx("<w:p><w:r><w:t>Hello, </w:t></w:r><w:r><w:t>world.</w:t></w:r></w:p>");

        var result = DocxTextExtractor.Extract(docx);

        Assert.Contains("Hello, world.", result.Text);
    }

    [Fact]
    public void Extract_handles_tabs_and_line_breaks_within_a_paragraph()
    {
        using var docx = BuildDocx("<w:p><w:r><w:t>a</w:t><w:tab/><w:t>b</w:t><w:br/><w:t>c</w:t></w:r></w:p>");

        var result = DocxTextExtractor.Extract(docx);

        Assert.Contains("a\tb\nc", result.Text);
    }

    [Fact]
    public void Extract_renders_a_table_as_tab_separated_rows()
    {
        using var docx = BuildDocx("""
            <w:tbl>
              <w:tr>
                <w:tc><w:p><w:r><w:t>Name</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>Age</w:t></w:r></w:p></w:tc>
              </w:tr>
              <w:tr>
                <w:tc><w:p><w:r><w:t>Alice</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>30</w:t></w:r></w:p></w:tc>
              </w:tr>
            </w:tbl>
            """);

        var result = DocxTextExtractor.Extract(docx);

        Assert.Contains("Name\tAge", result.Text);
        Assert.Contains("Alice\t30", result.Text);
    }

    [Fact]
    public void Extract_skips_a_malformed_zip_with_a_reason()
    {
        using var garbage = new MemoryStream(Encoding.UTF8.GetBytes("this is not a zip file"));

        var result = DocxTextExtractor.Extract(garbage);

        Assert.Equal(FileTextExtractionStatus.Skipped, result.Status);
        Assert.NotEmpty(result.Reason);
    }

    [Fact]
    public void Extract_skips_a_zip_with_no_document_xml_entry()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("readme.txt");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("not a docx");
        }
        stream.Position = 0;

        var result = DocxTextExtractor.Extract(stream);

        Assert.Equal(FileTextExtractionStatus.Skipped, result.Status);
        Assert.NotEmpty(result.Reason);
    }

    [Fact]
    public void Extract_skips_a_document_xml_entry_over_the_size_cap()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml", CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            // Highly compressible padding still reports a large uncompressed Length,
            // which is exactly what the entry.Length guard checks.
            writer.Write(NsPrefix);
            writer.Write(new string('a', 9 * 1024 * 1024));
            writer.Write(NsSuffix);
        }
        stream.Position = 0;

        var result = DocxTextExtractor.Extract(stream);

        Assert.Equal(FileTextExtractionStatus.Skipped, result.Status);
        Assert.Contains("MB", result.Reason, StringComparison.Ordinal);
    }
}
