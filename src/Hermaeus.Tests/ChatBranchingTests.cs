using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r25 doc 01: regenerate and edit-and-resend create branches instead of
/// deleting what was there. Before r25, RegenerateAsync removed the assistant
/// message AND the user message and re-sent, so the previous answer was gone,
/// including from disk on the next save.
/// </summary>
public sealed class ChatBranchingTests
{
    private static ChatViewModel BuildChatViewModel(ISettingsService settings, CapturingLlm llm) =>
        new(
            llm,
            new InMemoryConversationStore(),
            new EmptyMemoryStore(),
            settings,
            new FakeTts(),
            new ModelProfileService(settings),
            new FakeToasts(),
            new NoOpConversationMemoryService(),
            new RuntimeLogService(settings),
            new ConversationExportService());

    private static async Task<ChatViewModel> ReadyViewModelAsync(TempDir temp, CapturingLlm llm)
    {
        var settings = NewSettings(temp);
        var vm = BuildChatViewModel(settings, llm);
        await vm.LoadModelsAsync(force: true);
        vm.SelectedModel = new LlmModel { Id = "capture", Name = "Capture", ProviderTag = "test" };
        return vm;
    }

    private static async Task SendAsync(ChatViewModel vm, string text)
    {
        vm.InputText = text;
        await vm.SendCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task A_plain_send_builds_a_linear_chain()
    {
        using var temp = new TempDir();
        var vm = await ReadyViewModelAsync(temp, new CapturingLlm());

        await SendAsync(vm, "first");
        await SendAsync(vm, "second");

        Assert.Equal(4, vm.Messages.Count);
        Assert.Equal(4, vm.ActivePath.Count);
        Assert.Equal(string.Empty, vm.Messages[0].ParentId);
        Assert.Equal(vm.Messages[0].Id, vm.Messages[1].ParentId);
        Assert.Equal(vm.Messages[1].Id, vm.Messages[2].ParentId);
        Assert.Equal(vm.Messages[2].Id, vm.Messages[3].ParentId);

        // No message has a sibling, so no switcher chrome appears anywhere.
        Assert.All(vm.Messages, m => Assert.False(m.HasSiblings));
    }

    [Fact]
    public async Task Regenerate_keeps_the_previous_answer_as_a_sibling()
    {
        using var temp = new TempDir();
        var vm = await ReadyViewModelAsync(temp, new CapturingLlm());
        await SendAsync(vm, "what is 2 + 2?");

        var originalAnswerId = vm.Messages.Single(m => m.IsAssistant).Id;
        var userMessageId = vm.Messages.Single(m => m.IsUser).Id;

        await vm.RegenerateCommand.ExecuteAsync(null);

        // Nothing was deleted: one user message, two assistant siblings under it.
        Assert.Single(vm.Messages, m => m.IsUser);
        Assert.Equal(2, vm.Messages.Count(m => m.IsAssistant));
        Assert.Contains(vm.Messages, m => m.Id == originalAnswerId);
        Assert.All(vm.Messages.Where(m => m.IsAssistant), m => Assert.Equal(userMessageId, m.ParentId));

        // The active path shows the new answer, and both answers offer a switcher.
        Assert.Equal(2, vm.ActivePath.Count);
        Assert.NotEqual(originalAnswerId, vm.ActivePath[^1].Id);
        Assert.True(vm.ActivePath[^1].HasSiblings);
        Assert.Equal("2/2", vm.ActivePath[^1].BranchLabel);
    }

    /// <summary>
    /// Before r25 regenerate wrote the old message text back into the input box,
    /// destroying anything half-typed there.
    /// </summary>
    [Fact]
    public async Task Regenerate_does_not_clobber_a_half_typed_message()
    {
        using var temp = new TempDir();
        var vm = await ReadyViewModelAsync(temp, new CapturingLlm());
        await SendAsync(vm, "first question");

        vm.InputText = "a follow-up I was still typing";
        await vm.RegenerateCommand.ExecuteAsync(null);

        Assert.Equal("a follow-up I was still typing", vm.InputText);
    }

    [Fact]
    public async Task Switching_branches_changes_the_rendered_path_but_deletes_nothing()
    {
        using var temp = new TempDir();
        var vm = await ReadyViewModelAsync(temp, new CapturingLlm());
        await SendAsync(vm, "question");
        var firstAnswerId = vm.Messages.Single(m => m.IsAssistant).Id;
        await vm.RegenerateCommand.ExecuteAsync(null);

        var leaf = vm.ActivePath[^1];
        vm.PreviousBranchCommand.Execute(leaf);

        Assert.Equal(firstAnswerId, vm.ActivePath[^1].Id);
        Assert.Equal("1/2", vm.ActivePath[^1].BranchLabel);
        Assert.Equal(3, vm.Messages.Count);

        vm.NextBranchCommand.Execute(vm.ActivePath[^1]);
        Assert.NotEqual(firstAnswerId, vm.ActivePath[^1].Id);
    }

    [Fact]
    public async Task Editing_a_user_message_creates_a_sibling_and_leaves_the_original_subtree_intact()
    {
        using var temp = new TempDir();
        var vm = await ReadyViewModelAsync(temp, new CapturingLlm());
        await SendAsync(vm, "what is the capitol of France?");

        var original = vm.Messages.Single(m => m.IsUser);
        var originalId = original.Id;
        var originalAnswerId = vm.Messages.Single(m => m.IsAssistant).Id;

        vm.BeginEditMessageCommand.Execute(original);
        Assert.True(original.IsEditing);
        Assert.Equal("what is the capitol of France?", original.EditText);

        original.EditText = "what is the capital of France?";
        await vm.SubmitEditMessageCommand.ExecuteAsync(original);

        Assert.False(original.IsEditing);
        Assert.Equal(2, vm.Messages.Count(m => m.IsUser));
        Assert.Contains(vm.Messages, m => m.Id == originalId);
        Assert.Contains(vm.Messages, m => m.Id == originalAnswerId);

        var activeUser = vm.ActivePath.First(m => m.IsUser);
        Assert.Equal("what is the capital of France?", activeUser.OriginalContent);
        Assert.True(activeUser.HasSiblings);
        Assert.Equal("2/2", activeUser.BranchLabel);
    }

    [Fact]
    public async Task Assistant_messages_cannot_be_edited()
    {
        using var temp = new TempDir();
        var vm = await ReadyViewModelAsync(temp, new CapturingLlm());
        await SendAsync(vm, "hello");

        var assistant = vm.Messages.Single(m => m.IsAssistant);
        vm.BeginEditMessageCommand.Execute(assistant);

        Assert.False(assistant.IsEditing);
    }

    [Fact]
    public async Task Cancelling_an_edit_leaves_nothing_behind()
    {
        using var temp = new TempDir();
        var vm = await ReadyViewModelAsync(temp, new CapturingLlm());
        await SendAsync(vm, "hello");

        var user = vm.Messages.Single(m => m.IsUser);
        vm.BeginEditMessageCommand.Execute(user);
        user.EditText = "abandoned";
        vm.CancelEditMessageCommand.Execute(user);

        Assert.False(user.IsEditing);
        Assert.Equal(string.Empty, user.EditText);
        Assert.Equal(2, vm.Messages.Count);
        Assert.Equal("hello", user.OriginalContent);
    }

    [Fact]
    public async Task Deleting_a_branch_removes_exactly_that_subtree()
    {
        using var temp = new TempDir();
        var vm = await ReadyViewModelAsync(temp, new CapturingLlm());
        await SendAsync(vm, "question");
        var firstAnswerId = vm.Messages.Single(m => m.IsAssistant).Id;
        await vm.RegenerateCommand.ExecuteAsync(null);
        var secondAnswer = vm.ActivePath[^1];

        Assert.Equal(1, vm.CountBranchDeletion(secondAnswer));
        await vm.DeleteBranchCommand.ExecuteAsync(secondAnswer);

        Assert.Equal(2, vm.Messages.Count);
        Assert.DoesNotContain(vm.Messages, m => m.Id == secondAnswer.Id);
        Assert.Equal(firstAnswerId, vm.ActivePath[^1].Id);
        Assert.False(vm.ActivePath[^1].HasSiblings);
    }

    /// <summary>Deleting the last branch is deleting the conversation, and there is already a way to do that.</summary>
    [Fact]
    public async Task Deleting_the_only_version_of_a_message_is_refused()
    {
        using var temp = new TempDir();
        var vm = await ReadyViewModelAsync(temp, new CapturingLlm());
        await SendAsync(vm, "question");

        var onlyAnswer = vm.Messages.Single(m => m.IsAssistant);
        await vm.DeleteBranchCommand.ExecuteAsync(onlyAnswer);

        Assert.Equal(2, vm.Messages.Count);
    }

    /// <summary>
    /// The prompt is the conversation the user is actually having, not every
    /// branch they abandoned along the way.
    /// </summary>
    [Fact]
    public async Task The_prompt_history_follows_the_active_path_only()
    {
        using var temp = new TempDir();
        var llm = new CapturingLlm();
        var vm = await ReadyViewModelAsync(temp, llm);

        await SendAsync(vm, "original question");
        var user = vm.Messages.Single(m => m.IsUser);
        vm.BeginEditMessageCommand.Execute(user);
        user.EditText = "revised question";
        await vm.SubmitEditMessageCommand.ExecuteAsync(user);

        await SendAsync(vm, "follow up");

        var sent = llm.LastMessages ?? [];
        Assert.Contains(sent, m => m.Content.Contains("revised question", StringComparison.Ordinal));
        Assert.DoesNotContain(sent, m => m.Content.Contains("original question", StringComparison.Ordinal));
    }

    /// <summary>
    /// Every branch persists: a branch you navigated away from is still your
    /// words, so it stays searchable and stays in Recall.
    /// </summary>
    [Fact]
    public async Task Every_branch_persists_and_the_active_leaf_round_trips()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var store = new InMemoryConversationStore();
        var vm = new ChatViewModel(
            new CapturingLlm(), store, new EmptyMemoryStore(), settings, new FakeTts(),
            new ModelProfileService(settings), new FakeToasts(), new NoOpConversationMemoryService(),
            new RuntimeLogService(settings), new ConversationExportService());
        await vm.LoadModelsAsync(force: true);
        vm.SelectedModel = new LlmModel { Id = "capture", Name = "Capture", ProviderTag = "test" };

        await SendAsync(vm, "question");
        await vm.RegenerateCommand.ExecuteAsync(null);

        var saved = await store.GetByIdAsync(vm.CurrentConversationId);
        Assert.NotNull(saved);
        Assert.Equal(3, saved!.Messages.Count);
        Assert.Equal(vm.ActivePath[^1].Id, saved.ActiveLeafId);

        var path = ConversationTree.ActivePath(saved.Messages, saved.ActiveLeafId);
        Assert.Equal(2, path.Count);
    }

    /// <summary>
    /// The backfill must not depend on which IConversationStore supplied the
    /// conversation. A chainless message list from any other implementation
    /// would otherwise resolve to a one-message active path and render as
    /// though the whole history had vanished.
    /// </summary>
    [Fact]
    public async Task A_conversation_loaded_without_a_parent_chain_still_renders_in_full()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var store = new InMemoryConversationStore();
        var conversation = new Conversation { Id = "flat", Title = "Flat" };
        for (var i = 0; i < 6; i++)
            conversation.Messages.Add(new Message
            {
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"message {i}"
            });
        await store.SaveAsync(conversation);

        var vm = new ChatViewModel(
            new CapturingLlm(), store, new EmptyMemoryStore(), settings, new FakeTts(),
            new ModelProfileService(settings), new FakeToasts(), new NoOpConversationMemoryService(),
            new RuntimeLogService(settings), new ConversationExportService());
        await vm.LoadConversationAsync("flat");

        Assert.Equal(6, vm.Messages.Count);
        Assert.Equal(6, vm.ActivePath.Count);
        Assert.Equal(6, vm.VisibleMessages.Count);
        Assert.Equal("message 5", vm.VisibleMessages[^1].Content);
    }

    // ── Fakes, matching the nested-class pattern the other ChatViewModel test
    //    files already use rather than promoting them to shared helpers here. ──

    private sealed class InMemoryConversationStore : IConversationStore
    {
        private readonly Dictionary<string, Conversation> _items = [];
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<List<Conversation>> GetAllAsync(bool includeArchived = true, CancellationToken ct = default) =>
            Task.FromResult(_items.Values.ToList());
        public Task<Conversation?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(_items.GetValueOrDefault(id));
        public Task SaveAsync(Conversation conversation, CancellationToken ct = default)
        {
            _items[conversation.Id] = conversation;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string id, CancellationToken ct = default)
        {
            _items.Remove(id);
            return Task.CompletedTask;
        }
        public Task<List<Conversation>> SearchAsync(string query, CancellationToken ct = default) =>
            Task.FromResult(_items.Values.ToList());
    }

    private sealed class EmptyMemoryStore : IMemoryStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Memory>> GetAllAsync(bool includeArchived = false, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task<Memory?> GetByIdAsync(string id, CancellationToken ct = default) => Task.FromResult<Memory?>(null);
        public Task<List<Memory>> GetByCategoryAsync(string category, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task<List<Memory>> GetByScopeAsync(MemoryScope scope, string? scopeId = null, bool includeArchived = false, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task SaveAsync(Memory memory, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Memory>> SearchAsync(string query, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task<List<Memory>> GetByImportanceAsync(double minScore, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task<List<Memory>> GetRecentAsync(int limit = 10, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task<List<Memory>> GetRecentByConversationAsync(string conversationId, int limit = 10, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task<int> GetCountByConversationAsync(string conversationId, bool includeArchived = false, CancellationToken ct = default) => Task.FromResult(0);
        public Task<Dictionary<string, int>> GetCountsByConversationAsync(IEnumerable<string> conversationIds, bool includeArchived = false, CancellationToken ct = default) =>
            Task.FromResult(conversationIds.ToDictionary(id => id, _ => 0));
        public Task MarkRecalledAsync(IEnumerable<string> ids, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> ArchiveStaleMemoriesAsync(double importanceFloor = 0.05, int unrecalledForDays = 180, CancellationToken ct = default) => Task.FromResult(0);
        public Task RunEmbeddingBackfillAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> GetEmbeddingMismatchCountAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> ClearMismatchedEmbeddingsAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoOpConversationMemoryService : IConversationMemoryService
    {
        public Task RunAutoSummaryAsync(string conversationId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> ApplyInjectedMemoryMarkersAsync(string responseText, IReadOnlyList<string> injectedMemoryIds, CancellationToken ct = default) =>
            Task.FromResult(responseText);
        public Task<string> ApplyMemoryMarkersAsync(string responseText, IReadOnlyList<string> injectedMemoryIds, string? conversationId, int maxNewMemories = 3, CancellationToken ct = default) =>
            Task.FromResult(responseText);
    }
}
