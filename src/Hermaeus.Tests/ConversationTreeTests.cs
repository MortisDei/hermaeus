using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// r25 doc 01: the tree walk is pure and runs on the UI thread for every render
/// of every conversation, over a messages_json blob the user owns, syncs and can
/// edit. It gets the coverage that implies.
/// </summary>
public sealed class ConversationTreeTests
{
    private static Message M(string id, string parentId, int minute = 0) => new()
    {
        Id = id,
        ParentId = parentId,
        CreatedAt = new DateTime(2026, 7, 29, 12, minute, 0, DateTimeKind.Utc)
    };

    private static List<Message> Linear() =>
    [
        M("a", string.Empty, 0),
        M("b", "a", 1),
        M("c", "b", 2)
    ];

    [Fact]
    public void PathTo_returns_root_to_leaf_in_order()
    {
        var path = ConversationTree.PathTo(Linear(), "c");

        Assert.Equal(["a", "b", "c"], path.Select(m => m.Id));
    }

    [Fact]
    public void PathTo_returns_nothing_for_an_unknown_or_empty_leaf()
    {
        Assert.Empty(ConversationTree.PathTo(Linear(), "nope"));
        Assert.Empty(ConversationTree.PathTo(Linear(), string.Empty));
        Assert.Empty(ConversationTree.PathTo(Linear(), null));
        Assert.Empty(ConversationTree.PathTo<Message>([], "a"));
    }

    /// <summary>
    /// A cycle must truncate, not hang. messages_json is a text blob in a file
    /// the user owns and can sync or hand-edit, and this walk happens on the UI
    /// thread with no cancel.
    /// </summary>
    [Fact]
    public void PathTo_terminates_on_a_cycle()
    {
        List<Message> cyclic = [M("a", "b", 0), M("b", "a", 1)];

        var path = ConversationTree.PathTo(cyclic, "b");

        Assert.Equal(2, path.Count);
        Assert.Equal(["a", "b"], path.Select(m => m.Id));
    }

    [Fact]
    public void Subtree_terminates_on_a_cycle()
    {
        List<Message> cyclic = [M("a", string.Empty, 0), M("b", "a", 1), M("c", "b", 2)];
        cyclic[0].ParentId = "c";

        var subtree = ConversationTree.Subtree(cyclic, "a");

        Assert.Equal(3, subtree.Count);
    }

    [Fact]
    public void ChildrenOf_orders_by_created_then_id()
    {
        List<Message> messages =
        [
            M("root", string.Empty, 0),
            M("zz", "root", 5),
            M("aa", "root", 5),
            M("mm", "root", 1)
        ];

        var children = ConversationTree.ChildrenOf(messages, "root");

        Assert.Equal(["mm", "aa", "zz"], children.Select(m => m.Id));
    }

    [Fact]
    public void Leaves_finds_the_end_of_every_branch()
    {
        List<Message> messages = [M("a", string.Empty), M("b", "a"), M("c", "a")];

        var leaves = ConversationTree.Leaves(messages);

        Assert.Equal(["b", "c"], leaves.Select(m => m.Id).OrderBy(id => id));
    }

    [Fact]
    public void ResolveLeaf_empty_pointer_takes_the_last_message_in_stored_order()
    {
        Assert.Equal("c", ConversationTree.ResolveLeaf(Linear(), string.Empty));
        Assert.Equal("c", ConversationTree.ResolveLeaf(Linear(), null));
    }

    [Fact]
    public void ResolveLeaf_keeps_a_valid_pointer()
    {
        Assert.Equal("b", ConversationTree.ResolveLeaf(Linear(), "b"));
    }

    [Fact]
    public void ResolveLeaf_falls_back_to_the_newest_leaf_when_the_pointer_is_gone()
    {
        List<Message> messages = [M("a", string.Empty, 0), M("b", "a", 1), M("c", "a", 9)];

        Assert.Equal("c", ConversationTree.ResolveLeaf(messages, "deleted-subtree-leaf"));
    }

    [Fact]
    public void ResolveLeaf_of_an_empty_conversation_is_empty()
    {
        Assert.Equal(string.Empty, ConversationTree.ResolveLeaf<Message>([], "anything"));
    }

    [Fact]
    public void NewestLeafUnder_descends_to_the_newest_leaf_of_a_sibling()
    {
        List<Message> messages =
        [
            M("root", string.Empty, 0),
            M("branchA", "root", 1),
            M("branchB", "root", 2),
            M("a-old", "branchA", 3),
            M("a-new", "branchA", 8)
        ];

        var branchA = messages.First(m => m.Id == "branchA");

        Assert.Equal("a-new", ConversationTree.NewestLeafUnder(messages, branchA));
    }

    [Fact]
    public void NewestLeafUnder_of_a_childless_message_is_itself()
    {
        List<Message> messages = [M("a", string.Empty), M("b", "a")];

        Assert.Equal("b", ConversationTree.NewestLeafUnder(messages, messages[1]));
    }

    [Fact]
    public void Subtree_collects_everything_beneath_a_root()
    {
        List<Message> messages =
        [
            M("root", string.Empty),
            M("x", "root"),
            M("y", "x"),
            M("other", "root")
        ];

        var subtree = ConversationTree.Subtree(messages, "x");

        Assert.Equal(["x", "y"], subtree.Select(m => m.Id).OrderBy(id => id));
    }

    [Fact]
    public void SiblingsOf_includes_the_message_itself()
    {
        List<Message> messages = [M("root", string.Empty), M("b", "root", 1), M("c", "root", 2)];

        var siblings = ConversationTree.SiblingsOf(messages, messages[1]);

        Assert.Equal(["b", "c"], siblings.Select(m => m.Id));
    }

    // ── Backfill: the highest-risk change in the round ────────────────────────

    [Fact]
    public void BackfillLinearChain_infers_the_chain_stored_order_implies()
    {
        List<Message> flat = [M("a", string.Empty), M("b", string.Empty), M("c", string.Empty)];

        Assert.True(ConversationTree.BackfillLinearChain(flat));
        Assert.Equal(string.Empty, flat[0].ParentId);
        Assert.Equal("a", flat[1].ParentId);
        Assert.Equal("b", flat[2].ParentId);

        // And the whole thing is now one path, rendering exactly as it did before r25.
        var path = ConversationTree.ActivePath(flat, string.Empty);
        Assert.Equal(["a", "b", "c"], path.Select(m => m.Id));
    }

    /// <summary>
    /// A conversation that already has parents is a real tree. Re-inferring a
    /// chain over it would flatten its branches into nonsense.
    /// </summary>
    [Fact]
    public void BackfillLinearChain_leaves_an_existing_tree_alone()
    {
        List<Message> tree = [M("a", string.Empty), M("b", "a"), M("c", "a")];

        Assert.False(ConversationTree.BackfillLinearChain(tree));
        Assert.Equal("a", tree[1].ParentId);
        Assert.Equal("a", tree[2].ParentId);
    }

    [Fact]
    public void BackfillLinearChain_is_a_no_op_below_two_messages()
    {
        Assert.False(ConversationTree.BackfillLinearChain([]));
        Assert.False(ConversationTree.BackfillLinearChain([M("only", string.Empty)]));
    }

    /// <summary>
    /// The property that matters most: an unbranched conversation renders the
    /// identical message sequence before and after r25.
    /// </summary>
    [Fact]
    public void An_unbranched_conversation_renders_the_same_sequence_as_the_flat_list()
    {
        var flat = Linear();
        foreach (var message in flat)
            message.ParentId = string.Empty;

        ConversationTree.BackfillLinearChain(flat);
        var rendered = ConversationTree.ActivePath(flat, string.Empty);

        Assert.Equal(flat.Select(m => m.Id), rendered.Select(m => m.Id));
    }
}
