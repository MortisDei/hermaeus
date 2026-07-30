using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>r19 5.4: saving a chat code block lands in the right conversation's artifacts folder and shows up in the strip; switching conversations reloads the right list.</summary>
public sealed class ChatArtifactsViewModelTests
{
    private static ChatViewModel NewChatViewModel(SettingsService settings, ChatArtifactService artifacts, ThrowingSaveConversationStore store)
    {
        // FakeMemoryStore, not a real MemoryStore: this suite never asserts on
        // memory state, and ChatViewModel's fire-and-forget memory-status
        // refresh (constructor, NewConversation) would otherwise race a real
        // SQLite connection against this test's TempDir.Dispose().
        return new ChatViewModel(
            new FakeLlm(), store, new FakeMemoryStore(), settings,
            new FakeTts(), new ModelProfileService(settings), new FakeToasts(),
            new FakeConversationMemoryService(), new RuntimeLogService(settings), new ConversationExportService(),
            artifacts: artifacts);
    }

    [Fact]
    public async Task Saving_a_code_block_adds_it_to_the_strip_for_the_current_conversation()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var artifacts = new ChatArtifactService(settings);
        var vm = NewChatViewModel(settings, artifacts, new ThrowingSaveConversationStore());
        vm.CurrentConversationId = "conv-under-test";

        vm.SaveCodeBlockAction("csharp", "class Foo {}", "# My Heading\n\nSome text");
        await WaitForAsync(() => vm.Artifacts.Count > 0, "an artifact appearing in the chat artifact list");

        var saved = Assert.Single(vm.Artifacts);
        Assert.Equal("My-Heading.cs", saved.FileName);
        Assert.True(File.Exists(saved.FullPath));
        Assert.True(vm.HasArtifacts);
    }

    [Fact]
    public async Task Saving_a_code_block_does_not_double_up_the_extension_when_the_heading_already_names_the_file()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var artifacts = new ChatArtifactService(settings);
        var vm = NewChatViewModel(settings, artifacts, new ThrowingSaveConversationStore());
        vm.CurrentConversationId = "conv-under-test";

        vm.SaveCodeBlockAction("csharp", "class Calculator {}", "# calculator.cs\n\nHere it is.");
        await WaitForAsync(() => vm.Artifacts.Count > 0, "an artifact appearing in the chat artifact list");

        Assert.Equal("calculator.cs", Assert.Single(vm.Artifacts).FileName);
    }

    [Fact]
    public async Task Loading_a_different_conversation_shows_only_that_conversations_artifacts()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var artifacts = new ChatArtifactService(settings);
        await artifacts.SaveAsync("conv-a", "a.txt", "a content");
        await artifacts.SaveAsync("conv-b", "b.txt", "b content");

        var store = new ThrowingSaveConversationStore();
        await store.SaveAsync(new Conversation { Id = "conv-a", Title = "Conversation A" });
        await store.SaveAsync(new Conversation { Id = "conv-b", Title = "Conversation B" });

        var vm = NewChatViewModel(settings, artifacts, store);

        await vm.LoadConversationAsync("conv-a");
        Assert.Equal("a.txt", Assert.Single(vm.Artifacts).FileName);

        await vm.LoadConversationAsync("conv-b");
        Assert.Equal("b.txt", Assert.Single(vm.Artifacts).FileName);
    }

    // ── r24: saving a code block before the conversation's first persist ──────

    [Fact]
    public async Task Saving_a_code_block_before_the_first_persist_assigns_a_real_conversation_id_that_later_lookups_agree_with()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var artifacts = new ChatArtifactService(settings);
        var store = new ThrowingSaveConversationStore();
        var vm = NewChatViewModel(settings, artifacts, store);

        Assert.Equal(string.Empty, vm.CurrentConversationId);
        vm.SaveCodeBlockAction("csharp", "class Foo {}", "# My Heading\n\nSome text");
        await WaitForAsync(() => vm.Artifacts.Count > 0, "an artifact appearing in the chat artifact list");

        // The artifact must not be orphaned in a separate "unsaved" bucket the
        // conversation's real id can never resolve back to.
        Assert.NotEqual(string.Empty, vm.CurrentConversationId);
        var assignedId = vm.CurrentConversationId;

        await store.SaveAsync(new Conversation { Id = assignedId, Title = "My Heading" });
        vm.NewConversation();
        Assert.Empty(vm.Artifacts);

        await vm.LoadConversationAsync(assignedId);
        Assert.Single(vm.Artifacts);
    }

    // ── r24: a heading that is only an inline-code-wrapped filename ───────────

    [Fact]
    public void DeriveArtifactStem_strips_backticks_around_an_inline_code_filename_heading()
    {
        Assert.Equal("calculator", ChatViewModel.DeriveArtifactStem("# `calculator.cs`\n\nbody", ""));
    }

    [Fact]
    public async Task NewConversation_clears_the_artifacts_strip()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var artifacts = new ChatArtifactService(settings);
        var vm = NewChatViewModel(settings, artifacts, new ThrowingSaveConversationStore());
        vm.CurrentConversationId = "conv-under-test";

        vm.SaveCodeBlockAction(null, "print('hi')", "no heading here");
        await WaitForAsync(() => vm.Artifacts.Count > 0, "an artifact appearing in the chat artifact list");

        vm.NewConversation();

        Assert.Empty(vm.Artifacts);
        Assert.False(vm.HasArtifacts);
    }

    // ── DeriveArtifactStem ───────────────────────────────────────────────────

    [Theory]
    [InlineData("# Fix the login bug\n\nSome text", "", "Fix-the-login-bug")]
    [InlineData("No heading at all here", "Fallback Title", "Fallback-Title")]
    [InlineData("", "", "artifact")]
    [InlineData("## Multi   Space   Heading", "", "Multi-Space-Heading")]
    [InlineData("# calculator.cs\n\nbody", "", "calculator")]
    public void DeriveArtifactStem_prefers_the_first_heading_then_the_conversation_title_then_a_fallback(
        string markdown, string conversationTitle, string expected)
    {
        Assert.Equal(expected, ChatViewModel.DeriveArtifactStem(markdown, conversationTitle));
    }

    [Fact]
    public void DeriveArtifactStem_strips_filesystem_invalid_characters_from_the_heading()
    {
        var stem = ChatViewModel.DeriveArtifactStem("# Fix: the \"bug\"?\n", "");
        Assert.DoesNotContain(Path.GetInvalidFileNameChars(), c => stem.Contains(c));
    }

    // ── ExtensionForLanguage ─────────────────────────────────────────────────

    [Theory]
    [InlineData("csharp", ".cs")]
    [InlineData("cs", ".cs")]
    [InlineData("python", ".py")]
    [InlineData("md", ".md")]
    [InlineData(null, ".txt")]
    [InlineData("some-unknown-language", ".txt")]
    public void ExtensionForLanguage_maps_known_fence_languages_and_falls_back_to_txt(string? language, string expected)
    {
        Assert.Equal(expected, ChatArtifactService.ExtensionForLanguage(language));
    }
}
