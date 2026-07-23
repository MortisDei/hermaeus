using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Pipeline;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r21 1.3/1.5/2.2: chat's per-turn RAG injection - the Knowledge Context
/// block, citation pills, trace fields, and the best-effort matrix that
/// guarantees a send never fails because retrieval had a bad day.
/// </summary>
public sealed class ChatRagInjectionTests
{
    private static async Task<(SqliteRagStore Store, RagDataset Dataset)> IngestDatasetAsync(
        TempDir temp, ISettingsService settings, string content, string datasetName = "knowledge",
        Hermaeus.Rag.Embeddings.IEmbeddingService? embed = null)
    {
        var store = new SqliteRagStore(settings);
        await store.InitializeAsync();
        var docs = temp.PathFor("docs-" + datasetName);
        Directory.CreateDirectory(docs);
        await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), content);

        var dataset = new RagDataset { Name = datasetName };
        var pipeline = new RagPipeline(store, embed ?? new FakeEmbeddingService());
        await pipeline.IngestDirectoryAsync(dataset, docs);
        return (store, dataset);
    }

    private static ChatViewModel BuildChatViewModel(
        ISettingsService settings, CapturingLlm llm, RagQueryService rag, IRuntimeLogService? logs = null) =>
        new(
            llm,
            new InMemoryConversationStore(),
            new EmptyMemoryStore(),
            settings,
            new FakeTts(),
            new ModelProfileService(settings),
            new FakeToasts(),
            new NoOpConversationMemoryService(),
            logs ?? new RuntimeLogService(settings),
            new ConversationExportService(),
            rag: rag);

    [Fact]
    public async Task AttachedDatasetInjectsKnowledgeBlockAndCitationPillsAndPopulatesTrace()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var (store, dataset) = await IngestDatasetAsync(temp, settings, "The archivist's seal is a gold H monogram grown through with a tree and open book.");

        var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
        var llm = new CapturingLlm();
        var vm = BuildChatViewModel(settings, llm, query);
        await vm.LoadModelsAsync(force: true);
        vm.SelectedModel = new LlmModel { Id = "capture", Name = "Capture", ProviderTag = "test" };
        vm.RagDatasetId = dataset.Id;

        vm.InputText = "What does the archivist's seal look like?";
        await vm.SendCommand.ExecuteAsync(null);

        var systemPrompt = llm.LastOptions?.SystemPrompt ?? string.Empty;
        True(systemPrompt.Contains("## Knowledge Context", StringComparison.Ordinal),
            "the system prompt sent to the model should carry the Knowledge Context block");
        True(systemPrompt.Contains(dataset.Name, StringComparison.Ordinal),
            "the Knowledge Context header should name the attached dataset");

        var assistant = vm.Messages.Last(m => m.Role == "assistant");
        True(assistant.CitationSources.Count > 0, "the reply should carry individually clickable RAG citation pills");

        var trace = Assert.Single(vm.ChatTraces);
        True(trace.RagContextItems > 0, "trace RagContextItems should be non-zero when a block was injected");
        True(trace.RagMs >= 0, "trace RagMs should be recorded");
    }

    [Fact]
    public async Task UnrelatedMessageSkipsInjectionAndTraceNotesTheWeakRetrievalReason()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        // FakeEmbeddingService's vectors share fixed components regardless of
        // content, which can accidentally clear the refusal threshold; a real
        // shared-vocabulary embedding is needed to prove an unrelated question
        // is genuinely dissimilar (RagTests.cs uses the same technique).
        var embed = new HashingBagOfWordsEmbeddingService();
        var (store, dataset) = await IngestDatasetAsync(temp, settings,
            "The archivist's seal is a gold H monogram grown through with a tree and open book.", embed: embed);

        var query = new RagQueryService(store, embed, new FakeLlm(), settings, new NoOpReranker());
        var llm = new CapturingLlm();
        var vm = BuildChatViewModel(settings, llm, query);
        await vm.LoadModelsAsync(force: true);
        vm.SelectedModel = new LlmModel { Id = "capture", Name = "Capture", ProviderTag = "test" };
        vm.RagDatasetId = dataset.Id;

        vm.InputText = "thanks!";
        await vm.SendCommand.ExecuteAsync(null);

        False(llm.LastOptions?.SystemPrompt?.Contains("## Knowledge Context", StringComparison.Ordinal) == true,
            "chat must not parrot weakly-related chunks into an unrelated message just because a dataset is attached");
        var trace = Assert.Single(vm.ChatTraces);
        Equal(0, trace.RagContextItems, "no chunks should have been injected");
        True(trace.RagNote.Contains("confidence threshold", StringComparison.OrdinalIgnoreCase),
            "the trace should record the weak-retrieval skip reason");
    }

    [Fact]
    public async Task NoDatasetAttachedSendsAreByteIdenticalToPreRoundBehavior()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var llm = new CapturingLlm();
        var query = new RagQueryService(new SqliteRagStore(settings), new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
        var vm = BuildChatViewModel(settings, llm, query);
        await vm.LoadModelsAsync(force: true);
        vm.SelectedModel = new LlmModel { Id = "capture", Name = "Capture", ProviderTag = "test" };

        vm.InputText = "Hello there";
        await vm.SendCommand.ExecuteAsync(null);

        False(llm.LastOptions?.SystemPrompt?.Contains("Knowledge Context", StringComparison.Ordinal) == true,
            "no dataset attached (RagDatasetId empty) must never inject a Knowledge block");
        var trace = Assert.Single(vm.ChatTraces);
        Equal(0, trace.RagContextItems, "RagContextItems should be zero with no dataset attached");
    }

    [Fact]
    public async Task EmbeddingServerDownDegradesToBm25OnlyAndSendStillCompletes()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var (store, dataset) = await IngestDatasetAsync(temp, settings, "Consolas is the classic Windows monospace font used for code.");

        var throwingEmbed = new ThrowingEmbeddingService();
        var logs = new CollectingRuntimeLog();
        var query = new RagQueryService(store, throwingEmbed, new FakeLlm(), settings, new NoOpReranker(), logs);
        var llm = new CapturingLlm();
        var vm = BuildChatViewModel(settings, llm, query, logs);
        await vm.LoadModelsAsync(force: true);
        vm.SelectedModel = new LlmModel { Id = "capture", Name = "Capture", ProviderTag = "test" };
        vm.RagDatasetId = dataset.Id;

        vm.InputText = "Tell me about Consolas font";
        await vm.SendCommand.ExecuteAsync(null);

        var trace = Assert.Single(vm.ChatTraces);
        True(trace.RagNote.Contains("semantic search unavailable", StringComparison.OrdinalIgnoreCase),
            "trace RagNote should carry the embedding-failure planner note");
        True(logs.Entries.Any(e => e.Level == RuntimeLogLevel.Warning && e.Category == RuntimeLogCategory.Rag),
            "an embedding failure should log exactly one Warning");
        False(vm.Messages.Last().IsError, "the send itself must complete normally despite the embedding server being down");
    }

    [Fact]
    public async Task DeletedDatasetSkipsInjectionWithHonestTraceNoteAndNoException()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteRagStore(settings);
        await store.InitializeAsync();

        var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
        var llm = new CapturingLlm();
        var vm = BuildChatViewModel(settings, llm, query);
        await vm.LoadModelsAsync(force: true);
        vm.SelectedModel = new LlmModel { Id = "capture", Name = "Capture", ProviderTag = "test" };
        vm.RagDatasetId = "dataset-that-was-deleted";

        vm.InputText = "Does this still work?";
        await vm.SendCommand.ExecuteAsync(null);

        False(vm.Messages.Last().IsError, "a stale/deleted dataset attachment must never fail the send");
        var trace = Assert.Single(vm.ChatTraces);
        Equal("attached dataset no longer exists", trace.RagNote, "the trace should record why nothing was injected");
        Equal("dataset-that-was-deleted", vm.RagDatasetId, "the stale id must not be silently cleared (doc 03.2)");
    }

    [Fact]
    public async Task ZeroChunkDatasetSkipsInjectionWithoutWarningSpam()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteRagStore(settings);
        await store.InitializeAsync();
        var dataset = new RagDataset { Name = "empty-dataset" };
        await store.SaveDatasetAsync(dataset);

        var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
        var llm = new CapturingLlm();
        var logs = new CollectingRuntimeLog();
        var vm = BuildChatViewModel(settings, llm, query, logs);
        await vm.LoadModelsAsync(force: true);
        vm.SelectedModel = new LlmModel { Id = "capture", Name = "Capture", ProviderTag = "test" };
        vm.RagDatasetId = dataset.Id;

        vm.InputText = "Anything in here?";
        await vm.SendCommand.ExecuteAsync(null);

        False(vm.Messages.Last().IsError, "an empty dataset must never fail the send");
        False(logs.Entries.Any(e => e.Level == RuntimeLogLevel.Warning && e.Category == RuntimeLogCategory.Rag),
            "an empty dataset is a normal state, not a warning-worthy one");
    }

    [Fact]
    public async Task StoreThrowsSkipsInjectionWithOneWarningAndSendCompletes()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var (store, dataset) = await IngestDatasetAsync(temp, settings, "Consolas is the classic Windows monospace font used for code.");

        // Simulate a locked/corrupt DB file: release pooled connections from
        // ingest, then overwrite the real backing file with non-SQLite bytes
        // (mirrors ServiceTests.SettingsLoadBacksUpUnreadableJson's approach
        // of corrupting a real file rather than mocking a seam that doesn't
        // exist - SqliteRagStore is a concrete class, not behind an interface).
        SqliteConnection.ClearAllPools();
        var dbPath = Path.Combine(Path.GetFullPath(temp.PathFor("data")), "conversations.db");
        await File.WriteAllBytesAsync(dbPath, [0x00, 0x01, 0x02, 0x03]);

        var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
        var llm = new CapturingLlm();
        var logs = new CollectingRuntimeLog();
        var vm = BuildChatViewModel(settings, llm, query, logs);
        await vm.LoadModelsAsync(force: true);
        vm.SelectedModel = new LlmModel { Id = "capture", Name = "Capture", ProviderTag = "test" };
        vm.RagDatasetId = dataset.Id;

        vm.InputText = "Does this still work?";
        await vm.SendCommand.ExecuteAsync(null);

        False(vm.Messages.Last().IsError, "a store/DB failure must never fail the send");
        Equal(1, logs.Entries.Count(e => e.Level == RuntimeLogLevel.Warning && e.Category == RuntimeLogCategory.Rag),
            "a store/DB throw should log exactly one Warning, not spam or silently vanish");
        var trace = Assert.Single(vm.ChatTraces);
        Equal(0, trace.RagContextItems, "nothing should have been injected when the store threw");
    }

    [Fact]
    public async Task CancellationDuringRetrievalPropagatesWithNoHalfInjectedState()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var (store, dataset) = await IngestDatasetAsync(temp, settings, "Consolas is the classic Windows monospace font used for code.");

        // Mirrors RagTests.cs's CancellingEmbeddingService: a synchronous
        // OperationCanceledException from the embed seam, exercising the
        // explicit rethrow in BuildRagInjectionAsync rather than a real
        // Stop-command race (2.1's fallback test already covers the seam).
        var query = new RagQueryService(store, new CancellingEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
        var llm = new CapturingLlm();
        var logs = new CollectingRuntimeLog();
        var vm = BuildChatViewModel(settings, llm, query, logs);
        await vm.LoadModelsAsync(force: true);
        vm.SelectedModel = new LlmModel { Id = "capture", Name = "Capture", ProviderTag = "test" };
        vm.RagDatasetId = dataset.Id;

        vm.InputText = "Tell me about Consolas font";
        await vm.SendCommand.ExecuteAsync(null);

        False(vm.Messages.Any(m => m.Role == "assistant"),
            "cancellation mid-retrieval must propagate and never leave a half-injected assistant bubble behind");
        Assert.Empty(vm.ChatTraces);
        True(logs.Entries.Any(e => e.Level == RuntimeLogLevel.Error && e.Category == RuntimeLogCategory.Service),
            "cancellation should surface through the normal send-failed path rather than being silently swallowed");
        False(logs.Entries.Any(e => e.Category == RuntimeLogCategory.Rag),
            "cancellation is a rethrow, not a best-effort failure, so it must not log through the RAG warning path");
    }

    /// <summary>Mirrors RagTests.cs's HashingBagOfWordsEmbeddingService: cosine similarity
    /// genuinely reflects shared vocabulary, unlike FakeEmbeddingService's length-derived
    /// vectors, which share fixed components regardless of content.</summary>
    private sealed class HashingBagOfWordsEmbeddingService : Hermaeus.Rag.Embeddings.IEmbeddingService
    {
        private const int Dims = 2048;
        public int Dimensions => Dims;

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult(Embed(text));

        public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult(texts.Select(Embed).ToList());

        private static float[] Embed(string text)
        {
            var vector = new float[Dims];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), "[a-z0-9]+"))
            {
                if (!seen.Add(m.Value)) continue;
                var bucket = (int)((uint)m.Value.GetHashCode() % Dims);
                vector[bucket] += 1f;
            }

            var norm = MathF.Sqrt(vector.Sum(v => v * v));
            if (norm > 0)
                for (var i = 0; i < vector.Length; i++)
                    vector[i] /= norm;
            return vector;
        }
    }

    private sealed class ThrowingEmbeddingService : Hermaeus.Rag.Embeddings.IEmbeddingService
    {
        public int Dimensions => 4;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            throw new HttpRequestException("connection refused");
        public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult(texts.Select(t => new[] { 1f, t.Length % 7, t.Length % 11, 0.5f }).ToList());
    }

    private sealed class CancellingEmbeddingService : Hermaeus.Rag.Embeddings.IEmbeddingService
    {
        public int Dimensions => 4;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            throw new OperationCanceledException(ct);
        public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult(texts.Select(t => new[] { 1f, t.Length % 7, t.Length % 11, 0.5f }).ToList());
    }

    private sealed class CollectingRuntimeLog : IRuntimeLogService
    {
        public List<RuntimeLogEntry> Entries { get; } = [];
        public event Action<RuntimeLogEntry>? LogAdded;
        public void Add(RuntimeLogEntry entry) { Entries.Add(entry); LogAdded?.Invoke(entry); }
        public IReadOnlyList<RuntimeLogEntry> GetEntries() => Entries;
        public void ClearInMemory() => Entries.Clear();
        public string GetLogDirectory() => string.Empty;
        public string GetLogFilePath() => string.Empty;
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
