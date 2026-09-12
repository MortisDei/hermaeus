using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Composition;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Rag.Models;
using Hermaeus.Rag.Pipeline;
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
            // only settings and external model, embedding, voice, and Lab
            // timing boundaries with explicit scratch instances. No owner
            // settings or live provider are touched.
            services.AddSingleton<ISettingsService>(settings);
            services.AddSingleton<ScriptedLlm>();
            services.AddSingleton<ILlmService>(sp => sp.GetRequiredService<ScriptedLlm>());
            services.AddSingleton<IEmbeddingService, DriverEmbeddingService>();
            services.AddSingleton<DriverVoiceProviderRegistry>();
            services.AddSingleton<IVoiceProviderRegistry>(sp =>
                sp.GetRequiredService<DriverVoiceProviderRegistry>());

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
                var chat = await RunChatContextAsync(provider, rag.context);
                var voice = await RunVoiceBoundaryAsync(provider, settings);
                var lab = await RunLabFailureLifecycleAsync(provider, dataRoot);
                var labApply = await RunLabApplyReconcileAndReopenAsync(provider, settings, settingsPath, dataRoot);

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
                    chat,
                    voice,
                    lab,
                    lab_apply = labApply
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

    private static async Task<RagDriverEvidence> RunRagGenerationAndRetrievalAsync(
        ServiceProvider provider,
        string sourcePath)
    {
        var store = provider.GetRequiredService<SqliteRagStore>();
        var query = provider.GetRequiredService<RagQueryService>();
        var pipeline = provider.GetRequiredService<RagPipeline>();
        var workspace = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException("The driver source has no workspace directory.");
        var knowledgePath = Path.Combine(workspace, "r33-driver-knowledge.md");
        await File.WriteAllTextAsync(
            knowledgePath,
            "# R33 driver knowledge\n\nThe shared lifecycle context is retained in the published generation.");
        var dataset = new RagDataset
        {
            Id = "r33-driver-dataset",
            Name = "R33 driver dataset",
            Description = "Scratch-only generation and retrieval evidence.",
            LastIngestPath = workspace,
            Config = new RagDatasetConfig
            {
                EmbeddingModel = "r33-driver-embedding",
                EmbeddingDimensions = 0
            }
        };

        var report = await pipeline.IngestDirectoryAsync(
            dataset,
            workspace,
            explicitFiles: [knowledgePath]);
        var stored = await store.GetStoredChunksAsync(dataset.Id, includeEmbeddings: true);
        dataset.ChunkCount = stored.Count(chunk => !chunk.IsParent);
        dataset.LastIngestUtc = DateTime.UtcNow;
        await store.SaveDatasetAsync(dataset);

        var history = await store.GetGenerationHistoryAsync(dataset.Id);
        var retrieved = await query.RetrieveAsync(dataset.Id, "shared lifecycle evidence", new RagQueryOptions(TopK: 1));
        if (report.Added != 1
            || history.Count != 1
            || retrieved.Selected.Count != 1
            || !retrieved.Selected[0].Chunk.Content.Contains("shared lifecycle context", StringComparison.Ordinal))
            throw new InvalidOperationException("RAG generation publication or retrieval evidence failed.");

        return new RagDriverEvidence(
            history[0].GenerationId,
            history.Count,
            retrieved.Selected[0].Chunk.Id,
            retrieved.PlannerNotes,
            retrieved.Selected[0].Chunk.Content);
    }

    private static async Task<object> RunChatContextAsync(ServiceProvider provider, string context)
    {
        var scripted = provider.GetRequiredService<ScriptedLlm>();
        var response = new StringBuilder();
        var result = await ChatSendOrchestrator.StreamAsync(
            scripted,
            "r33-driver-model",
            [
                new ChatMessage("system", "Answer from the supplied R33 retrieval context."),
                new ChatMessage("user", $"Context:\n{context}\n\nQuestion: what was retained?")
            ],
            new LlmChatOptions { Temperature = 0 },
            delta => response.Append(delta),
            _ => { },
            CancellationToken.None);
        if (result.Cancelled || !string.IsNullOrWhiteSpace(result.Error)
            || !scripted.SawRetrievalContext || response.Length == 0)
            throw new InvalidOperationException("The scripted Chat context workflow did not complete.");

        return new
        {
            cancelled = result.Cancelled,
            response_length = response.Length,
            retrieval_context_seen = scripted.SawRetrievalContext
        };
    }

    private static async Task<object> RunVoiceBoundaryAsync(
        ServiceProvider provider,
        SettingsService settings)
    {
        settings.Settings.Tts.Enabled = true;
        var voice = provider.GetRequiredService<IVoiceOrchestrator>();
        var completed = new TaskCompletionSource<VoiceChannel>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        voice.UtteranceCompleted += channel => completed.TrySetResult(channel);
        await voice.EnqueueAsync(new VoiceUtterance(
            "R33 voice boundary",
            VoiceChannel.Chat,
            VoicePriority.Normal,
            DedupeKey: "r33-driver-voice"));
        var channel = await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var providerInstance = provider.GetRequiredService<DriverVoiceProviderRegistry>().Provider;
        if (channel != VoiceChannel.Chat || providerInstance.SynthesisCount != 1)
            throw new InvalidOperationException("The voice orchestration boundary did not complete through the provider.");

        voice.StopAll();
        return new
        {
            channel = channel.ToString(),
            synthesis_count = providerInstance.SynthesisCount,
            completed = true
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

    private static async Task<object> RunLabApplyReconcileAndReopenAsync(
        ServiceProvider provider,
        SettingsService settings,
        string settingsPath,
        string dataRoot)
    {
        var modelPath = Path.Combine(dataRoot, "r33-driver-apply-model.gguf");
        await File.WriteAllTextAsync(modelPath, "not a GGUF model");
        var source = new ServerConfig
        {
            Id = "r33-driver-lab-apply-server",
            Name = "R33 driver Lab apply source",
            ExecutablePath = Environment.ProcessPath ?? throw new InvalidOperationException("The driver process path is unavailable."),
            ModelPath = modelPath,
            ContextSize = 256,
            GpuLayers = 0,
            GpuPlacement = GpuPlacementIntent.Cpu(),
            Threads = 1,
            PromptThreads = 1,
            Slots = 1
        };
        settings.Settings.ManagedServers = [source];
        await settings.SaveAsync();

        var baseline = LabConfigurationMapper.FromServer(source, "baseline", "Baseline");
        var candidate = baseline with { Id = "candidate", Label = "Candidate", ContextSize = 512 };
        var fakeHost = new DriverLabRuntimeHost();
        await using var experiments = new LabExperimentService(
            settings,
            provider.GetRequiredService<ISystemInfoService>(),
            provider.GetRequiredService<IEmpiricalExperienceStore>(),
            fakeHost,
            provider.GetService<ModelManifestStore>());
        var definition = await experiments.CreateDefinitionAsync(
            "R33 driver Lab apply",
            "r33-driver-lab-apply",
            source,
            baseline,
            [candidate],
            repetitions: 1,
            LabCorrectnessRequirement.ExactEquivalence);
        var run = await experiments.StartAsync(definition, source);
        if (run.Status != LabRunStatus.Running)
            throw new InvalidOperationException("The deterministic Lab runtime did not enter Running.");
        await experiments.SwitchConfigurationAsync(run.Id, source, candidate.Id);

        var observations = new[]
        {
            LabObservation(run, "baseline", "runtime.ready", 1, "bool"),
            LabObservation(run, "baseline", "process.ram.current", 1, "bytes"),
            LabObservation(run, "candidate", "runtime.ready", 1, "bool"),
            LabObservation(run, "candidate", "process.ram.current", 1, "bytes")
        };
        var outputs = new[]
        {
            LabCorrectnessEvaluator.Capture("baseline", "r33-driver-case", 0, "same output"),
            LabCorrectnessEvaluator.Capture("candidate", "r33-driver-case", 0, "same output")
        };
        var completed = await experiments.CompleteAsync(run.Id, observations, outputs);
        var review = experiments.CreateApplyReview(run.Id, "candidate");
        if (completed.Status != LabRunStatus.Succeeded || !review.CanApply)
            throw new InvalidOperationException($"The Lab Apply review was not earned: {review.RefusalReason}");

        await experiments.ApplyAsync(review);
        var applied = settings.Settings.ManagedServers.Single(server => server.Id == source.Id);
        if (applied.ContextSize != candidate.ContextSize || fakeHost.Session.StopCount != 1)
            throw new InvalidOperationException("Lab Apply did not persist the reviewed candidate or restore the owned runtime.");

        var reopened = new SettingsService(settingsPath);
        await reopened.LoadAsync();
        var reopenedServer = reopened.Settings.ManagedServers.Single(server => server.Id == source.Id);
        if (reopenedServer.ContextSize != candidate.ContextSize)
            throw new InvalidOperationException("The Lab-applied Services configuration did not survive reopen.");

        return new
        {
            status = completed.Status.ToString(),
            review_id = review.ReviewId,
            applied_context = applied.ContextSize,
            reopened_context = reopenedServer.ContextSize,
            runtime_stop_count = fakeHost.Session.StopCount,
            evidence_id = completed.CompletionEvidenceId
        };
    }

    private static LabObservation LabObservation(
        LabRunSnapshot run,
        string configurationId,
        string metricId,
        double value,
        string unit) => new()
        {
            RunId = run.Id,
            ConfigurationId = configurationId,
            CaseId = "r33-driver-case",
            Repetition = 0,
            MetricId = metricId,
            Value = value,
            Unit = unit,
            Source = "r33-driver-runtime",
            Trust = "DeterministicDriver",
            RuntimeFingerprint = run.Definition.ProfileFingerprint.Runtime.StableId,
            ModelFingerprint = run.Definition.ProfileFingerprint.Model.StableId,
            HardwareFingerprint = run.Definition.ProfileFingerprint.Hardware.StableId,
            ConfigurationFingerprint = run.Definition.ConfigurationFingerprints[configurationId]
        };

    private static EffectiveLaunchObservation EffectiveLaunch(LabConfiguration configuration)
    {
        var placement = configuration.GpuPlacement;
        if (placement is null)
            GpuPlacementIntent.TryFromLegacy(configuration.GpuLayers, out placement, out _);
        var gpuLayers = placement?.Kind switch
        {
            GpuPlacementKind.All => "-1",
            GpuPlacementKind.Exact => placement.ExactLayerCount!.Value.ToString(CultureInfo.InvariantCulture),
            _ => "0"
        };
        var fit = placement?.Kind == GpuPlacementKind.Auto ? "true" : "false";
        return new EffectiveLaunchObservation(
            RuntimeIdentityFactory.Unknown("r33-driver"), EffectiveLaunchObservationParser.ParserVersion, true,
            placement?.Kind == GpuPlacementKind.Auto, null, null,
            [
                new AdaptiveFieldObservation("context", configuration.ContextSize.ToString(CultureInfo.InvariantCulture), null,
                    configuration.ContextSize.ToString(CultureInfo.InvariantCulture), configuration.ContextSize.ToString(CultureInfo.InvariantCulture),
                    AdaptiveEvidenceState.Proven, "r33-driver.props.context"),
                new AdaptiveFieldObservation("gpu_layers", placement?.CanonicalValue, gpuLayers, gpuLayers, gpuLayers,
                    AdaptiveEvidenceState.Proven, "r33-driver.props.gpu_layers"),
                new AdaptiveFieldObservation("fit", fit, fit, fit, fit, AdaptiveEvidenceState.Proven, "r33-driver.props.fit"),
                new AdaptiveFieldObservation("slots", Math.Max(1, configuration.Slots).ToString(CultureInfo.InvariantCulture),
                    Math.Max(1, configuration.Slots).ToString(CultureInfo.InvariantCulture),
                    Math.Max(1, configuration.Slots).ToString(CultureInfo.InvariantCulture),
                    Math.Max(1, configuration.Slots).ToString(CultureInfo.InvariantCulture),
                    AdaptiveEvidenceState.Proven, "r33-driver.props.slots")
            ],
            ["r33-driver.props"], true)
        {
            Process = new RuntimeLaunchProcessEvidence(
                Environment.ProcessId,
                DateTime.UtcNow,
                Environment.ProcessPath ?? "r33-driver",
                ["--ctx-size", configuration.ContextSize.ToString(CultureInfo.InvariantCulture), "--n-gpu-layers", gpuLayers])
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

    private sealed record RagDriverEvidence(
        string generation_id,
        int generation_count,
        string selected_chunk,
        string planner_notes,
        string context);

    private sealed class DriverEmbeddingService : IEmbeddingService
    {
        public int Dimensions => 1;

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new[] { 1f });
        }

        public Task<List<float[]>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(texts.Select(_ => new[] { 1f }).ToList());
        }
    }

    private sealed class DriverLabRuntimeHost : ILabRuntimeHost
    {
        public DriverLabRuntimeSession Session { get; private set; } = new();

        public Task<ILabRuntimeSession> StartAsync(
            string runId,
            ServerConfig source,
            LabConfiguration configuration,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Session = new DriverLabRuntimeSession();
            Session.EffectiveLaunch = R33Driver.EffectiveLaunch(configuration);
            return Task.FromResult<ILabRuntimeSession>(Session);
        }

        public Task<IReadOnlyList<string>> RecoverOwnedProcessesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class DriverLabRuntimeSession : ILabRuntimeSession
    {
        private int _stopCount;

        public string OwnershipId { get; } = $"r33-driver-{Guid.NewGuid():N}";
        public int Port => 49_152;
        public bool IsRunning => Volatile.Read(ref _stopCount) == 0;
        public ManagedProcessReference Process => new(Environment.ProcessId, DateTime.UtcNow);
        public EffectiveLaunchObservation? EffectiveLaunch { get; set; }
        public int StopCount => Volatile.Read(ref _stopCount);

        public Task StopAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _stopCount);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DriverVoiceProviderRegistry : IVoiceProviderRegistry
    {
        public DriverVoiceProvider Provider { get; } = new();
        private VoiceProvider _active = VoiceProvider.KokoroNative;

        public IReadOnlyList<VoiceProviderInfo> GetAvailableProviders() =>
        [
            new VoiceProviderInfo(
                VoiceProvider.KokoroNative,
                "R33 driver voice",
                "Deterministic driver provider.",
                VoiceProviderCategory.Recommended,
                true,
                VoiceCapability.TextToSpeech | VoiceCapability.Local)
        ];

        public VoiceProvider GetActiveProvider() => _active;

        public IVoiceProvider GetActiveVoiceProvider() => Provider;

        public IVoiceProvider GetVoiceProvider(VoiceProvider provider) => provider == Provider.Id
            ? Provider
            : throw new ArgumentException($"Unknown driver voice provider: {provider}");

        public Task SetActiveProviderAsync(VoiceProvider provider)
        {
            if (provider != Provider.Id)
                throw new ArgumentException($"Unknown driver voice provider: {provider}");
            _active = provider;
            return Task.CompletedTask;
        }

        public VoiceProviderConfig GetProviderConfig(VoiceProvider provider) =>
            new(provider.ToString());

        public Task SetProviderConfigAsync(VoiceProvider provider, VoiceProviderConfig config) =>
            Task.CompletedTask;

        public ITtsService GetActiveTtsService() =>
            throw new NotSupportedException("The R33 driver exercises IVoiceOrchestrator, not the legacy TTS facade.");
    }

    private sealed class DriverVoiceProvider : IVoiceProvider
    {
        public VoiceProvider Id => VoiceProvider.KokoroNative;
        public string DisplayName => "R33 driver voice";
        public VoiceCapability Capabilities => VoiceCapability.TextToSpeech | VoiceCapability.Local;
        public (int Major, int Minor)? RequiredPythonVersion => null;
        public bool IsInstalled => true;
        public int SynthesisCount { get; private set; }

        public VoiceProviderDetection Detect() =>
            new(true, "Deterministic driver voice is available.", "No external voice process is used.");

        public VoiceInstallPlan InstallPlan() =>
            new("No installation is required.", [], "Driver-only provider.");

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<VoiceHealth> HealthCheckAsync(CancellationToken ct = default) =>
            Task.FromResult(new VoiceHealth(VoiceHealthStatus.Healthy, "Ready", "Driver provider."));

        public Task<IReadOnlyList<VoiceDefinition>> ListVoicesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VoiceDefinition>>([new("driver", "Driver voice")]);

        public Task<VoiceSynthesisResult> GenerateSpeechAsync(
            VoiceSynthesisRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            SynthesisCount++;
            return Task.FromResult(new VoiceSynthesisResult(true, "Driver voice captured."));
        }
    }

    private sealed class ScriptedLlm : ILlmService
    {
        private int _calls;
        private int _sawRetrievalContext;

        public string ProviderName => "R33 driver";
        public bool IsConfigured => true;
        public bool SawRetrievalContext => Volatile.Read(ref _sawRetrievalContext) != 0;

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
            if (messages.Any(message => message.Content.Contains(
                    "shared lifecycle context",
                    StringComparison.Ordinal)))
                Interlocked.Exchange(ref _sawRetrievalContext, 1);

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
