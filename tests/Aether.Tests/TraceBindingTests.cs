using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Aether.Rag;
using Aether.Rag.Pipeline;
using Aether.Rag.Storage;
using Aether.Rag.Retrieval;
using Aether.ViewModels;
using Aether.Services;
using Aether.Core.Services;
using static Aether.Tests.Helpers;

namespace Aether.Tests
{
    internal static class TraceBindingTests
    {
        // Verifies that the view model parses the __RAG_TRACE__ token and binds fields correctly.
        public static async Task RagViewModel_ParsesTraceBindings()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            // create a dataset with no chunks so the query will likely preflight-refuse
            var ds = new Aether.Rag.Models.RagDataset { Name = "trace-test" };
            await store.SaveDatasetAsync(ds);

            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            var eval = new Aether.Rag.Eval.RagEvalService(query, settings);
            var toasts = new FakeToasts();
            var logs = new SimpleRuntimeLog();

            var vm = new RagViewModel(query, pipeline, eval, toasts, logs);
            vm.SelectedDataset = (await query.GetDatasetsAsync()).FirstOrDefault(d => d.Name == ds.Name);
            vm.QuestionText = "will cause refusal";

            // Execute the query command and wait for completion
            vm.QueryCommand.Execute(null);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (vm.IsQuerying && sw.ElapsedMilliseconds < 5000) await Task.Delay(10);

            True(!string.IsNullOrWhiteSpace(vm.LastTraceId), "LastTraceId should be set from trace");
            True(vm.TraceRefused, "Trace should indicate refusal for empty dataset");
            True(vm.RefusalReason.Contains("Preflight grounding") || vm.RefusalReason.Length > 0, "RefusalReason should be present");
        }

        // Ingest a couple of small documents and assert retrieval returns sources and emits a trace token.
        public static async Task RagIntegration_SmallDatasetRetrievalAndTrace()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "apple banana carrot");
            await File.WriteAllTextAsync(Path.Combine(docs, "b.txt"), "delta echo foxtrot");

            var dataset = new Aether.Rag.Models.RagDataset { Name = "small" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs);

            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());

            // call RetrieveAsync to ensure candidates present
            var retrieval = await query.RetrieveAsync(dataset.Id, "apple", new RagQueryOptions(TopK: 3));
            True(retrieval.Selected.Count > 0, "RetrieveAsync should return at least one selected context");

            // StreamQueryAsync should yield sources header and then a trace
            var sawSources = false;
            var sawTrace = false;
            await foreach (var token in query.StreamQueryAsync(dataset.Id, "apple", new RagQueryOptions(TopK: 3)))
            {
                if (token.StartsWith("__RAG_SOURCES__")) sawSources = true;
                if (token.StartsWith("__RAG_TRACE__")) sawTrace = true;
            }

            True(sawSources, "StreamQueryAsync should yield a sources header");
            True(sawTrace, "StreamQueryAsync should yield a trace token at the end");
        }

        // Minimal runtime log implementation for tests
        private sealed class SimpleRuntimeLog : IRuntimeLogService
        {
            public event Action<Aether.Core.Models.RuntimeLogEntry>? LogAdded;
            public void Add(Aether.Core.Models.RuntimeLogEntry entry) => LogAdded?.Invoke(entry);
            public IReadOnlyList<Aether.Core.Models.RuntimeLogEntry> GetEntries() => Array.Empty<Aether.Core.Models.RuntimeLogEntry>();
            public void ClearInMemory() { }
            public string GetLogDirectory() => Path.GetTempPath();
            public string GetLogFilePath() => Path.Combine(GetLogDirectory(), "runtime.log");
        }
    }
}
