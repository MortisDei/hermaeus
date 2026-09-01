using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Pipeline;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// The RAG question box used to keep the question after a successful ask, which
/// reads as "that was never sent" next to an answer that plainly was. It now
/// behaves like the chat composer: empty on send, with the question echoed above
/// the answer, and put back only when there is no answer to show for it.
/// </summary>
public sealed class RagQuestionBoxTests
{
    private static async Task<(RagViewModel Vm, RagDataset Dataset, RagQueryService Query)> NewAsync(TempDir temp, ILlmService llm)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SqliteRagStore(settings);
        await store.InitializeAsync();
        var embed = new FakeEmbeddingService();
        var query = new RagQueryService(store, embed, llm, settings, new NoOpReranker());
        var pipeline = new RagPipeline(store, embed);
        var eval = new Hermaeus.Rag.Eval.RagEvalService(query, settings, new FakeEvalStore());
        var vm = new RagViewModel(query, pipeline, eval, new FakeToasts(), new RuntimeLogService(settings), settings,
            services: null, xtts: null, kokoro: null, activity: null, watchedSources: null);

        var dataset = new RagDataset { Name = "ask-ds" };
        await query.SaveDatasetAsync(dataset);

        var docs = temp.PathFor("docs");
        Directory.CreateDirectory(docs);
        await File.WriteAllTextAsync(Path.Combine(docs, "note.txt"),
            "Hermaeus Mora is the daedric prince of knowledge and memory.");
        await pipeline.IngestDirectoryAsync(dataset, docs);

        await vm.LoadDatasetsAsync();
        vm.SelectedDataset = vm.Datasets.FirstOrDefault(d => d.Id == dataset.Id);
        return (vm, dataset, query);
    }

    [Fact]
    public async Task A_sent_question_leaves_the_box_and_is_shown_with_its_answer()
    {
        using var temp = new TempDir();
        var (vm, _, _) = await NewAsync(temp, new FakeLlm());
        vm.ChatModelProvider = () => "fake";
        vm.QuestionText = "  Who is Hermaeus Mora?  ";

        Assert.Single(vm.QueryDatasetOptions);
        Assert.True(vm.QueryDatasetOptions[0].IsIncluded,
            "loading RAG datasets should include the selected dataset by default");

        await vm.QueryCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.QuestionText);
        // Trimmed, because the echo is the question as asked, not as typed.
        Assert.Equal("Who is Hermaeus Mora?", vm.AskedQuestion);
        Assert.True(vm.HasAskedQuestion);
        Assert.True(vm.HasAnswer);
    }

    [Fact]
    public async Task A_failed_question_goes_back_in_the_box_to_be_retried()
    {
        using var temp = new TempDir();
        var (vm, _, _) = await NewAsync(temp, new ThrowingLlm());
        vm.ChatModelProvider = () => "fake";
        vm.QuestionText = "Who is Hermaeus Mora?";

        await vm.QueryCommand.ExecuteAsync(null);

        Assert.Equal("Who is Hermaeus Mora?", vm.QuestionText);
        Assert.True(vm.IsError);
        // No answer was produced, so nothing should claim one was asked for.
        Assert.Equal(string.Empty, vm.AskedQuestion);
        Assert.False(vm.HasAskedQuestion);
    }

    [Fact]
    public async Task Retyping_during_a_failing_query_is_not_overwritten_by_the_restore()
    {
        using var temp = new TempDir();
        var (vm, _, _) = await NewAsync(temp, new ThrowingLlm());
        vm.ChatModelProvider = () => "fake";
        vm.QuestionText = "first";

        var run = vm.QueryCommand.ExecuteAsync(null);
        vm.QuestionText = "second";
        await run;

        Assert.Equal("second", vm.QuestionText);
    }

    [Fact]
    public async Task A_query_can_include_more_than_one_explicit_dataset()
    {
        using var temp = new TempDir();
        var (_, first, query) = await NewAsync(temp, new FakeLlm());
        var second = new RagDataset { Name = "second-dataset" };
        await query.SaveDatasetAsync(second);

        string? traceDatasetId = null;
        await foreach (var evt in query.StreamQueryAsync(
            new[] { first.Id, second.Id },
            "Who is Hermaeus Mora?",
            new RagQueryOptions(TopK: 3)))
        {
            if (evt.Kind == RagStreamEventKind.Trace)
                traceDatasetId = evt.Trace?.DatasetId;
        }

        Assert.Contains(first.Id, traceDatasetId, StringComparison.Ordinal);
        Assert.Contains(second.Id, traceDatasetId, StringComparison.Ordinal);
    }

    private sealed class ThrowingLlm : ILlmService
    {
        public string ProviderName => "Throwing";
        public bool IsConfigured => true;

        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<LlmModel>());

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            throw new InvalidOperationException("the model went away mid-answer");
#pragma warning disable CS0162 // Required to make this an iterator.
            yield break;
#pragma warning restore CS0162
        }
    }
}
