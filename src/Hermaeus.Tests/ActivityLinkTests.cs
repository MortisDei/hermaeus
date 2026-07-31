using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// Activity always carried the artifact's identifier and the app always had a
/// typed navigation instruction; the two had never been introduced (r28 doc
/// 03). Links are deterministic and time grouping is arithmetic. Nothing here
/// explains why two rows are related, because that is not a thing this panel
/// is allowed to claim.
/// </summary>
public sealed class ActivityLinkTests
{
    private static ActivityEvent Event(string operation, string sourceId) =>
        new("id", DateTime.UtcNow, operation, sourceId, ActivityOutcome.Succeeded, "title", "reason", "");

    // ── 3.1 the resolver ──

    [Fact]
    public void An_agent_row_points_at_its_task()
    {
        var target = ActivityTargetResolver.Resolve(Event("agent.run", "task-7"));

        Assert.Equal("task-7", target!.TaskId);
        Assert.Equal(RecallKind.Task, ActivityTargetResolver.KindFor(target));
    }

    [Fact]
    public void A_rag_row_points_at_its_dataset()
    {
        foreach (var operation in new[] { "rag.ingest", "rag.watched-refresh" })
        {
            var target = ActivityTargetResolver.Resolve(Event(operation, "ds-1"));
            Assert.Equal("ds-1", target!.DatasetId);
            Assert.Equal(RecallKind.Document, ActivityTargetResolver.KindFor(target));
        }
    }

    [Fact]
    public void A_chat_row_points_at_its_conversation()
    {
        var target = ActivityTargetResolver.Resolve(Event("chat.send", "conv-9"));

        Assert.Equal("conv-9", target!.ConversationId);
        Assert.Equal(RecallKind.Message, ActivityTargetResolver.KindFor(target));
    }

    [Fact]
    public void A_memory_row_points_at_its_memory()
    {
        var target = ActivityTargetResolver.Resolve(Event("memory.merge", "mem-3"));

        Assert.Equal("mem-3", target!.MemoryId);
        Assert.Equal(RecallKind.Memory, ActivityTargetResolver.KindFor(target));
    }

    [Fact]
    public void A_row_with_no_source_id_points_nowhere()
    {
        Assert.Null(ActivityTargetResolver.Resolve(Event("agent.run", "")));
        Assert.Null(ActivityTargetResolver.Resolve(Event("agent.run", "   ")));
        // A memory sweep is not about one memory, so it carries no id and
        // shows no link. That is correct, not a gap.
        Assert.Null(ActivityTargetResolver.Resolve(Event("memory.auto-archive", "")));
    }

    [Fact]
    public void An_unrecognised_operation_points_nowhere_rather_than_guessing()
    {
        Assert.Null(ActivityTargetResolver.Resolve(Event("something.new", "x")));
        Assert.Null(ActivityTargetResolver.Resolve(Event("", "x")));
    }

    /// <summary>
    /// The mapping is total over the operation strings actually in use. A new
    /// operation that is neither linked nor deliberately listed here fails
    /// this test rather than silently losing its link.
    /// </summary>
    [Theory]
    [InlineData("doctor.scan")]
    [InlineData("rag.ingest")]
    [InlineData("rag.watched-refresh")]
    [InlineData("services.server-start")]
    [InlineData("services.server-stop")]
    [InlineData("services.server-crash")]
    [InlineData("models.download")]
    [InlineData("backup.write")]
    [InlineData("backup.restore")]
    [InlineData("memory.auto-archive")]
    [InlineData("voice.backend-start")]
    [InlineData("voice.backend-stop")]
    public void Every_operation_in_use_either_links_or_is_deliberately_unlinked(string operation)
    {
        var linked = ActivityTargetResolver.LinkedPrefixes
            .Any(p => operation.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        // "memory.auto-archive" is the one linked-prefix operation that
        // carries no id, because a sweep is not about one memory.
        var resolved = ActivityTargetResolver.Resolve(operation, "some-id");
        Assert.Equal(linked, resolved is not null);
    }

    // ── 3.2 the view routes through the existing navigator ──

    private static ActivityRowViewModel Row(string operation, string sourceId, DateTime? at = null) => new()
    {
        Timestamp = at ?? DateTime.UtcNow,
        Operation = operation,
        SourceId = sourceId,
        Title = "did a thing"
    };

    [Fact]
    public async Task Activating_a_linked_row_hands_a_recall_hit_to_the_shared_navigator()
    {
        RecallHit? navigated = null;
        var vm = new ActivityViewModel(new NoOpToasts()) { RequestNavigate = hit => { navigated = hit; return Task.CompletedTask; } };

        await vm.OpenCommand.ExecuteAsync(Row("agent.run", "task-7"));

        Assert.NotNull(navigated);
        Assert.Equal("task-7", navigated!.Target.TaskId);
        Assert.Equal(RecallKind.Task, navigated.Kind);
    }

    [Fact]
    public async Task Activating_an_unlinked_row_does_nothing()
    {
        var navigated = false;
        var vm = new ActivityViewModel(new NoOpToasts()) { RequestNavigate = _ => { navigated = true; return Task.CompletedTask; } };

        await vm.OpenCommand.ExecuteAsync(Row("doctor.scan", "whatever"));
        await vm.OpenCommand.ExecuteAsync(null);

        Assert.False(navigated);
    }

    [Fact]
    public void A_row_knows_whether_it_has_somewhere_to_go()
    {
        Assert.True(Row("rag.ingest", "ds-1").HasTarget);
        Assert.False(Row("services.server-start", "srv-1").HasTarget);
    }

    // ── 3.4 grouping is arithmetic ──

    [Fact]
    public void Rows_within_the_window_share_one_heading()
    {
        var now = DateTime.UtcNow;
        var rows = new[]
        {
            Row("rag.ingest", "a", now),
            Row("services.server-start", "b", now.AddSeconds(-10)),
            Row("doctor.scan", "", now.AddSeconds(-30)),
            Row("backup.write", "c", now.AddMinutes(-20))
        };

        ActivityViewModel.MarkTimeGroups(rows);

        Assert.True(rows[0].StartsGroup);
        Assert.False(rows[1].StartsGroup);
        Assert.False(rows[2].StartsGroup);
        Assert.True(rows[3].StartsGroup);
    }

    [Fact]
    public void An_empty_list_groups_without_throwing()
    {
        ActivityViewModel.MarkTimeGroups([]);
    }

    private sealed class NoOpToasts : IToastService
    {
        public event Action<ToastMessage>? ToastRaised;
        public void Show(string title, string message, ToastKind kind = ToastKind.Info, int durationMs = 3500) => ToastRaised?.Invoke(new ToastMessage(title, message, kind, durationMs));
    }
}
