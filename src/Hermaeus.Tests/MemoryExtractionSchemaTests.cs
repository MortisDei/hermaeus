using System.Reflection;
using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// Auto-summary asked a small model for JSON and defended against the answer
/// three times over. r28 doc 01 1.5 has it ask for a shape the sampler
/// enforces instead, on providers that can enforce one, and keeps every
/// fallback for the providers that cannot.
/// </summary>
public sealed class MemoryExtractionSchemaTests
{
    private static JsonElement Schema()
    {
        using var doc = JsonDocument.Parse(MemoryExtractionService.StructuredExtractionSchema);
        return doc.RootElement.Clone();
    }

    private static IEnumerable<string> SchemaPropertyNames(JsonElement objectSchema) =>
        objectSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name);

    private static IEnumerable<string> RecordPropertyNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name.ToLowerInvariant());

    /// <summary>
    /// The test that fails when someone adds a field to either side. The
    /// schema is hand-written on purpose; this is the cost of that choice and
    /// it is cheaper than a generator.
    /// </summary>
    [Fact]
    public void The_schema_and_the_record_agree_property_for_property()
    {
        var schema = Schema();

        Assert.Equal(
            RecordPropertyNames(typeof(MemoryExtractionService.StructuredExtractionResult)).Order(),
            SchemaPropertyNames(schema).Order());

        var item = schema.GetProperty("properties").GetProperty("memories").GetProperty("items");
        Assert.Equal(
            RecordPropertyNames(typeof(MemoryExtractionService.StructuredMemoryItem)).Order(),
            SchemaPropertyNames(item).Order());
    }

    [Fact]
    public void The_schema_categories_are_the_ones_the_parser_accepts()
    {
        var categories = Schema()
            .GetProperty("properties").GetProperty("memories").GetProperty("items")
            .GetProperty("properties").GetProperty("category").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        // Anything outside this set is silently mapped to "facts" by
        // ExtractStructuredMemoriesAsync, so constraining to it is the whole
        // point rather than a nicety.
        Assert.Equal(["facts", "preferences", "learned_behaviors", "interests"], categories);
    }

    [Fact]
    public async Task A_document_valid_against_the_schema_parses_without_any_fallback()
    {
        var response = """
            {"memories":[{"content":"Prefers Australian spelling.","category":"preferences","importance":0.8,"tags":["writing"]}]}
            """;

        var memories = await new MemoryExtractionService().ExtractStructuredMemoriesAsync(response, "conv-1");

        Assert.Single(memories);
        Assert.Equal("preferences", memories[0].Category);
        Assert.Equal(0.8, memories[0].ImportanceScore);
    }

    [Theory]
    [InlineData("No prior discussion about food paired with wine was found.")]
    [InlineData("I could not find any earlier conversation mentioning wine.")]
    public void Search_state_absence_conclusions_are_recognized_without_rejecting_the_extractor(string content)
    {
        Assert.True(MemoryExtractionService.IsUnsupportedAbsenceConclusion(content));
    }

    // ── the constrained and unconstrained paths through auto-summary ──

    private sealed class ConstraintCapturingLlm : ILlmService
    {
        private readonly bool _supportsConstraints;
        private readonly string _response;

        public ConstraintCapturingLlm(bool supportsConstraints, string response)
        {
            _supportsConstraints = supportsConstraints;
            _response = response;
        }

        public List<LlmChatOptions> Captured { get; } = [];
        public string ProviderName => "ConstraintCapturing";
        public bool IsConfigured => true;

        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<LlmModel>
            {
                new() { Id = "memory-test", Name = "Memory Test", Provider = "Test", SupportsOutputConstraints = _supportsConstraints }
            });

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            Captured.Add(options ?? LlmChatOptions.Default);
            await Task.Delay(1, ct);
            yield return new LlmStreamEvent(_response);
        }
    }

    private static async Task<(ConversationMemoryService Service, MemoryStore Memories, ConstraintCapturingLlm Llm)> BuildAsync(
        TempDir temp, bool supportsConstraints, string response)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.Memory.Enabled = true;
        settings.Settings.Memory.AutoSummarizeImportanceThreshold = 0.2;
        settings.Settings.Memory.MaxMemoriesPerConversation = 10;
        settings.Settings.Llm.DefaultModel = "memory-test";

        var conversations = new ConversationStore(settings);
        var memories = new MemoryStore(settings);
        await conversations.InitializeAsync();
        await memories.InitializeAsync();
        await conversations.SaveAsync(new Conversation
        {
            Id = "conv-1",
            Title = "Memory worthy chat",
            ModelId = "memory-test",
            Messages =
            [
                new Message { Role = "user", Content = "I prefer Australian spelling and optimisation focused solutions." },
                new Message { Role = "assistant", Content = "Noted, I will use Australian English." },
                new Message { Role = "user", Content = "Please remember this preference for future sessions too." },
                new Message { Role = "assistant", Content = "I can store that as durable memory." }
            ]
        });

        var llm = new ConstraintCapturingLlm(supportsConstraints, response);
        var service = new ConversationMemoryService(
            settings, conversations, memories, new MemoryExtractionService(), llm, new RuntimeLogService(settings));
        return (service, memories, llm);
    }

    private const string StructuredResponse = """
        {"memories":[{"content":"Prefers Australian spelling.","category":"preferences","importance":0.9,"tags":["writing"]}]}
        """;

    [Fact]
    public async Task Auto_summary_asks_for_the_schema_when_the_model_can_enforce_one()
    {
        using var temp = new TempDir();
        var (service, _, llm) = await BuildAsync(temp, supportsConstraints: true, StructuredResponse);

        await service.RunAutoSummaryAsync("conv-1");

        var constraint = Assert.Single(llm.Captured).OutputConstraint;
        Assert.NotNull(constraint);
        Assert.Equal(MemoryExtractionService.StructuredExtractionConstraintDescription, constraint!.Description);
        Assert.Equal(MemoryExtractionService.StructuredExtractionSchema, constraint.JsonSchema);
    }

    [Fact]
    public async Task Auto_summary_sends_nothing_extra_when_the_model_cannot_enforce_one()
    {
        using var temp = new TempDir();
        var (service, _, llm) = await BuildAsync(temp, supportsConstraints: false, StructuredResponse);

        await service.RunAutoSummaryAsync("conv-1");

        Assert.Null(Assert.Single(llm.Captured).OutputConstraint);
    }

    [Fact]
    public async Task The_marker_fallback_still_runs_when_the_response_is_not_structured()
    {
        using var temp = new TempDir();
        // Constrained provider, prose answer: the constraint is what should
        // make this rare, not what should make the fallback unnecessary.
        var (service, memories, _) = await BuildAsync(temp, supportsConstraints: true,
            "[MEMORY: User prefers Australian English spelling.]");

        await service.RunAutoSummaryAsync("conv-1");

        var stored = await memories.GetAllAsync(includeArchived: true);
        Assert.Contains(stored, m => m.Content.Contains("Australian", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task An_answer_that_is_neither_json_nor_a_marker_stores_nothing_and_does_not_throw()
    {
        using var temp = new TempDir();
        var (service, memories, _) = await BuildAsync(temp, supportsConstraints: true, "Sorry, I cannot do that.");

        await service.RunAutoSummaryAsync("conv-1");

        Assert.Empty(await memories.GetAllAsync(includeArchived: true));
    }

    [Fact]
    public async Task Auto_summary_does_not_promote_a_model_generated_absence_conclusion()
    {
        using var temp = new TempDir();
        var response = """
            {"memories":[{"content":"No prior discussion about food paired with wine was found.","category":"facts","importance":1,"tags":["search"]}]}
            """;
        var (service, memories, _) = await BuildAsync(temp, supportsConstraints: true, response);

        await service.RunAutoSummaryAsync("conv-1");

        Assert.DoesNotContain(await memories.GetAllAsync(includeArchived: true),
            memory => memory.Content.Contains("prior discussion", StringComparison.OrdinalIgnoreCase));
    }
}
