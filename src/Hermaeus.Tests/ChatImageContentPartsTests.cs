using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>r19 5.3: image attachments for vision models - the OpenAI-style content-part payload and the --mmproj launch argument.</summary>
public sealed class ChatImageContentPartsTests
{
    [Fact]
    public void BuildMessages_sends_a_plain_string_for_a_message_without_images()
    {
        var messages = new List<ChatMessage> { new("user", "hello there") };

        var built = OpenAiCompatibleToolWire.BuildMessages(messages, systemPrompt: null);

        var json = JsonSerializer.Serialize(built[0]);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("content").ValueKind);
        Assert.Equal("hello there", doc.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public void BuildMessages_emits_a_content_part_array_for_one_text_and_one_image()
    {
        var messages = new List<ChatMessage>
        {
            new("user", "what is this?", Images: [new ChatMessageImage("photo.png", "data:image/png;base64,QUJD")])
        };

        var built = OpenAiCompatibleToolWire.BuildMessages(messages, systemPrompt: null);

        var json = JsonSerializer.Serialize(built[0]);
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.Equal(2, content.GetArrayLength());

        var textPart = content[0];
        Assert.Equal("text", textPart.GetProperty("type").GetString());
        Assert.Equal("what is this?", textPart.GetProperty("text").GetString());

        var imagePart = content[1];
        Assert.Equal("image_url", imagePart.GetProperty("type").GetString());
        Assert.Equal("data:image/png;base64,QUJD", imagePart.GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public void BuildMessages_omits_the_text_part_when_the_message_is_image_only()
    {
        var messages = new List<ChatMessage>
        {
            new("user", string.Empty, Images: [new ChatMessageImage("photo.png", "data:image/png;base64,QUJD")])
        };

        var built = OpenAiCompatibleToolWire.BuildMessages(messages, systemPrompt: null);
        var json = JsonSerializer.Serialize(built[0]);
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("content");

        Assert.Equal(1, content.GetArrayLength());
        Assert.Equal("image_url", content[0].GetProperty("type").GetString());
    }

    [Fact]
    public void BuildMessages_emits_one_image_part_per_attached_image()
    {
        var messages = new List<ChatMessage>
        {
            new("user", "compare these", Images:
            [
                new ChatMessageImage("a.png", "data:image/png;base64,AAAA"),
                new ChatMessageImage("b.jpg", "data:image/jpeg;base64,BBBB")
            ])
        };

        var built = OpenAiCompatibleToolWire.BuildMessages(messages, systemPrompt: null);
        var json = JsonSerializer.Serialize(built[0]);
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("content");

        Assert.Equal(3, content.GetArrayLength()); // text + 2 images
    }

    [Fact]
    public void BuildLaunchArguments_appends_mmproj_when_configured()
    {
        var cfg = new ServerConfig { ModelPath = "model.gguf", MmprojPath = "mmproj-model.gguf" };

        var args = ServerProcessManager.BuildLaunchArguments(cfg);

        var idx = args.ToList().IndexOf("--mmproj");
        Assert.True(idx >= 0, "expected --mmproj in the launch arguments");
        Assert.Equal("mmproj-model.gguf", args[idx + 1]);
    }

    [Fact]
    public void BuildLaunchArguments_omits_mmproj_when_not_configured()
    {
        var cfg = new ServerConfig { ModelPath = "model.gguf" };

        var args = ServerProcessManager.BuildLaunchArguments(cfg);

        Assert.DoesNotContain("--mmproj", args);
    }

    [Fact]
    public void BuildLaunchArguments_omits_configured_projector_when_use_is_disabled()
    {
        var cfg = new ServerConfig
        {
            ModelPath = "model.gguf",
            MmprojPath = "verified-projector.gguf",
            UseProjector = false,
            ExtraArgs = "--mmproj manual-projector.gguf --alias chat"
        };

        var args = ServerProcessManager.BuildLaunchArguments(cfg).ToList();

        Assert.DoesNotContain("--mmproj", args);
        Assert.DoesNotContain("verified-projector.gguf", args);
        Assert.DoesNotContain("manual-projector.gguf", args);
        Assert.Contains("--alias", args);
        Assert.Equal("verified-projector.gguf", cfg.MmprojPath);
    }

    [Fact]
    public void BuildLaunchArguments_uses_the_configured_projector_when_re_enabled()
    {
        var cfg = new ServerConfig
        {
            ModelPath = "model.gguf",
            MmprojPath = "verified-projector.gguf",
            UseProjector = false
        };

        Assert.DoesNotContain("--mmproj", ServerProcessManager.BuildLaunchArguments(cfg));

        cfg.UseProjector = true;
        var args = ServerProcessManager.BuildLaunchArguments(cfg).ToList();
        var index = args.IndexOf("--mmproj");

        Assert.True(index >= 0);
        Assert.Equal("verified-projector.gguf", args[index + 1]);
    }

    [Fact]
    public void BuildLaunchArguments_respects_an_explicit_mmproj_in_ExtraArgs()
    {
        var cfg = new ServerConfig { ModelPath = "model.gguf", MmprojPath = "auto-suggested.gguf", ExtraArgs = "--mmproj manual-override.gguf" };

        var args = ServerProcessManager.BuildLaunchArguments(cfg).ToList();

        Assert.Equal(1, args.Count(a => a == "--mmproj"));
        Assert.Contains("manual-override.gguf", args);
        Assert.DoesNotContain("auto-suggested.gguf", args);
    }
}
