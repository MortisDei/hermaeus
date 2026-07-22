using Aether.ViewModels;
using Xunit;

namespace Aether.Tests;

/// <summary>r19 5.3: image attachments never enter the text prompt budget, are refused honestly
/// when the active server has no vision projector, and are capped in size/count per send.</summary>
public sealed class ChatContextAttachmentImageTests
{
    private static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D,
        0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82
    ];

    [Fact]
    public async Task A_png_attaches_as_an_image_with_a_data_uri_when_vision_is_available()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("photo.png");
        await File.WriteAllBytesAsync(path, OnePixelPng);

        var attachments = await ChatContextAttachment.LoadFilesAsync([path], visionAvailable: true);

        var attachment = Assert.Single(attachments);
        Assert.True(attachment.IsReady, attachment.StatusMessage);
        Assert.Equal(ChatContextAttachmentKind.Image, attachment.Kind);
        Assert.True(attachment.IsImage);
        Assert.StartsWith("data:image/png;base64,", attachment.ImageDataUri);
        Assert.Empty(attachment.Content);
    }

    [Fact]
    public async Task An_image_is_skipped_with_an_honest_reason_when_no_vision_projector_is_configured()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("photo.png");
        await File.WriteAllBytesAsync(path, OnePixelPng);

        var attachments = await ChatContextAttachment.LoadFilesAsync([path], visionAvailable: false);

        var attachment = Assert.Single(attachments);
        Assert.Equal(ChatContextAttachmentStatus.Skipped, attachment.Status);
        Assert.Contains("vision projector", attachment.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Only_four_images_are_accepted_per_send()
    {
        using var temp = new TempDir();
        var paths = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var path = temp.PathFor($"photo{i}.png");
            await File.WriteAllBytesAsync(path, OnePixelPng);
            paths.Add(path);
        }

        var attachments = await ChatContextAttachment.LoadFilesAsync(paths, visionAvailable: true);

        Assert.Equal(4, attachments.Count(a => a.IsReady));
        var skipped = Assert.Single(attachments, a => a.Status == ChatContextAttachmentStatus.Skipped);
        Assert.Contains("4 images", skipped.StatusMessage);
    }

    [Fact]
    public async Task An_oversized_image_is_skipped()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("huge.png");
        var oversized = new byte[8 * 1024 * 1024 + 1];
        await File.WriteAllBytesAsync(path, oversized);

        var attachments = await ChatContextAttachment.LoadFilesAsync([path], visionAvailable: true);

        var attachment = Assert.Single(attachments);
        Assert.Equal(ChatContextAttachmentStatus.Skipped, attachment.Status);
        Assert.Contains("over", attachment.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildPrompt_excludes_images_from_the_text_context_block()
    {
        using var temp = new TempDir();
        var imagePath = temp.PathFor("photo.png");
        await File.WriteAllBytesAsync(imagePath, OnePixelPng);
        var textPath = temp.PathFor("notes.txt");
        await File.WriteAllTextAsync(textPath, "hello");

        var attachments = await ChatContextAttachment.LoadFilesAsync([imagePath, textPath], visionAvailable: true);
        var prompt = ChatContextAttachment.BuildPrompt("what do you see?", attachments);

        Assert.DoesNotContain("photo.png", prompt);
        Assert.Contains("notes.txt", prompt);
    }

    [Fact]
    public async Task BuildDisplayMessage_still_lists_the_image_so_the_transcript_stays_honest()
    {
        using var temp = new TempDir();
        var imagePath = temp.PathFor("photo.png");
        await File.WriteAllBytesAsync(imagePath, OnePixelPng);

        var attachments = await ChatContextAttachment.LoadFilesAsync([imagePath], visionAvailable: true);
        var display = ChatContextAttachment.BuildDisplayMessage("what do you see?", attachments);

        Assert.Contains("photo.png", display);
    }
}
