using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class ModelAutoTuneLifecycleTests
{
    [Fact]
    public async Task AutoTuneModel_suspends_a_loaded_source_and_builds_an_isolated_target_probe()
    {
        using var temp = new TempDir();
        var fixture = CreateFixture(temp);

        await fixture.ViewModel.AutoTuneModelCommand.ExecuteAsync(fixture.Item);

        Assert.Equal(["suspend", "tune", "restore"], fixture.Events);
        Assert.Equal([fixture.Source.Id], fixture.Services.RestoredIds);
        Assert.NotNull(fixture.Tuning.Probe);
        Assert.Equal(fixture.TargetPath, fixture.Tuning.Probe!.ModelPath);
        Assert.Equal(fixture.TargetDraftPath, fixture.Tuning.Probe.Speculative.DraftModelPath);
        Assert.Equal(["draft-mtp"], fixture.Tuning.Probe.Speculative.Types);
        Assert.Empty(fixture.Tuning.Probe.ExtraArgs);
        Assert.Empty(fixture.Tuning.Probe.MmprojPath);
        Assert.False(fixture.Tuning.Probe.UseProjector);
        Assert.Equal(fixture.SourceModelPath, fixture.Services.ChatServer!.ModelPath);

        var profile = Assert.Single(fixture.Settings.Settings.LlamaTuneProfiles);
        Assert.Equal(fixture.TargetPath, profile.ModelPath);
        Assert.Equal(8192, profile.ContextSize);
    }

    [Fact]
    public async Task AutoTuneModel_restores_a_loaded_source_when_the_real_tune_operation_fails()
    {
        using var temp = new TempDir();
        var fixture = CreateFixture(temp);
        fixture.Tuning.Handler = (_, _) => Task.FromException<ServerTuneResult>(
            new InvalidOperationException("candidate failed"));

        await fixture.ViewModel.AutoTuneModelCommand.ExecuteAsync(fixture.Item);

        Assert.Equal(["suspend", "tune", "restore"], fixture.Events);
        Assert.Equal([fixture.Source.Id], fixture.Services.RestoredIds);
        Assert.Empty(fixture.Settings.Settings.LlamaTuneProfiles);
    }

    [Fact]
    public async Task AutoTuneModel_restores_a_loaded_source_when_cancelled_during_tuning()
    {
        using var temp = new TempDir();
        var fixture = CreateFixture(temp);
        fixture.Tuning.Handler = async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new ServerTuneResult(24, 32, 6, "test", "cancelled", TunedContextSize: 8192);
        };

        var tuneTask = fixture.ViewModel.AutoTuneModelCommand.ExecuteAsync(fixture.Item);
        await fixture.Tuning.Started.Task;
        fixture.ViewModel.CancelAutoTuneModelCommand.Execute(fixture.Item);
        await tuneTask;

        Assert.Equal(["suspend", "tune", "restore"], fixture.Events);
        Assert.Equal([fixture.Source.Id], fixture.Services.RestoredIds);
        Assert.Empty(fixture.Settings.Settings.LlamaTuneProfiles);
    }

    private static AutoTuneFixture CreateFixture(TempDir temp)
    {
        var settings = NewSettings(temp);
        var sourceModelPath = temp.PathFor("source.gguf");
        var sourceDraftPath = temp.PathFor("source-draft.gguf");
        var sourceProjectorPath = temp.PathFor("source-projector.gguf");
        var targetPath = temp.PathFor("target.gguf");
        var targetDraftPath = temp.PathFor("target-draft.gguf");
        File.WriteAllText(sourceModelPath, "source");
        File.WriteAllText(sourceDraftPath, "source draft");
        File.WriteAllText(sourceProjectorPath, "source projector");
        File.WriteAllText(targetPath, "target");
        File.WriteAllText(targetDraftPath, "target draft");

        var sourceConfig = settings.Settings.ManagedServers.Single(server => !server.EmbeddingsMode);
        sourceConfig.ExecutablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The test process has no executable path.");
        sourceConfig.ModelPath = sourceModelPath;
        sourceConfig.ExtraArgs = "--source-only";
        sourceConfig.MmprojPath = sourceProjectorPath;
        sourceConfig.Speculative = new SpeculativeDecodingConfig
        {
            Types = ["draft-mtp"],
            DraftModelPath = sourceDraftPath
        };

        var events = new List<string>();
        var services = new RecordingServicesViewModel(settings, events);
        var source = services.Servers.Single(server => !server.EmbeddingsMode);
        source.Status = ServerStatus.Running;

        var tuning = new RecordingTuningService(events);
        var item = new ModelProfileItemViewModel(
            new LlmModel { Id = targetPath, Name = "Target model", Provider = "local GGUF" },
            new ModelProfile { ModelId = targetPath });
        item.ApplyCatalogClassification(
            ModelCatalogRole.ChatGeneration,
            null,
            new ModelManifestEntry
            {
                FilePath = targetPath,
                Companions =
                [
                    new ModelCompanionManifestEntry
                    {
                        LocalFilePath = targetDraftPath,
                        Role = "draft_head",
                        SizeBytes = new FileInfo(targetDraftPath).Length
                    }
                ]
            });

        var vm = new ModelManagementViewModel(
            new ScriptedModelsLlm(() => []),
            new ModelProfileService(settings),
            new FakeToasts(),
            settings,
            new FakeSystemInfo(),
            services,
            new ModelManifestStore(settings),
            new HuggingFaceClient(),
            new ModelDownloadService(),
            runtimeTuning: tuning);

        return new AutoTuneFixture(
            settings,
            services,
            tuning,
            vm,
            item,
            source,
            events,
            sourceModelPath,
            targetPath,
            targetDraftPath);
    }

    private sealed record AutoTuneFixture(
        SettingsService Settings,
        RecordingServicesViewModel Services,
        RecordingTuningService Tuning,
        ModelManagementViewModel ViewModel,
        ModelProfileItemViewModel Item,
        ServerProcessViewModel Source,
        List<string> Events,
        string SourceModelPath,
        string TargetPath,
        string TargetDraftPath);

    private sealed class RecordingTuningService(List<string> events) : IManagedRuntimeTuningService
    {
        public ServerConfig? Probe { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<ServerConfig, CancellationToken, Task<ServerTuneResult>> Handler { get; set; } =
            (_, _) => Task.FromResult(new ServerTuneResult(24, 32, 6, "test", "verified", TunedContextSize: 8192));

        public async Task<ServerTuneResult> RunAsync(
            ServerConfig config,
            IProgress<string>? progress = null,
            CancellationToken ct = default,
            GgufModelInfo? ggufInfo = null,
            HardwareProfile? hardware = null)
        {
            Probe = config;
            events.Add("tune");
            Started.TrySetResult();
            return await Handler(config, ct);
        }
    }

    private sealed class RecordingServicesViewModel : ServicesViewModel
    {
        private readonly List<string> _events;

        public IReadOnlyList<string> RestoredIds { get; private set; } = [];

        public RecordingServicesViewModel(SettingsService settings, List<string> events)
            : base(
                settings,
                new RuntimeProfileService(settings),
                new FakeToasts(),
                new RedactionService(),
                new TrustService(),
                new RuntimeLogService(settings),
                NewTtsSettingsViewModel(settings))
        {
            _events = events;
        }

        public override Task<IReadOnlyList<string>> SuspendRunningServersAsync(IEnumerable<string> serverIds)
        {
            _events.Add("suspend");
            var requested = serverIds.ToHashSet(StringComparer.Ordinal);
            var suspended = Servers
                .Where(server => requested.Contains(server.Id) && server.IsRunning)
                .Select(server => server.Id)
                .ToArray();
            return Task.FromResult<IReadOnlyList<string>>(suspended);
        }

        public override Task RestartServersAsync(IReadOnlyList<string> serverIds)
        {
            _events.Add("restore");
            RestoredIds = serverIds.ToArray();
            return Task.CompletedTask;
        }
    }
}
