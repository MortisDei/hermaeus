using System;
using System.Collections.Generic;
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
            var eval = new Aether.Rag.Eval.RagEvalService(query, settings, new FakeEvalStore());
            var toasts = new FakeToasts();
            var logs = new SimpleRuntimeLog(temp.PathFor("runtime-logs"));

            var vm = new RagViewModel(query, pipeline, eval, toasts, logs, settings);
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

            // StreamQueryAsync should yield a Sources event and then a Trace event
            var sawSources = false;
            var sawTrace = false;
            await foreach (var evt in query.StreamQueryAsync(dataset.Id, "apple", new RagQueryOptions(TopK: 3)))
            {
                if (evt.Kind == RagStreamEventKind.Sources) sawSources = true;
                if (evt.Kind == RagStreamEventKind.Trace) sawTrace = true;
            }

            True(sawSources, "StreamQueryAsync should yield a Sources event");
            True(sawTrace, "StreamQueryAsync should yield a Trace event at the end");
        }

        // r6 01-first-five-minutes.md 1.6: "why did retrieval choose this
        // chunk" needs a per-signal breakdown on the trace chunk, not just
        // the final fused score.
        public static async Task RagQueryStreamTraceChunksCarryScoreBreakdown()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

            var store = new SqliteRagStore(settings);
            await store.InitializeAsync();

            var docs = temp.PathFor("docs");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "apple banana carrot apple apple");
            await File.WriteAllTextAsync(Path.Combine(docs, "b.txt"), "delta echo foxtrot");

            var dataset = new Aether.Rag.Models.RagDataset { Name = "breakdown" };
            var pipeline = new RagPipeline(store, new FakeEmbeddingService());
            await pipeline.IngestDirectoryAsync(dataset, docs);

            var query = new RagQueryService(store, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());

            IReadOnlyList<Aether.Rag.Models.RagTraceChunk>? sourceChunks = null;
            await foreach (var evt in query.StreamQueryAsync(dataset.Id, "apple", new RagQueryOptions(TopK: 3)))
            {
                if (evt.Kind == RagStreamEventKind.Sources)
                    sourceChunks = evt.Sources;
            }

            True(sourceChunks is { Count: > 0 }, "sources event should carry at least one chunk");
            var top = sourceChunks![0];
            True(top.VectorScore.HasValue, "a chunk found via semantic search should carry a vector score");
            Equal(sourceChunks.Count, top.OutOfCount, "OutOfCount should reflect how many candidates were selected");
            True(top.PlainLanguageSummary.StartsWith("Ranked 1st of", StringComparison.Ordinal), "the top-ranked chunk's summary should say so");
            // NoOpReranker never rescored anything, so no chunk should claim a rerank score.
            True(sourceChunks.All(c => !c.RerankScore.HasValue), "reranker score should be absent when the reranker is a no-op");
        }

        // Minimal runtime log implementation for tests
        private sealed class SimpleRuntimeLog : IRuntimeLogService
        {
            private readonly string _logDirectory;

            public SimpleRuntimeLog(string logDirectory)
            {
                _logDirectory = logDirectory;
            }

            public event Action<Aether.Core.Models.RuntimeLogEntry>? LogAdded;
            public void Add(Aether.Core.Models.RuntimeLogEntry entry) => LogAdded?.Invoke(entry);
            public IReadOnlyList<Aether.Core.Models.RuntimeLogEntry> GetEntries() => Array.Empty<Aether.Core.Models.RuntimeLogEntry>();
            public void ClearInMemory() { }
            public string GetLogDirectory() => _logDirectory;
            public string GetLogFilePath() => Path.Combine(GetLogDirectory(), "runtime.log");
        }
    }
}
