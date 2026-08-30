using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>r19 5.3: ChatViewModel.AddContextFilesAsync gates image attachments on whether the
/// active chat server was configured with a vision projector (--mmproj), not merely on the
/// model's own capabilities - a text-only launch of a vision-capable model still gets Skipped.
/// A model routed through the OpenAI provider bypasses the mmproj check entirely, since
/// OpenAI's API accepts the same image_url content part with no local projector involved.</summary>
public sealed class ChatVisionGateTests
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

    private static ChatViewModel NewChatViewModel(SettingsService settings)
    {
        // FakeMemoryStore: this suite never asserts on memory state, and a real
        // MemoryStore's fire-and-forget status refresh would otherwise race a
        // SQLite connection against this test's TempDir.Dispose().
        return new ChatViewModel(
            new FakeLlm(), new ThrowingSaveConversationStore(), new FakeMemoryStore(), settings,
            new FakeTts(), new ModelProfileService(settings), new FakeToasts(),
            new FakeConversationMemoryService(), new RuntimeLogService(settings), new ConversationExportService());
    }

    [Fact]
    public async Task Image_is_skipped_when_the_chat_server_has_no_mmproj_configured()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.ManagedServers.Clear();
        settings.Settings.ManagedServers.Add(new ServerConfig { Name = "Chat", EmbeddingsMode = false });
        var vm = NewChatViewModel(settings);
        var path = temp.PathFor("photo.png");
        await File.WriteAllBytesAsync(path, OnePixelPng);

        await vm.AddContextFilesAsync([path]);

        var attachment = Assert.Single(vm.ContextAttachments);
        Assert.Equal(ChatContextAttachmentStatus.Skipped, attachment.Status);
        Assert.Contains("vision projector", attachment.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Image_attaches_when_the_chat_server_has_mmproj_configured()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.ManagedServers.Clear();
        settings.Settings.ManagedServers.Add(new ServerConfig { Name = "Chat", EmbeddingsMode = false, MmprojPath = "mmproj-model.gguf" });
        var vm = NewChatViewModel(settings);
        var path = temp.PathFor("photo.png");
        await File.WriteAllBytesAsync(path, OnePixelPng);

        await vm.AddContextFilesAsync([path]);

        var attachment = Assert.Single(vm.ContextAttachments);
        Assert.True(attachment.IsReady, attachment.StatusMessage);
        Assert.True(attachment.IsImage);
    }

    [Fact]
    public async Task Image_is_skipped_when_the_configured_projector_is_disabled()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.ManagedServers.Clear();
        settings.Settings.ManagedServers.Add(new ServerConfig
        {
            Name = "Chat",
            EmbeddingsMode = false,
            MmprojPath = "verified-projector.gguf",
            UseProjector = false
        });
        var vm = NewChatViewModel(settings);
        var path = temp.PathFor("photo.png");
        await File.WriteAllBytesAsync(path, OnePixelPng);

        await vm.AddContextFilesAsync([path]);

        var attachment = Assert.Single(vm.ContextAttachments);
        Assert.Equal(ChatContextAttachmentStatus.Skipped, attachment.Status);
        Assert.Contains("vision projector", attachment.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Image_attaches_for_an_OpenAI_model_with_no_local_mmproj_configured()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.ManagedServers.Clear();
        settings.Settings.ManagedServers.Add(new ServerConfig { Name = "Chat", EmbeddingsMode = false });
        var vm = NewChatViewModel(settings);
        vm.SelectedModel = new LlmModel { Id = "gpt-4o", Name = "gpt-4o", Provider = "OpenAI", ProviderTag = "openai" };
        var path = temp.PathFor("photo.png");
        await File.WriteAllBytesAsync(path, OnePixelPng);

        await vm.AddContextFilesAsync([path]);

        var attachment = Assert.Single(vm.ContextAttachments);
        Assert.True(attachment.IsReady, attachment.StatusMessage);
        Assert.True(attachment.IsImage);
    }
}
