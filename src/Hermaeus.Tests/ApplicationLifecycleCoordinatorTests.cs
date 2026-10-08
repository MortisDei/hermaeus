using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ApplicationLifecycleCoordinatorTests
{
    [Fact]
    public async Task Partial_startup_is_retryable_and_ready_startup_is_cached()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var attempts = 0;
        var participant = new ApplicationLifecycleParticipant("flaky", _ =>
        {
            attempts++;
            if (attempts == 1)
                throw new InvalidOperationException("first attempt failed");
            return Task.CompletedTask;
        });
        var coordinator = BuildCoordinator(settings, [participant]);

        var first = await coordinator.StartAsync();
        var second = await coordinator.StartAsync();
        var third = await coordinator.StartAsync();

        Assert.False(first.Ready);
        Assert.True(first.IsPartial);
        Assert.True(second.Ready);
        Assert.Same(second, third);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Shutdown_deadline_returns_without_waiting_for_an_owner_that_ignores_cancellation()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var coordinator = BuildCoordinator(settings, []);
        var ownerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.RegisterShutdownOwner("stuck owner", _ =>
        {
            ownerStarted.TrySetResult();
            return ownerCompletion.Task;
        });

        var started = DateTime.UtcNow;
        var shutdown = await coordinator.ShutdownAsync(TimeSpan.FromMilliseconds(75));
        var elapsed = DateTime.UtcNow - started;
        ownerCompletion.TrySetResult();

        Assert.True(ownerStarted.Task.IsCompleted);
        Assert.True(shutdown.TimedOut);
        Assert.False(shutdown.Clean);
        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"shutdown took {elapsed.TotalMilliseconds:N0} ms");
        Assert.Contains(shutdown.Phases, phase =>
            phase.Name == "stuck owner"
            && !phase.Succeeded
            && phase.Error.Contains("deadline", StringComparison.OrdinalIgnoreCase));
    }

    private static ApplicationLifecycleCoordinator BuildCoordinator(
        ISettingsService settings,
        IReadOnlyList<IApplicationLifecycleParticipant> participants)
    {
        var store = new EmptyRecommendationStore();
        var recommendation = new RecommendationApplicationService(store, settings);
        return new ApplicationLifecycleCoordinator(
            participants,
            recommendation,
            new EmptyLabRuntimeHost(),
            new AppLifecycleJournalService(settings),
            new RuntimeLogService(settings, new RedactionService()));
    }

    private sealed class EmptyRecommendationStore : IRecommendationStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<ConfigurationRecommendation> AddOrGetAsync(ConfigurationRecommendation recommendation, CancellationToken ct = default) =>
            Task.FromResult(recommendation);

        public Task<ConfigurationRecommendation?> GetAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<ConfigurationRecommendation?>(null);

        public Task<IReadOnlyList<ConfigurationRecommendation>> QueryAsync(RecommendationQuery query, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConfigurationRecommendation>>([]);

        public Task SetStatusAsync(string recommendationId, RecommendationStatus status, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<RecommendationDecisionRecord> AddDecisionAsync(RecommendationDecisionRecord decision, CancellationToken ct = default) =>
            Task.FromResult(decision);

        public Task<RecommendationRollbackRecord> AddRollbackAsync(RecommendationRollbackRecord rollback, CancellationToken ct = default) =>
            Task.FromResult(rollback);

        public Task ConsumeRollbackAsync(string rollbackId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<RecommendationDecisionRecord>> QueryDecisionsAsync(string? recommendationId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RecommendationDecisionRecord>>([]);

        public Task<IReadOnlyList<RecommendationRollbackRecord>> QueryRollbacksAsync(string? recommendationId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RecommendationRollbackRecord>>([]);
    }

    private sealed class EmptyLabRuntimeHost : ILabRuntimeHost
    {
        public Task<ILabRuntimeSession> StartAsync(
            string runId,
            ServerConfig source,
            LabConfiguration configuration,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> RecoverOwnedProcessesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
