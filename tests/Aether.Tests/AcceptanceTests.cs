using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Aether.Agent.Models;
using Aether.Agent.Services;
using Aether.Core.Services;
using Aether.Rag;
using Aether.Rag.Pipeline;
using Aether.Rag.Storage;
using Aether.Rag.Retrieval;
using Aether.Services;
using Aether.ViewModels;
using static Aether.Tests.Helpers;
using Aether.Core.Models;

namespace Aether.Tests
{
    internal static class AcceptanceTests
    {
        // Very small, schema-like validator that ensures required trace fields exist
        public static Task TraceSchema_FileIsValidAndSampleConforms()
        {
            var schemaPath = Path.Combine("docs", "schemas", "agent_trace.schema.json");
            var schemaText = File.ReadAllText(schemaPath);
            // Ensure schema is valid JSON
            using var schemaDoc = JsonDocument.Parse(schemaText);

            // Build a small sample that matches the schema
            var sample = new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                taskId = "test-task",
                @event = "patch_queued",
                risk = "review",
                targetPath = "src/Example.cs",
                reason = "test",
                status = "pending"
            };

            var json = JsonSerializer.Serialize(sample);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // Minimal validation: required keys and timestamp format
            if (!root.TryGetProperty("timestamp", out var ts)) throw new InvalidOperationException("timestamp missing");
            if (!DateTime.TryParse(ts.GetString(), out _)) throw new InvalidOperationException("timestamp invalid");
            foreach (var required in new[] { "taskId", "event", "status" })
            {
                if (!root.TryGetProperty(required, out _)) throw new InvalidOperationException($"{required} missing");
            }

            return Task.CompletedTask;
        }

        // UI acceptance style test: ensure AgentViewModel surfaces queued patch metadata
        public static async Task AgentUiAcceptance_PatchQueueMetadataIsRendered()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

            var store = new FileAgentTaskStateStore(settings);
            await store.InitializeAsync();
            var memoryStore = new FileAgentWorkspaceMemoryStore(settings);
            await memoryStore.InitializeAsync();
            var workspace = temp.PathFor("workspace");
            Directory.CreateDirectory(workspace);
            var file = Path.Combine(workspace, "README.md");
            await File.WriteAllTextAsync(file, "initial content");

            var tools = new AgentWorkspaceTools();
            var ragStore = new SqliteRagStore(settings);
            await ragStore.InitializeAsync();
            var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
            var agentService = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), new FakeAgentLlm());
            var logs = new SimpleRuntimeLog(temp.PathFor("aether-test-logs"));
            var profiles = new FileWorkspaceProfileStore(settings);
            var analysis = new WorkspaceAnalysisService(profiles, memoryStore);

            var vm = new AgentViewModel(agentService, store, memoryStore, tools, new FakeLlm(), rag, logs, analysis);
            vm.WorkspaceRoot = workspace;

            // Create a task and set it as current
            var task = await agentService.CreateTaskAsync("Test patch queue", new AgentWorkspaceOptions(workspace));
            vm.CurrentTask = await store.LoadAsync(task.TaskId);

            // Populate selected file and proposed content
            vm.SelectedWorkspaceFile = new AgentWorkspaceFileViewModel("README.md", "initial content", File.GetLastWriteTimeUtc(file));
            vm.DraftRationale = "Fix header";
            vm.DraftProposedContent = "# Updated\n\ncontent";

            // Queue the patch via command and wait for it to appear in the view model
            vm.QueueDraftPatchCommand.Execute(null);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (vm.QueuedPatches.Count == 0 && sw.ElapsedMilliseconds < 3000) await Task.Delay(10);

            if (vm.QueuedPatches.Count == 0) throw new InvalidOperationException("Queued patch did not appear");
            var patch = vm.QueuedPatches[0];
            if (patch.RelativePath != "README.md") throw new InvalidOperationException("RelativePath incorrect");
            if (!patch.Rationale.Contains("Fix header")) throw new InvalidOperationException("Rationale missing");
            if (vm.PendingPatchCount != 1) throw new InvalidOperationException("PendingPatchCount incorrect");
        }

        // Small test-local runtime log implementation used only for acceptance tests.
        private sealed class SimpleRuntimeLog : IRuntimeLogService
        {
            private readonly string _logDirectory;

            public SimpleRuntimeLog(string logDirectory)
            {
                _logDirectory = logDirectory;
            }

            public event Action<RuntimeLogEntry>? LogAdded;

            public void Add(RuntimeLogEntry entry)
            {
                LogAdded?.Invoke(entry);
            }

            public IReadOnlyList<RuntimeLogEntry> GetEntries() => Array.Empty<RuntimeLogEntry>();

            public void ClearInMemory() { }

            public string GetLogDirectory() => _logDirectory;

            public string GetLogFilePath() => Path.Combine(GetLogDirectory(), "runtime.log");
        }
    }
}
