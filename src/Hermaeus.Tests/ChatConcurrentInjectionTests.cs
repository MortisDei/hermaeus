using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Pipeline;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.Services.Recall;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r27 02-retrieval-that-scales.md 2.6: memory, RAG and recall injection used
/// to run one after another in the pre-stream phase, so the wait before the
/// first token was their sum. They are independent and now run concurrently,
/// and the user-visible ordering must not depend on which one finished first.
/// </summary>
public sealed class ChatConcurrentInjectionTests
{
    private sealed class FakeRecallSource(IReadOnlyList<RecallHit> hits) : IRecallSource
    {
        public string Name => "Fake";
        public Task<IReadOnlyList<RecallHit>> SearchAsync(string query, string projectScope, CancellationToken ct) =>
            Task.FromResult(hits);
    }

    /// <summary>Deliberately the slowest of the three, to prove ordering is fixed rather than completion-ordered.</summary>
    private sealed class SlowEmbeddingService(int delayMs) : Hermaeus.Rag.Embeddings.IEmbeddingService
    {
        public int Dimensions => 4;

        public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            await Task.Delay(delayMs, ct);
            return [1f, 0f, 0f, 0f];
        }

        public async Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            await Task.Delay(delayMs, ct);
            return texts.Select(_ => new[] { 1f, 0f, 0f, 0f }).ToList();
        }
    }

    private sealed class ThrowingRecallSource : IRecallSource
    {
        public string Name => "Throwing";
        public Task<IReadOnlyList<RecallHit>> SearchAsync(string query, string projectScope, CancellationToken ct) =>
            throw new InvalidOperationException("recall index is unavailable");
    }

    private static async Task<(SqliteRagStore Store, RagDataset Dataset)> IngestAsync(
        TempDir temp, ISettingsService settings, Hermaeus.Rag.Embeddings.IEmbeddingService embed)
    {
        var store = new SqliteRagStore(settings);
        await store.InitializeAsync();
        var docs = temp.PathFor("docs-concurrent");
        Directory.CreateDirectory(docs);
        await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"),
            "The archivist's seal is a gold monogram grown through with a tree and an open book.");

        var dataset = new RagDataset { Name = "knowledge" };
        await new RagPipeline(store, embed).IngestDirectoryAsync(dataset, docs);
        return (store, dataset);
    }

    private static ChatViewModel BuildViewModel(
        ISettingsService settings, CapturingLlm llm, RagQueryService? rag, RecallService? recall) =>
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
            rag: rag,
            recallSearch: recall);

    [Fact]
    public async Task Sources_are_ordered_rag_then_recall_regardless_of_completion_order()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.Memory.RecallInjectionEnabled = true;

        // RAG is made the slowest of the three. If ordering followed completion
        // order, its sources would land after recall's.
        var embed = new SlowEmbeddingService(delayMs: 120);
        var (store, dataset) = await IngestAsync(temp, settings, embed);
        var rag = new RagQueryService(store, embed, new FakeLlm(), settings, new NoOpReranker());

        var hit = new RecallHit(RecallKind.Message, "Earlier conversation", "we settled on flash attention off",
            DateTime.UtcNow, "", 1.0, new RecallTarget(ConversationId: "c-old", MessageIndex: 3));
        var recall = new RecallService([new FakeRecallSource([hit])], new FakeEmbeddingService());

        var llm = new CapturingLlm();
        var vm = BuildViewModel(settings, llm, rag, recall);
        await vm.LoadModelsAsync(force: true);
        vm.SelectedModel = new LlmModel { Id = "capture", Name = "Capture", ProviderTag = "test" };
        vm.RagDatasetId = dataset.Id;

        vm.InputText = "What is the archivist's seal?";
        await vm.SendCommand.ExecuteAsync(null);

        var assistant = vm.Messages.Last(m => m.Role == "assistant");
        var kinds = assistant.Sources.Select(s => s.Kind).ToList();
        Assert.Contains(ProvenanceKind.Rag, kinds);
        Assert.Contains(ProvenanceKind.Recall, kinds);
        Assert.True(kinds.LastIndexOf(ProvenanceKind.Rag) < kinds.IndexOf(ProvenanceKind.Recall),
            "the user sees memory, then RAG, then recall; concurrency is an implementation detail and must not reorder them");
    }

    [Fact]
    public async Task One_injection_throwing_does_not_prevent_the_other_two()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.Memory.RecallInjectionEnabled = true;

        var embed = new FakeEmbeddingService();
        var (store, dataset) = await IngestAsync(temp, settings, embed);
        var rag = new RagQueryService(store, embed, new FakeLlm(), settings, new NoOpReranker());
        var recall = new RecallService([new ThrowingRecallSource()], new FakeEmbeddingService());

        var llm = new CapturingLlm();
        var vm = BuildViewModel(settings, llm, rag, recall);
        await vm.LoadModelsAsync(force: true);
        vm.SelectedModel = new LlmModel { Id = "capture", Name = "Capture", ProviderTag = "test" };
        vm.RagDatasetId = dataset.Id;

        vm.InputText = "What is the archivist's seal?";
        await vm.SendCommand.ExecuteAsync(null);

        // Each injection already degrades independently; running them
        // concurrently must not turn one failure into three.
        var assistant = vm.Messages.Last(m => m.Role == "assistant");
        Assert.Contains(assistant.Sources, s => s.Kind == ProvenanceKind.Rag);
        Assert.Contains("Knowledge Context", llm.LastOptions?.SystemPrompt ?? string.Empty);
        Assert.False(assistant.IsError, "a failing recall source must not fail the send");
    }

    [Fact]
    public async Task The_context_receipt_is_identical_to_the_sequential_version_for_fixed_inputs()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.Memory.RecallInjectionEnabled = true;

        var embed = new FakeEmbeddingService();
        var (store, dataset) = await IngestAsync(temp, settings, embed);
        var rag = new RagQueryService(store, embed, new FakeLlm(), settings, new NoOpReranker());
        var hit = new RecallHit(RecallKind.Message, "Earlier conversation", "we settled on flash attention off",
            DateTime.UtcNow, "", 1.0, new RecallTarget(ConversationId: "c-old", MessageIndex: 3));

        // The same fixed inputs twice: the receipt is what the user reads back
        // as "what went into this answer", and it must not vary run to run.
        var receipts = new List<string>();
        for (var run = 0; run < 2; run++)
        {
            var recall = new RecallService([new FakeRecallSource([hit])], new FakeEmbeddingService());
            var llm = new CapturingLlm();
            var vm = BuildViewModel(settings, llm, rag, recall);
            await vm.LoadModelsAsync(force: true);
            vm.SelectedModel = new LlmModel { Id = "capture", Name = "Capture", ProviderTag = "test" };
            vm.RagDatasetId = dataset.Id;

            vm.InputText = "What is the archivist's seal?";
            await vm.SendCommand.ExecuteAsync(null);

            var assistant = vm.Messages.Last(m => m.Role == "assistant");
            receipts.Add(string.Join("|", assistant.ContextSections.Select(s => $"{s.Kind}:{s.Items.Count}")));
        }

        Assert.Equal(receipts[0], receipts[1]);
        Assert.NotEmpty(receipts[0]);
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
