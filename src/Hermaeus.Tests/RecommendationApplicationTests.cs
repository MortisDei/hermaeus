using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class RecommendationApplicationTests
{
    [Fact]
    public async Task Apply_and_undo_are_stale_guarded_settings_transactions()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var target = settings.Settings.ManagedServers[0];
        target.Id = "chat-server";
        var proposedSettings = settings.Settings.Clone();
        proposedSettings.ManagedServers[0].ContextSize = 8192;
        var store = new SqliteRecommendationStore(settings, new RedactionService());
        var recommendation = await CreateRecommendationAsync(settings, target, proposedSettings.ManagedServers[0], store);
        var application = new RecommendationApplicationService(store, settings);

        var applied = await application.ApplyAsync(recommendation.Id);

        Assert.True(applied.Succeeded);
        Assert.Equal("applied", applied.ResultCode);
        Assert.Equal(8192, settings.Settings.ManagedServers[0].ContextSize);
        Assert.Equal(RecommendationStatus.Accepted, (await store.GetAsync(recommendation.Id))!.Status);
        Assert.False((await store.QueryRollbacksAsync(recommendation.Id))[0].Consumed);

        var undone = await application.UndoAsync(recommendation.Id);

        Assert.True(undone.Succeeded);
        Assert.Equal("undone", undone.ResultCode);
        Assert.Equal(4096, settings.Settings.ManagedServers[0].ContextSize);
        Assert.Equal(RecommendationStatus.Current, (await store.GetAsync(recommendation.Id))!.Status);
        Assert.All(await store.QueryRollbacksAsync(recommendation.Id), rollback => Assert.True(rollback.Consumed));
    }

    [Fact]
    public async Task Apply_refuses_a_changed_target_without_writing_settings()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var target = settings.Settings.ManagedServers[0];
        target.Id = "chat-server";
        var proposed = settings.Settings.Clone().ManagedServers[0];
        proposed.Id = target.Id;
        proposed.ContextSize = 8192;
        var recommendation = await CreateRecommendationAsync(settings, target, proposed);
        var store = new SqliteRecommendationStore(settings, new RedactionService());
        var application = new RecommendationApplicationService(store, settings);
        target.Threads = 7;

        var result = await application.ApplyAsync(recommendation.Id);

        Assert.False(result.Succeeded);
        Assert.Equal("stale-refused", result.ResultCode);
        Assert.Equal(4096, target.ContextSize);
        Assert.Equal(RecommendationStatus.Superseded, (await store.GetAsync(recommendation.Id))!.Status);
    }

    [Fact]
    public async Task Reconcile_observes_a_persisted_apply_without_reapplying_it()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var target = settings.Settings.ManagedServers[0];
        target.Id = "chat-server";
        var proposed = settings.Settings.Clone().ManagedServers[0];
        proposed.Id = target.Id;
        proposed.ContextSize = 8192;
        var store = new SqliteRecommendationStore(settings, new RedactionService());
        var recommendation = await CreateRecommendationAsync(settings, target, proposed, store);
        var preImage = ManagedServerRecommendationPatch.Create(target.Id, proposed, target);
        var candidateSettings = settings.Settings.Clone();
        candidateSettings.ManagedServers[0].ContextSize = 8192;
        var postIdentity = ConfigurationIdentityFactory.Create(candidateSettings.ManagedServers[0]).StableId;
        await store.AddRollbackAsync(new RecommendationRollbackRecord(
            "rollback-pending", recommendation.Id, preImage.CanonicalJson, preImage.Sha256,
            postIdentity, DateTime.UtcNow, false));
        await store.AddDecisionAsync(new RecommendationDecisionRecord(
            "decision-pending", recommendation.Id, RecommendationDecisionKind.Apply, "owner",
            recommendation.CurrentConfigurationIdentity, "pending", DateTime.UtcNow));
        await settings.SaveAsync(candidateSettings);
        var application = new RecommendationApplicationService(store, settings);

        var reconciled = await application.ReconcileAsync();

        Assert.Equal(1, reconciled);
        Assert.Equal(8192, settings.Settings.ManagedServers[0].ContextSize);
        Assert.Equal(RecommendationStatus.Accepted, (await store.GetAsync(recommendation.Id))!.Status);
        Assert.Contains(await store.QueryDecisionsAsync(recommendation.Id),
            decision => decision.ResultCode == "reconciled-applied");
    }

    private static async Task<ConfigurationRecommendation> CreateRecommendationAsync(
        SettingsService settings, ServerConfig current, ServerConfig proposed, SqliteRecommendationStore? existingStore = null)
    {
        var store = existingStore ?? new SqliteRecommendationStore(settings, new RedactionService());
        var patch = ManagedServerRecommendationPatch.Create(current.Id, current, proposed);
        var recommendation = await new RecommendationDerivationService(store, new RecommendationRuleRegistry()).DeriveAsync(
            new RecommendationProposal(
                RecommendationKind.RuntimeConfiguration,
                current.Id,
                ConfigurationIdentityFactory.Create(current).StableId,
                patch,
                [new RecommendationEvidenceReference("launch", "adaptive", true, CapabilityState.Available, DateTime.UtcNow)],
                [],
                [],
                "compatible-proven-launch",
                1,
                "compatible-success",
                DateTime.UtcNow,
                true,
                true,
                false,
                false,
                false,
                true,
                true));
        return recommendation;
    }

    private static SettingsService NewSettings(TempDir temp)
    {
        var settings = new SettingsService(temp.PathFor("settings.json"));
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        return settings;
    }
}
