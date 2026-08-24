using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

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
    public async Task HasNoAvailableModelsDrivesTheChatEmptyState()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = new ChatViewModel(
            new UsageLlm(),
            new InMemoryConversationStore(),
            new EmptyMemoryStore(),
            settings,
            new FakeTts(),
            new ModelProfileService(settings),
            new FakeToasts(),
            new NoOpConversationMemoryService(),
            new RuntimeLogService(settings),
            new ConversationExportService());

        Assert.True(vm.HasNoAvailableModels, "Before any models load, the empty state should offer setup guidance.");

        await vm.LoadModelsAsync(force: true);

        Assert.False(vm.HasNoAvailableModels, "Once a provider returns models, the setup-guidance empty state must not show.");
    }

    [Fact]
    public void NoModelRecoveryOnlyOffersTheWizardForIncompleteOnboarding()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = new ChatViewModel(
            new UsageLlm(),
            new InMemoryConversationStore(),
            new EmptyMemoryStore(),
            settings,
            new FakeTts(),
            new ModelProfileService(settings),
            new FakeToasts(),
            new NoOpConversationMemoryService(),
            new RuntimeLogService(settings),
            new ConversationExportService());
        var destinations = new List<string>();
        vm.RequestNavigate = destinations.Add;

        Assert.True(vm.ShowSetupWizardFromEmptyState);
        vm.OpenSetupWizardFromEmptyStateCommand.Execute(null);
        Assert.Equal("wizard", destinations[^1]);

        settings.Settings.SetupWizardCompleted = true;
        vm.RefreshSetupState();

        Assert.False(vm.ShowSetupWizardFromEmptyState);
        vm.OpenSetupWizardFromEmptyStateCommand.Execute(null);
        Assert.Equal("services", destinations[^1]);
    }

    [Fact]
    public async Task LongConversationRendersOnlyTheNewestWindowUntilShowEarlierIsClicked()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var store = new InMemoryConversationStore();
        var conversation = new Conversation { Id = "long-convo", Title = "Long" };
        for (var i = 0; i < 250; i++)
            conversation.Messages.Add(new Message { Role = i % 2 == 0 ? "user" : "assistant", Content = $"message {i}" });
        await store.SaveAsync(conversation);

        var vm = new ChatViewModel(
            new UsageLlm(),
            store,
            new EmptyMemoryStore(),
            settings,
            new FakeTts(),
            new ModelProfileService(settings),
            new FakeToasts(),
            new NoOpConversationMemoryService(),
            new RuntimeLogService(settings),
            new ConversationExportService());

        await vm.LoadConversationAsync("long-convo");

        Assert.Equal(250, vm.Messages.Count);
        Assert.Equal(100, vm.VisibleMessages.Count);
        Assert.Equal("message 249", vm.VisibleMessages[^1].Content);
        Assert.True(vm.HasEarlierMessages, "A 250-message conversation windowed to 100 must offer to show earlier messages.");

        vm.ShowEarlierMessagesCommand.Execute(null);

        Assert.Equal(200, vm.VisibleMessages.Count);
        Assert.Equal("message 249", vm.VisibleMessages[^1].Content);
        Assert.True(vm.HasEarlierMessages, "50 messages still remain hidden after one reveal.");
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

    [Fact]
    public void SelectedModelLocalityReflectsTheProviderRegistry()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = new ChatViewModel(
            new UsageLlm(),
            new InMemoryConversationStore(),
            new EmptyMemoryStore(),
            settings,
            new FakeTts(),
            new ModelProfileService(settings),
            new FakeToasts(),
            new NoOpConversationMemoryService(),
            new RuntimeLogService(settings),
            new ConversationExportService());

        Assert.False(vm.HasSelectedModel);

        vm.SelectedModel = new LlmModel { Id = "gpt", Name = "GPT", ProviderTag = "openai" };
        Assert.True(vm.HasSelectedModel);
        Assert.True(vm.IsSelectedModelRemote, "an openai-tagged model should be reported as remote");
        Assert.Equal("Remote", vm.SelectedModelLocalityLabel);

        vm.SelectedModel = new LlmModel { Id = "local-model", Name = "Local", ProviderTag = "llama.cpp" };
        Assert.False(vm.IsSelectedModelRemote, "a llama.cpp-tagged model should be reported as local");
        Assert.Equal("Local", vm.SelectedModelLocalityLabel);

        vm.SelectedModel = null;
        Assert.False(vm.HasSelectedModel);
        Assert.Equal(string.Empty, vm.SelectedModelLocalityLabel);
    }

    [Fact]
    public async Task SendAsyncInjectsRelevantMemoriesAndPopulatesSourcesWhenMemoryEnabled()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Memory.Enabled = true;
        var capturing = new CapturingLlm();
        var memorySource = new SourceReference(ProvenanceKind.Memory, "User prefers concise summaries", Locator: "conv-1");
        var memories = new SearchableMemoryStore([
            new Memory { Id = "m1", Category = "preferences", Content = "User prefers concise summaries.", Source = memorySource }
        ]);
        var vm = new ChatViewModel(
            capturing,
            new InMemoryConversationStore(),
            memories,
            settings,
            new FakeTts(),
            new ModelProfileService(settings),
            new FakeToasts(),
            new NoOpConversationMemoryService(),
            new RuntimeLogService(settings),
            new ConversationExportService(),
            memoryInjection: new MemoryInjectionService());

        await vm.LoadModelsAsync(force: true);
        vm.InputText = "How should I phrase this?";
        await vm.SendCommand.ExecuteAsync(null);

        var assistantMessage = vm.Messages.Last(m => m.IsAssistant);
        Assert.Contains(assistantMessage.Sources, s => s.Locator == "conv-1");
        Assert.NotNull(capturing.LastOptions?.SystemPrompt);
        Assert.Contains("concise summaries", capturing.LastOptions!.SystemPrompt, StringComparison.OrdinalIgnoreCase);

        // r25 doc 02: a recalled memory lands in the Memories section of the
        // collapsed-by-default context receipt, and there is no second,
        // always-visible strip for it to leak into.
        Assert.True(assistantMessage.HasContext);
        Assert.False(assistantMessage.IsContextExpanded);
        var memorySection = Assert.Single(assistantMessage.ContextSections, s => s.Kind == ProvenanceKind.Memory);
        Assert.Contains(memorySection.Items, s => s.Locator == "conv-1");
    }

    [Fact]
    public async Task SendAsyncInjectsAcceptedProjectStateAndLabelsItsReceipt()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var projects = new ProjectStore(settings);
        var project = new Project { Id = "p-state", Name = "Stateful" };
        await projects.SaveAsync(project);
        await projects.SaveStateAsync(new ProjectState
        {
            ProjectId = project.Id,
            CurrentObjective = "Verify project context",
            Items = [new ProjectStateItem { Kind = ProjectStateItemKind.Constraint, Text = "Accepted only" }]
        }, 0);
        var capturing = new CapturingLlm();
        var vm = new ChatViewModel(
            capturing, new InMemoryConversationStore(), new EmptyMemoryStore(), settings,
            new FakeTts(), new ModelProfileService(settings), new FakeToasts(),
            new NoOpConversationMemoryService(), new RuntimeLogService(settings),
            new ConversationExportService(), projectState: projects);
        vm.ActiveProjectProvider = () => project;
        vm.NewConversation();
        await vm.LoadModelsAsync(force: true);
        vm.InputText = "What is next?";
        await vm.SendCommand.ExecuteAsync(null);

        Assert.Contains("Verify project context", capturing.LastOptions!.SystemPrompt, StringComparison.Ordinal);
        var assistant = vm.Messages.Last(message => message.IsAssistant);
        var section = Assert.Single(assistant.ContextSections, item => item.Kind == ProvenanceKind.ProjectState);
        Assert.Equal("Project State", section.Label);
        Assert.Contains(":state:1:", Assert.Single(section.Items).Locator, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsyncSkipsMemoryInjectionWhenMemoryDisabled()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Memory.Enabled = false;
        var capturing = new CapturingLlm();
        var memories = new SearchableMemoryStore([
            new Memory { Id = "m1", Category = "preferences", Content = "User prefers concise summaries.", Source = new SourceReference(ProvenanceKind.Memory, "x", Locator: "conv-1") }
        ]);
        var vm = new ChatViewModel(
            capturing,
            new InMemoryConversationStore(),
            memories,
            settings,
            new FakeTts(),
            new ModelProfileService(settings),
            new FakeToasts(),
            new NoOpConversationMemoryService(),
            new RuntimeLogService(settings),
            new ConversationExportService(),
            memoryInjection: new MemoryInjectionService());

        await vm.LoadModelsAsync(force: true);
        vm.InputText = "How should I phrase this?";
        await vm.SendCommand.ExecuteAsync(null);

        var assistantMessage = vm.Messages.Last(m => m.IsAssistant);
        Assert.Empty(assistantMessage.Sources);
        False(capturing.LastOptions?.SystemPrompt?.Contains("concise summaries", StringComparison.OrdinalIgnoreCase) ?? false,
            "memory context should not be injected when Memory.Enabled is false");
    }

    [Fact]
    public async Task SendAsyncInjectsGlobalAgentLessonsWhenToggleEnabled()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Memory.Enabled = true;
        settings.Settings.Memory.ConsumeAgentLessonsInChat = true;
        var lessons = new SqliteLessonStore(settings);
        await lessons.RecordEvidenceAsync(new AgentLessonEvidence(
            AgentLessonScope.Global, "", AgentLessonKind.Stated, "stated:commit-style",
            "The user prefers terse commit messages.", "", AgentLessonOutcome.Observation));

        var capturing = new CapturingLlm();
        var vm = new ChatViewModel(
            capturing,
            new InMemoryConversationStore(),
            new EmptyMemoryStore(),
            settings,
            new FakeTts(),
            new ModelProfileService(settings),
            new FakeToasts(),
            new NoOpConversationMemoryService(),
            new RuntimeLogService(settings),
            new ConversationExportService(),
            lessons: lessons);

        await vm.LoadModelsAsync(force: true);
        vm.InputText = "How should I write this commit?";
        await vm.SendCommand.ExecuteAsync(null);

        Assert.Contains("terse commit messages", capturing.LastOptions!.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsyncSkipsGlobalAgentLessonsWhenToggleDisabled()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Memory.Enabled = true;
        settings.Settings.Memory.ConsumeAgentLessonsInChat = false;
        var lessons = new SqliteLessonStore(settings);
        await lessons.RecordEvidenceAsync(new AgentLessonEvidence(
            AgentLessonScope.Global, "", AgentLessonKind.Stated, "stated:commit-style",
            "The user prefers terse commit messages.", "", AgentLessonOutcome.Observation));

        var capturing = new CapturingLlm();
        var vm = new ChatViewModel(
            capturing,
            new InMemoryConversationStore(),
            new EmptyMemoryStore(),
            settings,
            new FakeTts(),
            new ModelProfileService(settings),
            new FakeToasts(),
            new NoOpConversationMemoryService(),
            new RuntimeLogService(settings),
            new ConversationExportService(),
            lessons: lessons);

        await vm.LoadModelsAsync(force: true);
        vm.InputText = "How should I write this commit?";
        await vm.SendCommand.ExecuteAsync(null);

        False(capturing.LastOptions?.SystemPrompt?.Contains("terse commit messages", StringComparison.OrdinalIgnoreCase) ?? false,
            "agent lessons should not be injected into chat when the toggle is off");
    }

    [Fact]
    public async Task SpeakMessageSkipsBlankContent()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var tts = new CapturingTts();
        var vm = new ChatViewModel(
            new FakeLlm(),
            new InMemoryConversationStore(),
            new EmptyMemoryStore(),
            settings,
            tts,
            new ModelProfileService(settings),
            new FakeToasts(),
            new NoOpConversationMemoryService(),
            new RuntimeLogService(settings),
            new ConversationExportService());

        await vm.SpeakMessageCommand.ExecuteAsync(new MessageViewModel { Role = "assistant", Content = "   " });

        False(tts.SpeakCalled, "blank message content should not be sent to TTS");
    }

    [Fact]
    public async Task SpeakMessageAsync_routes_through_the_voice_orchestrator_when_one_is_wired_in()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var voice = new FakeVoiceOrchestrator();
        var vm = new ChatViewModel(
            new FakeLlm(),
            new InMemoryConversationStore(),
            new EmptyMemoryStore(),
            settings,
            new FakeTts(),
            new ModelProfileService(settings),
            new FakeToasts(),
            new NoOpConversationMemoryService(),
            new RuntimeLogService(settings),
            new ConversationExportService(),
            voice: voice);

        await vm.SpeakMessageCommand.ExecuteAsync(new MessageViewModel { Role = "assistant", Content = "Here is `code` to run." });

        var utterance = Assert.Single(voice.Enqueued);
        Assert.Equal(VoiceChannel.Chat, utterance.Channel);
        Assert.DoesNotContain("`", utterance.Text);
        Assert.Contains(VoiceChannel.Chat, voice.StoppedChannels);
    }

    [Fact]
    public async Task SendAsync_auto_speaks_the_full_reply_when_the_setting_is_enabled()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Tts.AutoSpeakChatReplies = true;
        var voice = new FakeVoiceOrchestrator();
        var vm = new ChatViewModel(
            new FakeLlm(),
            new InMemoryConversationStore(),
            new EmptyMemoryStore(),
            settings,
            new FakeTts(),
            new ModelProfileService(settings),
            new FakeToasts(),
            new NoOpConversationMemoryService(),
            new RuntimeLogService(settings),
            new ConversationExportService(),
            voice: voice);

        await vm.LoadModelsAsync(force: true);
        vm.InputText = "Say something";
        await vm.SendCommand.ExecuteAsync(null);

        Assert.Contains(voice.Enqueued, u => u.Channel == VoiceChannel.Chat && u.Text.Contains("ready alpha beta 42"));
    }

    [Fact]
    public async Task SendAsync_does_not_auto_speak_when_the_setting_is_disabled()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Tts.AutoSpeakChatReplies = false;
        var voice = new FakeVoiceOrchestrator();
        var vm = new ChatViewModel(
            new FakeLlm(),
            new InMemoryConversationStore(),
            new EmptyMemoryStore(),
            settings,
            new FakeTts(),
            new ModelProfileService(settings),
            new FakeToasts(),
            new NoOpConversationMemoryService(),
            new RuntimeLogService(settings),
            new ConversationExportService(),
            voice: voice);

        await vm.LoadModelsAsync(force: true);
        vm.InputText = "Say something";
        await vm.SendCommand.ExecuteAsync(null);

        Assert.Empty(voice.Enqueued);
    }

    [Fact]
    public void Stop_stops_the_chat_voice_channel()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var voice = new FakeVoiceOrchestrator();
        var vm = new ChatViewModel(
            new FakeLlm(),
            new InMemoryConversationStore(),
            new EmptyMemoryStore(),
            settings,
            new FakeTts(),
            new ModelProfileService(settings),
            new FakeToasts(),
            new NoOpConversationMemoryService(),
            new RuntimeLogService(settings),
            new ConversationExportService(),
            voice: voice);

        vm.StopCommand.Execute(null);

        Assert.Contains(VoiceChannel.Chat, voice.StoppedChannels);
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

    private sealed class SearchableMemoryStore(List<Memory> memories) : IMemoryStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Memory>> GetAllAsync(bool includeArchived = false, CancellationToken ct = default) => Task.FromResult(memories);
        public Task<Memory?> GetByIdAsync(string id, CancellationToken ct = default) => Task.FromResult(memories.FirstOrDefault(m => m.Id == id));
        public Task<List<Memory>> GetByCategoryAsync(string category, CancellationToken ct = default) => Task.FromResult(memories.Where(m => m.Category == category).ToList());
        public Task<List<Memory>> GetByScopeAsync(MemoryScope scope, string? scopeId = null, bool includeArchived = false, CancellationToken ct = default) => Task.FromResult(memories);
        public Task SaveAsync(Memory memory, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Memory>> SearchAsync(string query, CancellationToken ct = default) => Task.FromResult(memories);
        public Task<List<Memory>> GetByImportanceAsync(double minScore, CancellationToken ct = default) => Task.FromResult(memories);
        public Task<List<Memory>> GetRecentAsync(int limit = 10, CancellationToken ct = default) => Task.FromResult(memories);
        public Task<List<Memory>> GetRecentByConversationAsync(string conversationId, int limit = 10, CancellationToken ct = default) => Task.FromResult(memories);
        public Task<int> GetCountByConversationAsync(string conversationId, bool includeArchived = false, CancellationToken ct = default) => Task.FromResult(memories.Count);
        public Task<Dictionary<string, int>> GetCountsByConversationAsync(IEnumerable<string> conversationIds, bool includeArchived = false, CancellationToken ct = default) =>
            Task.FromResult(conversationIds.ToDictionary(id => id, _ => memories.Count));
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

    private sealed class CapturingTts : ITtsService
    {
        public bool SpeakCalled { get; private set; }

        public Task SpeakAsync(string text, CancellationToken ct = default)
        {
            SpeakCalled = true;
            return Task.CompletedTask;
        }

        public Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> ImportVoiceSampleAsync(string sourcePath, string displayName, CancellationToken ct = default) => Task.FromResult(displayName);
        public Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(new List<string> { "default" });
    }
}
