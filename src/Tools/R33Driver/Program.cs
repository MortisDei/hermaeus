using System.Runtime.CompilerServices;
using System.Text.Json;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Composition;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Microsoft.Extensions.DependencyInjection;

return await R33Driver.RunAsync(args);

internal static class R33Driver
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            ValidateArguments(args);
            var settingsPath = RequiredPath(args, "--settings-path", file: true);
            var dataRoot = RequiredPath(args, "--data-root", file: false);
            var workspace = RequiredPath(args, "--workspace", file: false);
            ValidateIsolation(settingsPath, dataRoot, workspace);

            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(workspace);
            var target = Path.Combine(workspace, "r33-driver.md");
            await File.WriteAllTextAsync(target, "before");

            var settings = new SettingsService(settingsPath);
            await settings.LoadAsync();
            settings.Settings.DataManagement.DataRootDirectory = dataRoot;
            settings.Settings.SetupWizardCompleted = false;
            settings.Settings.Rag.EmbeddingBaseUrl = "http://127.0.0.1:9";
            settings.Settings.Rag.EmbeddingModel = string.Empty;
            settings.Settings.Rag.RerankerEnabled = false;
            await settings.SaveAsync();

            var services = new ServiceCollection();
            services.AddHermaeusCoreServices();
            // The driver uses the production composition graph but replaces
            // only settings and the model provider with explicit scratch
            // instances. No owner settings or live provider are touched.
            services.AddSingleton<ISettingsService>(settings);
            services.AddSingleton<ILlmService, ScriptedLlm>();

            await using var provider = services.BuildServiceProvider();
            var lifecycle = provider.GetRequiredService<IApplicationLifecycleCoordinator>();
            var startup = await lifecycle.StartAsync();
            if (!startup.Ready)
            {
                Console.Error.WriteLine(JsonSerializer.Serialize(new
                {
                    ok = false,
                    error = "shared_startup_incomplete",
                    phases = startup.Phases.Where(phase => !phase.Succeeded)
                }));
                await lifecycle.ShutdownAsync(TimeSpan.FromSeconds(10));
                return 2;
            }

            try
            {
                var agent = provider.GetRequiredService<IAgentService>();
                var options = new AgentWorkspaceOptions(workspace, ModelId: "r33-driver-model");
                var created = await agent.CreateTaskAsync("Change the driver marker from before to after.", options);
                var proposed = await agent.RunAsync(created.TaskId, options);
                var pending = proposed.State.PendingToolAction
                    ?? throw new InvalidOperationException("The scripted planner did not produce a pending mutation.");
                var fingerprint = AgentApprovalFingerprint.Resolve(pending);
                var approval = await agent.AppendApprovalAsync(
                    created.TaskId,
                    "r33-driver",
                    approved: true,
                    fingerprint,
                    options);
                if (approval.Outcome != AgentMutationOutcome.Applied || string.IsNullOrWhiteSpace(approval.ReceiptId))
                    throw new InvalidOperationException($"Prepared mutation was not applied: {approval.Message}");

                await agent.RunAsync(created.TaskId, options);
                var completed = await provider.GetRequiredService<IAgentTaskStateStore>().LoadAsync(created.TaskId)
                    ?? throw new InvalidOperationException("The driver task could not be reloaded.");
                var receipt = completed.MutationReceipts.Single(item => item.ReceiptId == approval.ReceiptId);
                var content = await File.ReadAllTextAsync(target);
                if (completed.Status != AgentTaskStatus.Complete
                    || content != "after"
                    || receipt.Outcome != AgentMutationOutcome.Applied
                    || !receipt.Verified
                    || !receipt.Changed)
                    throw new InvalidOperationException("The end-to-end task, receipt, or filesystem assertion failed.");

                var benchmark = await RunBenchmarkCancellationAsync(provider);
                var rag = await RunRagGenerationAndRetrievalAsync(provider, target);
                var lab = await RunLabFailureLifecycleAsync(provider, dataRoot);

                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    ok = true,
                    task_id = completed.TaskId,
                    proposal_id = pending.ProposalId,
                    receipt_id = receipt.ReceiptId,
                    outcome = receipt.Outcome.ToString(),
                    verified = receipt.Verified,
                    changed = receipt.Changed,
                    file = Path.GetFileName(target),
                    content,
                    benchmark,
                    rag,
                    lab
                }));
                return 0;
            }
            finally
            {
                var shutdown = await lifecycle.ShutdownAsync(TimeSpan.FromSeconds(10));
                if (!shutdown.Clean)
                    Console.Error.WriteLine("R33 driver shared shutdown was incomplete.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                ok = false,
                error = ex.Message,
                exception = ex.GetType().Name
            }));
            return 1;
        }
    }

    private static async Task<object> RunBenchmarkCancellationAsync(ServiceProvider provider)
    {
        var benchmarks = provider.GetRequiredService<BenchmarkService>();
        await benchmarks.InitializeAsync();
        var suite = new BenchmarkSuite
        {
            Id = "r33-driver-cancel-suite",
            Name = "R33 driver cancellation",
            SuiteVersion = "r33-driver",
            Cases =
            [
                new BenchmarkCase
                {
                    Id = "r33-driver-case",
                    Name = "Cancellation must prevent case execution",
                    Prompt = "This case must not run after preparation cancellation."
                }
            ]
        };
        var model = new LlmModel
        {
            Id = "r33-driver-benchmark-model",
            Name = "R33 driver benchmark model",
            Provider = "R33 driver"
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var run = await benchmarks.RunAsync(
            suite,
            model,
            ct: cancellation.Token,
            preparation: token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });
        var persisted = await benchmarks.GetRunAsync(run.Id)
            ?? throw new InvalidOperationException("The cancelled benchmark run was not persisted.");
        if (!string.Equals(run.Status, "Cancelled", StringComparison.Ordinal)
            || run.Results.Count != 0
            || !string.Equals(persisted.Status, "Cancelled", StringComparison.Ordinal)
            || persisted.Results.Count != 0)
            throw new InvalidOperationException("Benchmark cancellation did not produce a terminal no-case result.");

        return new
        {
            status = run.Status,
            phase = run.CurrentPhase,
            result_count = run.Results.Count,
            persisted = true
        };
    }

    private static async Task<object> RunRagGenerationAndRetrievalAsync(
        ServiceProvider provider,
        string sourcePath)
    {
        var store = provider.GetRequiredService<SqliteRagStore>();
        var query = provider.GetRequiredService<RagQueryService>();
        var dataset = new RagDataset
        {
            Id = "r33-driver-dataset",
            Name = "R33 driver dataset",
            Description = "Scratch-only generation and retrieval evidence.",
            LastIngestPath = sourcePath,
            Config = new RagDatasetConfig
            {
                EmbeddingModel = string.Empty,
                EmbeddingDimensions = 1
            }
        };
        const string sourceId = "r33-driver-source";
        const string revisionId = "r33-driver-revision";
        const string sourceHash = "r33-driver-content-hash";
        var chunk = new RagChunk
        {
            Id = "r33-driver-chunk",
            DatasetId = dataset.Id,
            SourceFile = Path.GetFileName(sourcePath),
            SourcePath = sourcePath,
            SourceHash = sourceHash,
            SourceId = sourceId,
            SourceRevisionId = revisionId,
            SourceTitle = "R33 driver",
            Content = "R33 shared lifecycle evidence is retained in the generation.",
            ChunkIndex = 0,
            ChunkTotal = 1,
            TokenCount = 9,
            Embedding = [1f],
            ChunkKind = RagChunkKind.MarkdownSection
        };
        var stats = Bm25Scorer.BuildStats([chunk]);
        var generation = await store.PublishGenerationAsync(
            dataset,
            [chunk],
            stats,
            [new RagSourceDescriptor(sourceId, dataset.Id, null, Path.GetFileName(sourcePath), RagSourceKind.LocalFile)],
            [new RagSourceRevision(
                revisionId,
                sourceId,
                sourceHash,
                "Scratch driver source bytes.",
                "r33-driver-embedding",
                RagSourceRevisionState.Current,
                DateTime.UtcNow)],
            "r33-driver-embedding",
            1);
        var retrieved = await query.RetrieveAsync(dataset.Id, "shared lifecycle evidence", new RagQueryOptions(TopK: 1));
        var history = await store.GetGenerationHistoryAsync(dataset.Id);
        if (history.Count != 1
            || history[0].GenerationId != generation.GenerationId
            || retrieved.Selected.Count != 1
            || retrieved.Selected[0].Chunk.Id != chunk.Id)
            throw new InvalidOperationException("RAG generation publication or retrieval evidence failed.");

        return new
        {
            generation_id = generation.GenerationId,
            generation_count = history.Count,
            selected_chunk = retrieved.Selected[0].Chunk.Id,
            planner_notes = retrieved.PlannerNotes
        };
    }

    private static async Task<object> RunLabFailureLifecycleAsync(
        ServiceProvider provider,
        string dataRoot)
    {
        var modelPath = Path.Combine(dataRoot, "r33-driver-model.gguf");
        await File.WriteAllTextAsync(modelPath, "not a GGUF model");
        var source = new ServerConfig
        {
            Id = "r33-driver-lab-server",
            Name = "R33 driver Lab source",
            ExecutablePath = Environment.ProcessPath ?? throw new InvalidOperationException("The driver process path is unavailable."),
            ModelPath = modelPath,
            ContextSize = 256,
            GpuLayers = 0,
            GpuPlacement = GpuPlacementIntent.Cpu(),
            Threads = 1,
            PromptThreads = 1,
            Slots = 1
        };
        var baseline = LabConfigurationMapper.FromServer(source, "baseline", "Baseline");
        var candidate = baseline with { Id = "candidate", Label = "Candidate", ContextSize = 512 };
        var experiments = provider.GetRequiredService<ILabExperimentService>();
        var definition = await experiments.CreateDefinitionAsync(
            "R33 driver Lab failure cleanup",
            "r33-driver-lab",
            source,
            baseline,
            [candidate],
            repetitions: 1,
            LabCorrectnessRequirement.SpeedOnly);
        var run = await experiments.StartAsync(definition, source);
        if (run.Status != LabRunStatus.Failed
            || string.IsNullOrWhiteSpace(run.CompletionEvidenceId)
            || experiments.GetRun(run.Id)?.Status != LabRunStatus.Failed)
            throw new InvalidOperationException("Lab failure cleanup or terminal evidence was not retained.");

        return new
        {
            status = run.Status.ToString(),
            completion_evidence_id = run.CompletionEvidenceId,
            temporary_port = run.TemporaryPort
        };
    }

    private static string RequiredPath(string[] args, string name, bool file)
    {
        var matches = args
            .Select((value, index) => (value, index))
            .Where(item => string.Equals(item.value, name, StringComparison.Ordinal))
            .Select(item => item.index)
            .ToArray();
        if (matches.Length != 1 || matches[0] + 1 >= args.Length || args[matches[0] + 1].StartsWith("--", StringComparison.Ordinal))
            throw new InvalidOperationException($"{name} is required exactly once.");

        var path = Path.GetFullPath(args[matches[0] + 1]);
        if (file && Directory.Exists(path))
            throw new InvalidOperationException($"{name} must be a file path.");
        return path;
    }

    private static void ValidateArguments(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] is "--settings-path" or "--data-root" or "--workspace")
            {
                index++;
                continue;
            }

            throw new InvalidOperationException($"Unknown argument '{args[index]}'.");
        }
    }

    private static void ValidateIsolation(string settingsPath, string dataRoot, string workspace)
    {
        if (string.Equals(dataRoot, workspace, StringComparison.OrdinalIgnoreCase)
            || IsWithin(dataRoot, workspace)
            || IsWithin(workspace, dataRoot)
            || IsWithin(workspace, settingsPath))
            throw new InvalidOperationException("The driver requires distinct data-root and workspace paths, with settings outside the workspace.");
    }

    private static bool IsWithin(string parent, string child)
    {
        var normalizedParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedChild = Path.GetFullPath(child)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedChild.StartsWith(
            normalizedParent,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private sealed class ScriptedLlm : ILlmService
    {
        private int _calls;

        public string ProviderName => "R33 driver";
        public bool IsConfigured => true;

        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<LlmModel>
            {
                new()
                {
                    Id = "r33-driver-model",
                    Name = "R33 driver model",
                    Provider = "R33 driver",
                    ProviderTag = "driver",
                    IsVisible = true,
                    SupportsOutputConstraints = false
                }
            });

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var response = Interlocked.Increment(ref _calls) == 1
                ? """
                  {
                    "thought_summary": "Prepare the requested marker edit.",
                    "current_step": "Awaiting approval for the marker edit.",
                    "next_action": {
                      "type": "tool",
                      "tool_name": "edit_file",
                      "arguments": {
                        "relative_path": "r33-driver.md",
                        "old_string": "before",
                        "new_string": "after"
                      },
                      "requires_approval": true,
                      "risk_level": "high"
                    },
                    "state_update": {
                      "completed": [],
                      "pending": ["Approve the marker edit."],
                      "new_facts": [],
                      "blockers": []
                    },
                    "user_message": "Review the prepared marker edit.",
                    "reservations": []
                  }
                  """
                : """
                  {
                    "thought_summary": "The approved marker edit is complete.",
                    "current_step": "Finished the driver check.",
                    "next_action": {
                      "type": "final",
                      "tool_name": null,
                      "arguments": {},
                      "requires_approval": false,
                      "risk_level": "none"
                    },
                    "state_update": {
                      "completed": ["Change the marker."],
                      "pending": [],
                      "new_facts": ["The marker now reads after."],
                      "blockers": []
                    },
                    "user_message": "The prepared marker edit was applied and verified.",
                    "reservations": []
                  }
                  """;

            yield return new LlmStreamEvent(response);
            await Task.CompletedTask;
        }
    }
}
