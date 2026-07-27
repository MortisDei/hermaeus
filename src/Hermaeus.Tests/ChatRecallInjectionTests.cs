using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Services.Recall;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>r24 doc 02 2.6: opt-in chat injection from Recall, its citation pills, and
/// the weak-retrieval/off-by-default guards that keep it from parroting unrelated
/// history into every message.</summary>
public sealed class ChatRecallInjectionTests
{
    private sealed class FakeRecallSourceOnly : IRecallSource
    {
        private readonly IReadOnlyList<RecallHit> _hits;
        public FakeRecallSourceOnly(IReadOnlyList<RecallHit> hits) => _hits = hits;
        public string Name => "Fake";
        public Task<IReadOnlyList<RecallHit>> SearchAsync(string query, string projectScope, CancellationToken ct) =>
            Task.FromResult(_hits);
    }

    private static RecallService NewRecallService(IReadOnlyList<RecallHit> hits) =>
        new([new FakeRecallSourceOnly(hits)], new FakeEmbeddingService());

    private static ChatViewModel BuildChatViewModel(ISettingsService settings, CapturingLlm llm, RecallService recall) =>
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
            new ConversationExportService(),
            recallSearch: recall);

    [Fact]
    public async Task Strong_retrieval_injects_a_recall_block_with_clickable_citation_pills()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Memory.RecallInjectionEnabled = true;
        var hit = new RecallHit(RecallKind.Message, "Old chat about KV cache", "we settled on flash attention off",
            DateTime.UtcNow, "", 0.02, new RecallTarget(ConversationId: "c-old", MessageIndex: 3));
        var recall = NewRecallService([hit]);
        var llm = new CapturingLlm();
        var vm = BuildChatViewModel(settings, llm, recall);
        await vm.LoadModelsAsync(force: true);
        vm.SelectedModel = new LlmModel { Id = "capture", Name = "Capture", ProviderTag = "test" };

        vm.InputText = "What did we decide about the KV cache settings?";
        await vm.SendCommand.ExecuteAsync(null);

        var systemPrompt = llm.LastOptions?.SystemPrompt ?? string.Empty;
        True(systemPrompt.Contains("## Recall Context", StringComparison.Ordinal),
            "the system prompt should carry the Recall Context block when injection is on and there are hits");
        True(systemPrompt.Contains("Old chat about KV cache", StringComparison.Ordinal),
            "the injected block should include the hit's title");

        var assistant = vm.Messages.Last(m => m.Role == "assistant");
        True(assistant.CitationSources.Count > 0, "a Recall hit must render as a visible, clickable citation pill");
        True(assistant.CitationSources.Any(s => s.Kind == ProvenanceKind.Recall),
            "the pill must be tagged with ProvenanceKind.Recall, not Memory, so it can never be targeted by a [MEMORY_UPDATE]/[MEMORY_FORGET] marker");

        var trace = Assert.Single(vm.ChatTraces);
        True(trace.RecallContextItems > 0, "trace RecallContextItems should be non-zero when a block was injected");
    }

    [Fact]
    public async Task Weak_retrieval_with_no_hits_injects_nothing()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Memory.RecallInjectionEnabled = true;
        var recall = NewRecallService([]);
        var llm = new CapturingLlm();
        var vm = BuildChatViewModel(settings, llm, recall);
        await vm.LoadModelsAsync(force: true);
        vm.SelectedModel = new LlmModel { Id = "capture", Name = "Capture", ProviderTag = "test" };

        vm.InputText = "thanks!";
        await vm.SendCommand.ExecuteAsync(null);

        False(llm.LastOptions?.SystemPrompt?.Contains("Recall Context", StringComparison.Ordinal) == true,
            "no hits means nothing should be injected");
        var trace = Assert.Single(vm.ChatTraces);
        Equal(0, trace.RecallContextItems, "no recall items should have been injected");
    }

    [Fact]
    public async Task Off_by_default_never_injects_even_when_hits_are_available()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        Assert.False(settings.Settings.Memory.RecallInjectionEnabled, "Recall-in-chat must default to off");
        var hit = new RecallHit(RecallKind.Message, "Would-be hit", "snippet", DateTime.UtcNow, "", 0.02,
            new RecallTarget(ConversationId: "c1", MessageIndex: 0));
        var recall = NewRecallService([hit]);
        var llm = new CapturingLlm();
        var vm = BuildChatViewModel(settings, llm, recall);
        await vm.LoadModelsAsync(force: true);
        vm.SelectedModel = new LlmModel { Id = "capture", Name = "Capture", ProviderTag = "test" };

        vm.InputText = "Anything relevant?";
        await vm.SendCommand.ExecuteAsync(null);

        False(llm.LastOptions?.SystemPrompt?.Contains("Recall Context", StringComparison.Ordinal) == true,
            "with the setting off, Recall must never inject regardless of what a source would return");
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
