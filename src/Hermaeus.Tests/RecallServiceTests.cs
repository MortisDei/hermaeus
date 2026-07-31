using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services.Recall;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class RecallServiceTests
{
    private sealed class FakeSource : IRecallSource
    {
        private readonly IReadOnlyList<RecallHit> _hits;
        private readonly TimeSpan _delay;
        private readonly bool _throws;

        public FakeSource(string name, IReadOnlyList<RecallHit> hits, TimeSpan? delay = null, bool throws = false)
        {
            Name = name;
            _hits = hits;
            _delay = delay ?? TimeSpan.Zero;
            _throws = throws;
        }

        public string Name { get; }

        public async Task<IReadOnlyList<RecallHit>> SearchAsync(string query, string projectScope, CancellationToken ct)
        {
            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, ct);
            if (_throws)
                throw new InvalidOperationException("simulated source failure");
            return _hits;
        }
    }

    private static RecallHit Hit(RecallKind kind, string title, double score = 0) =>
        new(kind, title, "snippet", DateTime.UtcNow, "", score, new RecallTarget(ConversationId: "c1"));

    [Fact]
    public async Task Fusion_interleaves_disjoint_source_lists_by_reciprocal_rank()
    {
        var messages = new FakeSource("Conversations", [Hit(RecallKind.Message, "m1"), Hit(RecallKind.Message, "m2")]);
        var tasks = new FakeSource("Agent tasks", [Hit(RecallKind.Task, "t1")]);
        var memories = new FakeSource("Memories", []);
        var documents = new FakeSource("Documents", []);
        var service = new RecallService([messages, tasks, memories, documents], new FakeEmbeddingService());

        var result = await service.SearchAsync("anything");

        Assert.Equal(3, result.Hits.Count);
        var titles = result.Hits.Select(h => h.Title).ToList();
        var m1Index = titles.IndexOf("m1");
        var m2Index = titles.IndexOf("m2");
        var t1Index = titles.IndexOf("t1");
        Assert.True(m1Index < m2Index, "the higher-ranked hit within a source must stay ahead of its own lower-ranked sibling");
        Assert.True(t1Index < m2Index, "a rank-1 hit from one source must outrank a rank-2 hit from another");
    }

    [Fact]
    public async Task A_source_that_throws_is_omitted_and_named_not_left_out_silently()
    {
        var ok = new FakeSource("Conversations", [Hit(RecallKind.Message, "m1")]);
        var broken = new FakeSource("Documents", [], throws: true);
        var service = new RecallService([ok, broken], new FakeEmbeddingService());

        var result = await service.SearchAsync("query");

        Assert.Single(result.Hits);
        Assert.Contains("Documents", result.OmittedSources);
        Assert.DoesNotContain("Conversations", result.OmittedSources);
    }

    [Fact]
    public async Task A_source_that_exceeds_its_timeout_is_omitted_and_named_leaving_a_partial_result()
    {
        var fast = new FakeSource("Conversations", [Hit(RecallKind.Message, "m1")]);
        var slow = new FakeSource("Agent tasks", [Hit(RecallKind.Task, "t1")], delay: TimeSpan.FromSeconds(10));
        // r29 doc 04 4.5: this used to wait out the real 3 s source timeout on
        // every run, on both CI legs. The timeout is injectable now; what the
        // test asserts is unchanged.
        var service = new RecallService([fast, slow], new FakeEmbeddingService(),
            sourceTimeout: TimeSpan.FromMilliseconds(50));

        var result = await service.SearchAsync("query");

        Assert.Single(result.Hits);
        Assert.Equal("m1", result.Hits[0].Title);
        Assert.Contains("Agent tasks", result.OmittedSources);
    }

    [Fact]
    public async Task No_embedding_service_configured_is_reported_as_keyword_only()
    {
        var source = new FakeSource("Conversations", [Hit(RecallKind.Message, "m1")]);
        var service = new RecallService([source], embeddings: null);

        var result = await service.SearchAsync("query");

        Assert.True(result.KeywordOnly);
    }

    [Fact]
    public async Task An_embedding_service_configured_is_not_keyword_only()
    {
        var source = new FakeSource("Conversations", [Hit(RecallKind.Message, "m1")]);
        var service = new RecallService([source], new FakeEmbeddingService());

        var result = await service.SearchAsync("query");

        Assert.False(result.KeywordOnly);
    }

    [Fact]
    public async Task An_empty_query_returns_no_hits_without_calling_any_source()
    {
        var source = new FakeSource("Conversations", [Hit(RecallKind.Message, "m1")]);
        var service = new RecallService([source], new FakeEmbeddingService());

        var result = await service.SearchAsync("   ");

        Assert.Empty(result.Hits);
    }

    [Theory]
    [InlineData(RecallKind.Message)]
    [InlineData(RecallKind.Task)]
    [InlineData(RecallKind.Memory)]
    [InlineData(RecallKind.Document)]
    public void Every_recall_hit_kind_has_a_populated_navigation_target(RecallKind kind)
    {
        var target = kind switch
        {
            RecallKind.Message => new RecallTarget(ConversationId: "c1", MessageIndex: 2),
            RecallKind.Task => new RecallTarget(TaskId: "t1"),
            RecallKind.Memory => new RecallTarget(MemoryId: "m1"),
            RecallKind.Document => new RecallTarget(DatasetId: "d1", ChunkId: "ch1"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var hit = new RecallHit(kind, "title", "snippet", DateTime.UtcNow, "", 1.0, target);

        switch (hit.Kind)
        {
            case RecallKind.Message:
                Assert.NotEqual(string.Empty, hit.Target.ConversationId);
                Assert.True(hit.Target.MessageIndex >= 0);
                break;
            case RecallKind.Task:
                Assert.NotEqual(string.Empty, hit.Target.TaskId);
                break;
            case RecallKind.Memory:
                Assert.NotEqual(string.Empty, hit.Target.MemoryId);
                break;
            case RecallKind.Document:
                Assert.NotEqual(string.Empty, hit.Target.DatasetId);
                Assert.NotEqual(string.Empty, hit.Target.ChunkId);
                break;
        }
    }
}
