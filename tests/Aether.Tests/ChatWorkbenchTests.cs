using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using Aether.ViewModels;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

public sealed class ChatWorkbenchTests
{
    [Fact]
    public void TruncateHistoryKeepsNewestMessages()
    {
        var messages = Enumerable.Range(1, 20)
            .Select(i => new MessageViewModel { Role = i % 2 == 0 ? "assistant" : "user", Content = new string('x', 80) + i })
            .ToList();

        var truncated = ChatViewModel.TruncateHistoryToContextWindow(messages, contextWindow: 160, systemTokens: 10, currentPromptTokens: 20);
        Assert.True(truncated.Count < messages.Count);
        Assert.Equal(messages[^1].Content, truncated[^1].Content);
    }

    [Fact]
    public async Task ContextInspectorTraceAndCompareModelsWork()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var llm = new UsageLlm();
        var vm = new ChatViewModel(
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

        await vm.LoadModelsAsync(force: true);
        vm.SystemPrompt = "Answer briefly.";
        vm.InputText = "Compare this workspace task.";
        vm.ToggleContextInspectorCommand.Execute(null);

        Assert.True(vm.ShowContextInspector);
        Assert.Contains(vm.ContextPreviewParts, p => p.Kind == "System");
        Assert.Contains(vm.ContextPreviewParts, p => p.Kind == "User");

        await vm.CompareSelectedModelsCommand.ExecuteAsync(null);
        Assert.Single(vm.CompareResults);
        Assert.Contains("ok", vm.CompareResults[0].Answer);

        await vm.SendCommand.ExecuteAsync(null);
        Assert.Single(vm.ChatTraces);
        Assert.Equal("usage", vm.ChatTraces[0].ModelId);
        Assert.NotNull(vm.ChatTraces[0].ProviderUsage);
    }

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
        public Task SaveAsync(Memory memory, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Memory>> SearchAsync(string query, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task<List<Memory>> GetByImportanceAsync(double minScore, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task<List<Memory>> GetRecentAsync(int limit = 10, CancellationToken ct = default) => Task.FromResult(new List<Memory>());
        public Task<int> GetCountByConversationAsync(string conversationId, bool includeArchived = false, CancellationToken ct = default) => Task.FromResult(0);
        public Task<Dictionary<string, int>> GetCountsByConversationAsync(IEnumerable<string> conversationIds, bool includeArchived = false, CancellationToken ct = default) =>
            Task.FromResult(conversationIds.ToDictionary(id => id, _ => 0));
    }

    private sealed class NoOpConversationMemoryService : IConversationMemoryService
    {
        public Task RunAutoSummaryAsync(string conversationId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
