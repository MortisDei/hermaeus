using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class KnowledgeRevisionStoreTests
{
    [Fact]
    public async Task Create_records_lineage_sources_decision_and_current_projection()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var source = new SourceReference(ProvenanceKind.Workspace, "Owner note", "workspace-key", "evidence",
            EvidenceOrigin: EvidenceOrigin.UserProvided);
        var revision = await store.CreateAssertionAsync(new KnowledgeRevisionDraft(
            new Memory { Id = "assertion-1", Content = "old fact", Category = "facts" },
            TemporalOrigin: KnowledgeTemporalOrigin.UserProvided,
            SourceReferences: [source],
            Decision: new KnowledgeRevisionDecision("create", "owner", "accepted", DateTime.UtcNow)));

        Assert.Equal("assertion-1", revision.AssertionId);
        Assert.Equal("old fact", revision.Content);
        Assert.Equal(KnowledgeRevisionStatus.Current, revision.Status);
        Assert.Single(revision.SourceReferences);
        Assert.Equal("owner", revision.Decision!.Actor);
        var projected = (await store.GetByIdAsync("assertion-1"))!;
        Assert.Equal("old fact", projected.Content);
        Assert.Equal(revision.RevisionId, projected.RevisionId);
        Assert.Contains(revision.RevisionId, projected.ToContextSource().Locator);
    }

    [Fact]
    public async Task Revise_closes_previous_revision_and_requires_expected_current_id()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "a1", "first");

        var second = await store.ReviseAssertionAsync("a1", first.RevisionId,
            new KnowledgeRevisionDraft(new Memory { Id = "a1", Content = "second" },
                TemporalOrigin: KnowledgeTemporalOrigin.SourceEvidence));

        Assert.Equal(first.RevisionId, second.PreviousRevisionId);
        Assert.Equal("second", (await store.GetByIdAsync("a1"))!.Content);
        var history = await store.GetHistoryAsync("a1");
        Assert.Equal(2, history.Count);
        Assert.Contains(history, r => r.RevisionId == first.RevisionId && r.Status == KnowledgeRevisionStatus.Superseded);
        Assert.Contains(history, r => r.RevisionId == second.RevisionId && r.Status == KnowledgeRevisionStatus.Current);

        await Assert.ThrowsAsync<KnowledgeRevisionConflictException>(() => store.ReviseAssertionAsync(
            "a1", first.RevisionId, new KnowledgeRevisionDraft(new Memory { Id = "a1", Content = "stale" })));
        Assert.Equal("second", (await store.GetByIdAsync("a1"))!.Content);
    }

    [Fact]
    public async Task Correct_preserves_prior_evidence_without_fabricating_effective_time()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "a2", "incorrect");
        var corrected = await store.CorrectAssertionAsync("a2", first.RevisionId,
            new KnowledgeRevisionDraft(
                new Memory { Id = "a2", Content = "corrected" },
                TemporalOrigin: KnowledgeTemporalOrigin.UserProvided,
                Decision: new KnowledgeRevisionDecision("correction", "owner", "source was wrong", DateTime.UtcNow)));

        Assert.Null(corrected.EffectiveFromUtc);
        Assert.Null(corrected.EffectiveToUtc);
        Assert.Equal("correction", corrected.Decision!.Kind);
        Assert.Equal(first.RevisionId, corrected.PreviousRevisionId);
    }

    [Fact]
    public async Task Disputed_current_revision_is_hidden_from_normal_projection_but_explicitly_queryable()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "a3", "contested");
        await store.SetDisputeAsync("a3", first.RevisionId, true,
            new KnowledgeRevisionDecision("dispute", "owner", "needs evidence", DateTime.UtcNow));

        Assert.Empty(await store.GetAllAsync());
        Assert.Empty(await store.SearchAsync("contested"));
        Assert.Empty(await store.QueryAsync(new KnowledgeTimeQuery(KnowledgeTimeQueryMode.Current)));
        var disputed = Assert.Single(await store.QueryAsync(
            new KnowledgeTimeQuery(KnowledgeTimeQueryMode.Current, IncludeDisputed: true)));
        Assert.Equal(KnowledgeRevisionStatus.Disputed, disputed.Status);

        await store.SetDisputeAsync("a3", first.RevisionId, false,
            new KnowledgeRevisionDecision("clear-dispute", "owner", "evidence reviewed", DateTime.UtcNow));
        Assert.NotNull(await store.GetByIdAsync("a3"));
    }

    [Fact]
    public async Task Presentation_mutation_does_not_create_content_revision()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "a4", "stable fact");
        var updated = await store.MutatePresentationAsync("a4", first.RevisionId,
            new KnowledgePresentationMutation(
                "Pinned fact", MemoryScope.Project, "project-1", "facts", ["important"], 0.9,
                true, false, 2, null, null, [], [], false, null));

        Assert.Equal(first.RevisionId, updated.RevisionId);
        Assert.Single(await store.GetHistoryAsync("a4"));
        var memory = await store.GetByIdAsync("a4");
        Assert.Equal("stable fact", memory!.Content);
        Assert.Equal("Pinned fact", memory.Title);
        Assert.Equal(MemoryScope.Project, memory.Scope);
        Assert.True(memory.IsPinned);
    }

    [Fact]
    public async Task As_of_requires_established_effective_time_and_history_keeps_unknown_revision()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var unknown = await CreateAsync(store, "a5", "unknown time");
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var known = await store.ReviseAssertionAsync("a5", unknown.RevisionId,
            new KnowledgeRevisionDraft(new Memory { Id = "a5", Content = "known time" },
                EffectiveFromUtc: from,
                TemporalOrigin: KnowledgeTemporalOrigin.SourceEvidence));

        Assert.Empty(await store.QueryAsync(new KnowledgeTimeQuery(
            KnowledgeTimeQueryMode.AsOf, AsOfUtc: from.AddDays(-1))));
        var asOf = Assert.Single(await store.QueryAsync(new KnowledgeTimeQuery(
            KnowledgeTimeQueryMode.AsOf, AsOfUtc: from.AddDays(1))));
        Assert.Equal(known.RevisionId, asOf.RevisionId);
        Assert.DoesNotContain(await store.QueryAsync(new KnowledgeTimeQuery(KnowledgeTimeQueryMode.History)),
            r => r.RevisionId == unknown.RevisionId && r.Status == KnowledgeRevisionStatus.Disputed);
        Assert.Equal(2, (await store.GetHistoryAsync("a5")).Count);
    }

    [Fact]
    public async Task Restore_creates_new_successor_and_delete_removes_all_lineage_without_promotion()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "a6", "original");
        var second = await store.ReviseAssertionAsync("a6", first.RevisionId,
            new KnowledgeRevisionDraft(new Memory { Id = "a6", Content = "replacement" }));
        var restored = await store.RestoreRevisionAsync("a6", second.RevisionId, first.RevisionId,
            new KnowledgeRevisionDecision("restore", "owner", "restored after review", DateTime.UtcNow));

        Assert.Equal(second.RevisionId, restored.PreviousRevisionId);
        Assert.Equal("original", (await store.GetByIdAsync("a6"))!.Content);
        Assert.Equal(3, (await store.GetHistoryAsync("a6")).Count);

        await store.HardDeleteAsync("a6", restored.RevisionId);
        Assert.Null(await store.GetCurrentRevisionAsync("a6"));
        Assert.Empty(await store.QueryAsync(new KnowledgeTimeQuery(KnowledgeTimeQueryMode.History)));

        await using var connection = new SqliteConnection($"Data Source={DataPath(temp)}");
        await connection.OpenAsync();
        var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(1) FROM memories_fts WHERE id = 'a6'";
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Legacy_rows_are_lazily_assigned_one_current_revision()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.SaveAsync(new Memory { Id = "legacy-1", Content = "old row" });

        var history = await store.GetHistoryAsync("legacy-1");
        var revision = Assert.Single(history);
        Assert.Equal("legacy:legacy-1", revision.RevisionId);
        Assert.Equal(KnowledgeRevisionStatus.Current, revision.Status);
        Assert.Equal("old row", revision.Content);
    }

    [Fact]
    public async Task Create_rejects_a_blank_assertion_id()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);

        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAssertionAsync(
            new KnowledgeRevisionDraft(new Memory { Id = " ", Content = "fact" })));
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_assertion()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await CreateAsync(store, "duplicate", "first");

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAssertionAsync(
            new KnowledgeRevisionDraft(new Memory { Id = "duplicate", Content = "second" })));
    }

    [Fact]
    public async Task Create_uses_the_memory_source_when_draft_sources_are_omitted()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var source = new SourceReference(ProvenanceKind.Memory, "Conversation", "conversation-1",
            EvidenceOrigin: EvidenceOrigin.DirectObservation);

        var revision = await store.CreateAssertionAsync(new KnowledgeRevisionDraft(new Memory
        {
            Id = "source-fallback",
            Content = "fact",
            Source = source
        }));

        Assert.Equal(source, Assert.Single(revision.SourceReferences));
        Assert.Equal(source, (await store.GetByIdAsync("source-fallback"))!.Source);
    }

    [Fact]
    public async Task Create_bounds_decision_and_source_fields_before_persistence()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var revision = await store.CreateAssertionAsync(new KnowledgeRevisionDraft(
            new Memory { Id = "bounded", Content = "fact" },
            SourceReferences: [new SourceReference(ProvenanceKind.Memory, new string('t', 700),
                Locator: new string('l', 2500), Snippet: new string('s', 5000))],
            Decision: new KnowledgeRevisionDecision("create", "owner", new string('r', 3000), DateTime.UtcNow)));

        var source = Assert.Single(revision.SourceReferences);
        Assert.Equal(512, source.Title.Length);
        Assert.Equal(2048, source.Locator!.Length);
        Assert.Equal(4096, source.Snippet!.Length);
        Assert.Equal(2048, revision.Decision!.Reason.Length);
    }

    [Fact]
    public async Task Create_rejects_a_reversed_effective_interval()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var from = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAssertionAsync(new KnowledgeRevisionDraft(
            new Memory { Id = "bad-interval", Content = "fact" },
            EffectiveFromUtc: from,
            EffectiveToUtc: from.AddMinutes(-1))));
    }

    [Fact]
    public async Task Revise_rejects_a_draft_for_a_different_assertion()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "a7", "first");

        await Assert.ThrowsAsync<ArgumentException>(() => store.ReviseAssertionAsync(
            "a7", first.RevisionId, new KnowledgeRevisionDraft(new Memory { Id = "other", Content = "second" })));
    }

    [Fact]
    public async Task Revise_carries_forward_sources_when_the_new_draft_has_none()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var source = new SourceReference(ProvenanceKind.Workspace, "Workspace", "/workspace/item");
        var first = await store.CreateAssertionAsync(new KnowledgeRevisionDraft(
            new Memory { Id = "source-carry", Content = "first", Source = source }));

        var second = await store.ReviseAssertionAsync("source-carry", first.RevisionId,
            new KnowledgeRevisionDraft(new Memory { Id = "source-carry", Content = "second" }));

        Assert.Equal(source, Assert.Single(second.SourceReferences));
    }

    [Fact]
    public async Task Revise_persists_both_effective_interval_boundaries()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "interval", "first");
        var from = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(2);

        var second = await store.ReviseAssertionAsync("interval", first.RevisionId,
            new KnowledgeRevisionDraft(new Memory { Id = "interval", Content = "second" },
                EffectiveFromUtc: from, EffectiveToUtc: to));

        Assert.Equal(from, second.EffectiveFromUtc);
        Assert.Equal(to, second.EffectiveToUtc);
    }

    [Fact]
    public async Task Dispute_clear_preserves_an_archived_presentation_state()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "archived-dispute", "fact");
        var archived = await store.MutatePresentationAsync("archived-dispute", first.RevisionId,
            KnowledgePresentationMutation.FromMemory(new Memory { Id = "archived-dispute", Content = "fact", IsArchived = true }));

        await store.SetDisputeAsync("archived-dispute", archived.RevisionId, true,
            new KnowledgeRevisionDecision("dispute", "owner", "review", DateTime.UtcNow));
        var cleared = await store.SetDisputeAsync("archived-dispute", archived.RevisionId, false,
            new KnowledgeRevisionDecision("clear-dispute", "owner", "reviewed", DateTime.UtcNow));

        Assert.Equal(KnowledgeRevisionStatus.Archived, cleared.Status);
        Assert.Empty(await store.GetAllAsync());
        Assert.Single(await store.GetAllAsync(includeArchived: true));
    }

    [Fact]
    public async Task Dispute_requires_the_current_revision_even_when_the_assertion_exists()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "dispute-stale", "first");
        await store.ReviseAssertionAsync("dispute-stale", first.RevisionId,
            new KnowledgeRevisionDraft(new Memory { Id = "dispute-stale", Content = "second" }));

        await Assert.ThrowsAsync<KnowledgeRevisionConflictException>(() => store.SetDisputeAsync(
            "dispute-stale", first.RevisionId, true,
            new KnowledgeRevisionDecision("dispute", "owner", "review", DateTime.UtcNow)));
    }

    [Fact]
    public async Task Presentation_archive_hides_the_assertion_without_creating_history()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "presentation-archive", "fact");

        var archived = new Memory { Id = "presentation-archive", Content = "fact", IsArchived = true };
        var current = await store.MutatePresentationAsync("presentation-archive", first.RevisionId,
            KnowledgePresentationMutation.FromMemory(archived));

        Assert.Equal(first.RevisionId, current.RevisionId);
        Assert.Single(await store.GetHistoryAsync("presentation-archive"));
        Assert.Empty(await store.SearchAsync("fact"));
        Assert.Single(await store.GetAllAsync(includeArchived: true));
    }

    [Fact]
    public async Task Presentation_mutation_is_stale_guarded()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "presentation-stale", "fact");
        var second = await store.ReviseAssertionAsync("presentation-stale", first.RevisionId,
            new KnowledgeRevisionDraft(new Memory { Id = "presentation-stale", Content = "new fact" }));

        await Assert.ThrowsAsync<KnowledgeRevisionConflictException>(() => store.MutatePresentationAsync(
            "presentation-stale", first.RevisionId,
            KnowledgePresentationMutation.FromMemory(new Memory { Id = "presentation-stale", Content = "fact", IsPinned = true })));
        Assert.False((await store.GetByIdAsync("presentation-stale"))!.IsPinned);
        Assert.Equal(second.RevisionId, (await store.GetCurrentRevisionAsync("presentation-stale"))!.RevisionId);
    }

    [Fact]
    public async Task As_of_interval_end_is_exclusive()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "interval-end", "fact");
        var from = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(1);
        await store.ReviseAssertionAsync("interval-end", first.RevisionId,
            new KnowledgeRevisionDraft(new Memory { Id = "interval-end", Content = "bounded fact" },
                EffectiveFromUtc: from, EffectiveToUtc: to));

        Assert.Single(await store.QueryAsync(new KnowledgeTimeQuery(
            KnowledgeTimeQueryMode.AsOf, AsOfUtc: from.AddHours(12))));
        Assert.Empty(await store.QueryAsync(new KnowledgeTimeQuery(
            KnowledgeTimeQueryMode.AsOf, AsOfUtc: to)));
    }

    [Fact]
    public async Task As_of_query_filters_scope_and_scope_id()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var when = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        await store.CreateAssertionAsync(new KnowledgeRevisionDraft(new Memory
        {
            Id = "scope-a", Content = "a", Scope = MemoryScope.Project, ScopeId = "project-a"
        }, EffectiveFromUtc: when));
        await store.CreateAssertionAsync(new KnowledgeRevisionDraft(new Memory
        {
            Id = "scope-b", Content = "b", Scope = MemoryScope.Project, ScopeId = "project-b"
        }, EffectiveFromUtc: when));

        var results = await store.QueryAsync(new KnowledgeTimeQuery(KnowledgeTimeQueryMode.AsOf,
            AsOfUtc: when.AddHours(1), Scope: MemoryScope.Project, ScopeId: "project-b"));

        var result = Assert.Single(results);
        Assert.Equal("scope-b", result.AssertionId);
    }

    [Fact]
    public async Task As_of_query_requires_an_explicit_time()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);

        await Assert.ThrowsAsync<ArgumentException>(() => store.QueryAsync(
            new KnowledgeTimeQuery(KnowledgeTimeQueryMode.AsOf)));
    }

    [Fact]
    public async Task History_query_can_exclude_disputed_revisions()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "history-dispute", "fact");
        await store.SetDisputeAsync("history-dispute", first.RevisionId, true,
            new KnowledgeRevisionDecision("dispute", "owner", "review", DateTime.UtcNow));

        var normalHistory = await store.QueryAsync(new KnowledgeTimeQuery(KnowledgeTimeQueryMode.History));
        var fullHistory = await store.QueryAsync(new KnowledgeTimeQuery(
            KnowledgeTimeQueryMode.History, IncludeDisputed: true));

        Assert.Empty(normalHistory);
        Assert.Single(fullHistory);
    }

    [Fact]
    public async Task History_is_newest_first_and_preserves_lineage_order_fields()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "history-order", "first");
        var second = await store.ReviseAssertionAsync("history-order", first.RevisionId,
            new KnowledgeRevisionDraft(new Memory { Id = "history-order", Content = "second" }));
        var third = await store.CorrectAssertionAsync("history-order", second.RevisionId,
            new KnowledgeRevisionDraft(new Memory { Id = "history-order", Content = "third" }));

        var history = await store.GetHistoryAsync("history-order");

        Assert.Equal(3, history.Count);
        Assert.Equal(third.RevisionId, history[0].RevisionId);
        Assert.Equal(second.RevisionId, history[0].PreviousRevisionId);
        Assert.Equal(first.RevisionId, history[2].RevisionId);
    }

    [Fact]
    public async Task Restore_rejects_a_revision_from_another_assertion()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "restore-a", "a");
        var other = await CreateAsync(store, "restore-b", "b");

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RestoreRevisionAsync(
            "restore-a", first.RevisionId, other.RevisionId,
            new KnowledgeRevisionDecision("restore", "owner", "wrong target", DateTime.UtcNow)));
    }

    [Fact]
    public async Task Restore_preserves_the_selected_revision_source()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var source = new SourceReference(ProvenanceKind.Memory, "Original source", "source-id");
        var first = await store.CreateAssertionAsync(new KnowledgeRevisionDraft(
            new Memory { Id = "restore-source", Content = "original", Source = source }));
        var second = await store.ReviseAssertionAsync("restore-source", first.RevisionId,
            new KnowledgeRevisionDraft(new Memory { Id = "restore-source", Content = "replacement" }));

        var restored = await store.RestoreRevisionAsync("restore-source", second.RevisionId, first.RevisionId,
            new KnowledgeRevisionDecision("restore", "owner", "selected original", DateTime.UtcNow));

        Assert.Equal(source, Assert.Single(restored.SourceReferences));
        Assert.Equal(source, (await store.GetByIdAsync("restore-source"))!.Source);
    }

    [Fact]
    public async Task Restore_requires_the_expected_current_revision()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "restore-stale", "first");
        var second = await store.ReviseAssertionAsync("restore-stale", first.RevisionId,
            new KnowledgeRevisionDraft(new Memory { Id = "restore-stale", Content = "second" }));

        await Assert.ThrowsAsync<KnowledgeRevisionConflictException>(() => store.RestoreRevisionAsync(
            "restore-stale", first.RevisionId, first.RevisionId,
            new KnowledgeRevisionDecision("restore", "owner", "stale", DateTime.UtcNow)));
        Assert.Equal(second.RevisionId, (await store.GetCurrentRevisionAsync("restore-stale"))!.RevisionId);
    }

    [Fact]
    public async Task Hard_delete_requires_the_expected_current_revision()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "delete-stale", "first");
        var second = await store.ReviseAssertionAsync("delete-stale", first.RevisionId,
            new KnowledgeRevisionDraft(new Memory { Id = "delete-stale", Content = "second" }));

        await Assert.ThrowsAsync<KnowledgeRevisionConflictException>(() => store.HardDeleteAsync(
            "delete-stale", first.RevisionId));
        Assert.Equal(second.RevisionId, (await store.GetCurrentRevisionAsync("delete-stale"))!.RevisionId);
    }

    [Fact]
    public async Task Hard_delete_removes_sources_decisions_revisions_fts_and_projection()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await store.CreateAssertionAsync(new KnowledgeRevisionDraft(
            new Memory { Id = "delete-all", Content = "first" },
            SourceReferences: [new SourceReference(ProvenanceKind.Memory, "source")],
            Decision: new KnowledgeRevisionDecision("create", "owner", "reason", DateTime.UtcNow)));
        await store.SetDisputeAsync("delete-all", first.RevisionId, true,
            new KnowledgeRevisionDecision("dispute", "owner", "reason", DateTime.UtcNow));

        await store.HardDeleteAsync("delete-all", first.RevisionId);

        await using var connection = new SqliteConnection($"Data Source={DataPath(temp)}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                (SELECT COUNT(1) FROM knowledge_assertions WHERE assertion_id = 'delete-all'),
                (SELECT COUNT(1) FROM knowledge_revisions WHERE assertion_id = 'delete-all'),
                (SELECT COUNT(1) FROM knowledge_revision_sources WHERE revision_id = $revision),
                (SELECT COUNT(1) FROM knowledge_revision_decisions WHERE assertion_id = 'delete-all'),
                (SELECT COUNT(1) FROM memories WHERE id = 'delete-all'),
                (SELECT COUNT(1) FROM memories_fts WHERE id = 'delete-all')";
        command.Parameters.AddWithValue("$revision", first.RevisionId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        for (var i = 0; i < 6; i++)
            Assert.Equal(0L, reader.GetInt64(i));
    }

    [Fact]
    public async Task Hard_delete_remains_deleted_after_a_new_store_initializes()
    {
        using var temp = new TempDir();
        var firstStore = NewStore(temp);
        var first = await CreateAsync(firstStore, "delete-restart", "fact");
        await firstStore.HardDeleteAsync("delete-restart", first.RevisionId);

        var restartedStore = NewStore(temp);
        await restartedStore.InitializeAsync();

        Assert.Null(await restartedStore.GetCurrentRevisionAsync("delete-restart"));
        Assert.Empty(await restartedStore.GetHistoryAsync("delete-restart"));
        Assert.Null(await restartedStore.GetByIdAsync("delete-restart"));
    }

    [Fact]
    public async Task Legacy_migration_preserves_archived_state_and_source_without_effective_time()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.SaveAsync(new Memory
        {
            Id = "legacy-archived",
            Content = "old row",
            IsArchived = true,
            Source = new SourceReference(ProvenanceKind.Workspace, "Legacy source")
        });

        var revision = Assert.Single(await store.GetHistoryAsync("legacy-archived"));

        Assert.Equal(KnowledgeRevisionStatus.Archived, revision.Status);
        Assert.Null(revision.EffectiveFromUtc);
        Assert.Equal("Legacy source", Assert.Single(revision.SourceReferences).Title);
        Assert.Empty(await store.GetAllAsync());
        Assert.Single(await store.GetAllAsync(includeArchived: true));
    }

    [Fact]
    public async Task Legacy_migration_is_idempotent_across_store_instances()
    {
        using var temp = new TempDir();
        var firstStore = NewStore(temp);
        await firstStore.SaveAsync(new Memory { Id = "legacy-idempotent", Content = "old row" });
        Assert.Single(await firstStore.GetHistoryAsync("legacy-idempotent"));

        var secondStore = NewStore(temp);
        var history = await secondStore.GetHistoryAsync("legacy-idempotent");

        Assert.Single(history);
        Assert.Equal("legacy:legacy-idempotent", history[0].RevisionId);
    }

    [Fact]
    public async Task Current_query_does_not_return_superseded_revisions()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var first = await CreateAsync(store, "current-only", "first");
        var second = await store.ReviseAssertionAsync("current-only", first.RevisionId,
            new KnowledgeRevisionDraft(new Memory { Id = "current-only", Content = "second" }));

        var current = await store.QueryAsync(new KnowledgeTimeQuery(KnowledgeTimeQueryMode.Current));

        var revision = Assert.Single(current);
        Assert.Equal(second.RevisionId, revision.RevisionId);
        Assert.DoesNotContain(current, item => item.RevisionId == first.RevisionId);
    }

    [Fact]
    public async Task Current_query_can_filter_scope()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        await store.CreateAssertionAsync(new KnowledgeRevisionDraft(new Memory
        {
            Id = "current-project", Content = "project", Scope = MemoryScope.Project, ScopeId = "project-1"
        }));
        await store.CreateAssertionAsync(new KnowledgeRevisionDraft(new Memory
        {
            Id = "current-global", Content = "global"
        }));

        var current = await store.QueryAsync(new KnowledgeTimeQuery(
            KnowledgeTimeQueryMode.Current, Scope: MemoryScope.Project, ScopeId: "project-1"));

        Assert.Equal("current-project", Assert.Single(current).AssertionId);
    }

    [Fact]
    public async Task Revision_sources_and_decisions_reload_after_store_restart()
    {
        using var temp = new TempDir();
        var firstStore = NewStore(temp);
        var created = await firstStore.CreateAssertionAsync(new KnowledgeRevisionDraft(
            new Memory { Id = "reload-lineage", Content = "fact" },
            SourceReferences: [new SourceReference(ProvenanceKind.Memory, "source", "locator")],
            Decision: new KnowledgeRevisionDecision("create", "owner", "accepted", DateTime.UtcNow)));

        var secondStore = NewStore(temp);
        var reloaded = await secondStore.GetCurrentRevisionAsync("reload-lineage");

        Assert.Equal(created.RevisionId, reloaded!.RevisionId);
        Assert.Equal("locator", Assert.Single(reloaded.SourceReferences).Locator);
        Assert.Equal("accepted", reloaded.Decision!.Reason);
    }

    [Fact]
    public async Task Contradiction_proposal_records_two_exact_revisions_without_changing_status()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var left = await CreateAsync(store, "proposal-left", "left fact");
        var right = await CreateAsync(store, "proposal-right", "right fact");

        var proposal = await store.CreateContradictionProposalAsync(new KnowledgeContradictionProposalDraft(
            "proposal-left", left.RevisionId, "proposal-right", right.RevisionId,
            "The two facts disagree about the same setting.",
            "Different conversation sources.", "Both have unknown effective time.",
            KnowledgeContradictionDisposition.MarkDisputed, "Need an owner decision."));

        Assert.Equal(KnowledgeContradictionProposalStatus.Pending, proposal.Status);
        Assert.Equal(KnowledgeTemporalOrigin.ModelInference, proposal.Origin);
        Assert.Equal(left.RevisionId, proposal.LeftRevisionId);
        Assert.Equal(right.RevisionId, proposal.RightRevisionId);
        Assert.Equal(KnowledgeRevisionStatus.Current, (await store.GetCurrentRevisionAsync("proposal-left"))!.Status);
        Assert.Equal(KnowledgeRevisionStatus.Current, (await store.GetCurrentRevisionAsync("proposal-right"))!.Status);
    }

    [Fact]
    public async Task Contradiction_proposal_normalizes_deterministic_origin_only_when_explicit()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var left = await CreateAsync(store, "origin-left", "left");
        var right = await CreateAsync(store, "origin-right", "right");
        var draft = new KnowledgeContradictionProposalDraft(
            "origin-left", left.RevisionId, "origin-right", right.RevisionId,
            "reason", "sources", "time", KnowledgeContradictionDisposition.Coexist, "evidence",
            KnowledgeTemporalOrigin.UserProvided);

        var proposal = await store.CreateContradictionProposalAsync(draft);

        Assert.Equal(KnowledgeTemporalOrigin.ModelInference, proposal.Origin);
        var deterministic = await store.CreateContradictionProposalAsync(draft with
        {
            Origin = KnowledgeTemporalOrigin.DeterministicRule
        });
        Assert.Equal(KnowledgeTemporalOrigin.DeterministicRule, deterministic.Origin);
    }

    [Fact]
    public async Task Contradiction_proposals_can_be_filtered_by_assertion_and_pending_status()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var left = await CreateAsync(store, "filter-left", "left");
        var right = await CreateAsync(store, "filter-right", "right");
        var unrelated = await CreateAsync(store, "filter-unrelated", "unrelated");
        await store.CreateContradictionProposalAsync(new KnowledgeContradictionProposalDraft(
            "filter-left", left.RevisionId, "filter-right", right.RevisionId,
            "reason", "sources", "time", KnowledgeContradictionDisposition.Coexist, "evidence"));
        await store.CreateContradictionProposalAsync(new KnowledgeContradictionProposalDraft(
            "filter-right", right.RevisionId, "filter-unrelated", unrelated.RevisionId,
            "reason", "sources", "time", KnowledgeContradictionDisposition.NoRelationship, "evidence"));

        var filtered = await store.GetContradictionProposalsAsync("filter-left");
        var all = await store.GetContradictionProposalsAsync();

        Assert.Single(filtered);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Rejecting_a_contradiction_proposal_is_durable_and_does_not_hide_either_revision()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var left = await CreateAsync(store, "reject-left", "left");
        var right = await CreateAsync(store, "reject-right", "right");
        var proposal = await store.CreateContradictionProposalAsync(new KnowledgeContradictionProposalDraft(
            "reject-left", left.RevisionId, "reject-right", right.RevisionId,
            "reason", "sources", "time", KnowledgeContradictionDisposition.MarkDisputed, "evidence"));

        await store.RejectContradictionProposalAsync(proposal.ProposalId,
            new KnowledgeRevisionDecision("reject", "owner", "insufficient evidence", DateTime.UtcNow));

        Assert.Empty(await store.GetContradictionProposalsAsync());
        var rejected = Assert.Single(await store.GetContradictionProposalsAsync(includeReviewed: true));
        Assert.Equal(KnowledgeContradictionProposalStatus.Rejected, rejected.Status);
        Assert.Equal("insufficient evidence", rejected.Decision!.Reason);
        Assert.NotNull(await store.GetByIdAsync("reject-left"));
        Assert.NotNull(await store.GetByIdAsync("reject-right"));
    }

    [Fact]
    public async Task Rejecting_a_contradiction_proposal_twice_fails_without_replacing_the_decision()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var left = await CreateAsync(store, "reject-twice-left", "left");
        var right = await CreateAsync(store, "reject-twice-right", "right");
        var proposal = await store.CreateContradictionProposalAsync(new KnowledgeContradictionProposalDraft(
            "reject-twice-left", left.RevisionId, "reject-twice-right", right.RevisionId,
            "reason", "sources", "time", KnowledgeContradictionDisposition.Coexist, "evidence"));
        await store.RejectContradictionProposalAsync(proposal.ProposalId,
            new KnowledgeRevisionDecision("reject", "owner", "first", DateTime.UtcNow));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RejectContradictionProposalAsync(
            proposal.ProposalId, new KnowledgeRevisionDecision("reject", "owner", "second", DateTime.UtcNow)));
        Assert.Equal("first", (await store.GetContradictionProposalsAsync(includeReviewed: true)).Single().Decision!.Reason);
    }

    [Fact]
    public async Task Contradiction_proposal_rejects_revision_identity_mismatches()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var left = await CreateAsync(store, "mismatch-left", "left");
        var right = await CreateAsync(store, "mismatch-right", "right");

        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateContradictionProposalAsync(
            new KnowledgeContradictionProposalDraft(
                "mismatch-left", right.RevisionId, "mismatch-right", left.RevisionId,
                "reason", "sources", "time", KnowledgeContradictionDisposition.Coexist, "evidence")));
    }

    [Fact]
    public async Task Hard_delete_removes_contradiction_proposals_referencing_the_assertion()
    {
        using var temp = new TempDir();
        var store = NewStore(temp);
        var left = await CreateAsync(store, "proposal-delete-left", "left");
        var right = await CreateAsync(store, "proposal-delete-right", "right");
        var proposal = await store.CreateContradictionProposalAsync(new KnowledgeContradictionProposalDraft(
            "proposal-delete-left", left.RevisionId, "proposal-delete-right", right.RevisionId,
            "reason", "sources", "time", KnowledgeContradictionDisposition.Coexist, "evidence"));
        await store.RejectContradictionProposalAsync(proposal.ProposalId,
            new KnowledgeRevisionDecision("reject", "owner", "done", DateTime.UtcNow));

        await store.HardDeleteAsync("proposal-delete-left", left.RevisionId);

        Assert.Empty(await store.GetContradictionProposalsAsync(includeReviewed: true));
        Assert.NotNull(await store.GetByIdAsync("proposal-delete-right"));
    }

    private static MemoryStore NewStore(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        return new MemoryStore(settings);
    }

    private static async Task<KnowledgeAssertionRevision> CreateAsync(MemoryStore store, string id, string content) =>
        await store.CreateAssertionAsync(new KnowledgeRevisionDraft(new Memory { Id = id, Content = content }));

    private static string DataPath(TempDir temp) => Path.Combine(temp.PathFor("data"), "memories.db");
}
