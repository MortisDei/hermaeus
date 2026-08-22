using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class KnowledgeRelationshipTests
{
    [Fact]
    public async Task Legacy_related_memory_ids_round_trip_as_typed_relationships()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new MemoryStore(settings);

        await store.SaveAsync(new Memory
        {
            Id = "source",
            Content = "A stored fact.",
            RelatedMemoryIds = ["target"]
        });

        var reloaded = await store.GetByIdAsync("source");
        Assert.NotNull(reloaded);
        Assert.Equal(["target"], reloaded.RelatedMemoryIds);
        var relationship = Assert.Single(reloaded.Relationships);
        Assert.Equal(KnowledgeRelationshipKind.RelatedTo, relationship.Kind);
        Assert.Equal(KnowledgeEntityKind.Memory, relationship.Target.Kind);
        Assert.Equal("target", relationship.Target.Id);
    }

    [Fact]
    public async Task Search_replaces_a_superseded_memory_with_its_direct_current_fact()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new MemoryStore(settings);

        await store.SaveAsync(new Memory
        {
            Id = "old",
            Title = "Old compiler setting",
            Content = "The compiler setting is legacy-value.",
            Relationships =
            [
                new KnowledgeRelationship(
                    new KnowledgeEntityReference(KnowledgeEntityKind.Memory, "current"),
                    KnowledgeRelationshipKind.Supersedes,
                    new SourceReference(ProvenanceKind.Memory, "Owner correction", EvidenceOrigin: EvidenceOrigin.UserProvided))
            ]
        });
        await store.SaveAsync(new Memory
        {
            Id = "current",
            Title = "Current compiler setting",
            Content = "The compiler setting is current-value."
        });

        var results = await store.SearchAsync("legacy-value");

        Assert.DoesNotContain(results, m => m.Id == "old");
        var current = Assert.Single(results);
        Assert.Equal("current", current.Id);
        Assert.NotNull(current.RetrievedViaRelationship);
        Assert.Equal("old", current.RetrievedViaRelationship!.SourceMemoryId);
        Assert.Equal(KnowledgeRelationshipKind.Supersedes, current.RetrievedViaRelationship.Kind);
        Assert.Contains("via superseded by relationship", current.ToContextSource().Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Fingerprint_is_stable_for_the_same_material_configuration_and_changes_for_prompt_threads()
    {
        var metadata = new BenchmarkRunMetadata
        {
            RuntimeKind = "llama.cpp",
            RuntimeVersion = "1.0",
            ContextSize = 8192,
            Threads = 8,
            PromptThreads = 4,
            KvCacheTypeK = "q8_0",
            KvCacheTypeV = "q8_0",
            FlashAttention = "on"
        };

        var baseline = EmpiricalProfileFingerprint.From(metadata, "model-a");
        var same = EmpiricalProfileFingerprint.From(metadata, "model-a");
        metadata.PromptThreads = 2;
        var different = EmpiricalProfileFingerprint.From(metadata, "model-a");

        Assert.Equal(baseline.StableId, same.StableId);
        Assert.NotEqual(baseline.StableId, different.StableId);
    }
}
