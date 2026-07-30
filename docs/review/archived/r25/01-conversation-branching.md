# 01. Conversation branching, and the end of destructive regenerate

## The problem

`ChatViewModel.RegenerateAsync` (`ChatViewModel.cs:1208-1232`) does this:

```
var lastAsst = Messages.LastOrDefault(m => m.IsAssistant);
if (lastAsst is not null) Messages.Remove(lastAsst);
var lastUser = Messages.LastOrDefault(m => m.IsUser);
...
Messages.Remove(lastUser);
InputText = userText;
await SendAsync();
```

The previous answer is deleted. If the regenerated one is worse, it is
gone. There is no undo, and the conversation is persisted after the send,
so it is gone from disk too. A button labelled as a retry destroys user
data every time it is pressed.

There is also no message editing at all. To rephrase a question you retype
it and lose the branch you were on.

r24 recorded this as the leading r25 candidate and declined to smuggle it
in, describing it as "a schema change to the message tree plus a rendering
change" (`docs/review/archived/r24/06-roadmap.md:186-189`).

## What r24 assumed, and what is actually true

That estimate was pessimistic, and the difference matters for scoping.

Conversations persist their messages as a single JSON blob:
`messages_json TEXT NOT NULL` (`ConversationStore.cs:47-54`). `Message`
already carries a stable `Id` (`Message.cs:5`). So the tree itself needs
**no SQLite migration at all**: `ParentId` is an additive JSON property
that old rows simply lack. One additive column is wanted for the active
leaf pointer, and that is the whole schema story.

Verify both facts before building. If `messages_json` has been normalized
into a real messages table by the time this is implemented, the doc still
holds but the migration is larger.

## 1.1 The tree

**`Message.ParentId`** (string, empty for the first message in a
conversation). Additive, JSON-serialized with the rest of `Message`.

**`Conversation.ActiveLeafId`** (string). New column `active_leaf_id TEXT
NOT NULL DEFAULT ''` through `SqliteMigrationRunner`, schema version 4 to 5
(`ConversationStore.cs:10`, migrations at `:66-81`). Empty means "the last
message in stored order", which is exactly what an unbranched conversation
means.

**`Conversation.Messages` stays the flat list of every message across every
branch.** The tree lives only in `ParentId`. This is the decision that
keeps the rest of the app working without learning about branches:
conversation FTS, Recall indexing, export and backup all iterate
`Messages` today and continue to see everything.

**Load-time backfill.** When a loaded conversation has more than one
message and every `ParentId` is empty, chain them in stored order: each
message's parent is the previous message's id. Deterministic, lossless, and
it makes a conversation written by 0.31.0 render identically in 0.32.0.

Do not backfill a conversation that already has any non-empty `ParentId`;
that one was written by this version or later and is already a tree.

## 1.2 Walking it

A pure static class in `Hermaeus.Core`, no store and no view model, so it
is directly testable:

```
ConversationTree.PathTo(messages, leafId)   // root to leaf, in order
ConversationTree.ChildrenOf(messages, id)   // ordered by CreatedAt, then Id
ConversationTree.Leaves(messages)           // messages with no children
ConversationTree.ResolveLeaf(messages, activeLeafId)  // falls back sanely
```

`ResolveLeaf` handles the three real cases: the id is empty (take the last
message in stored order), the id is missing because its subtree was deleted
(take the newest remaining leaf), and the id is valid (use it).

**`PathTo` must terminate on a cycle.** `messages_json` is a text blob in a
SQLite database on the user's own disk; it is editable, corruptible, and
syncable. A cycle in the parent chain must produce a truncated path and a
runtime log warning, never an infinite loop on the UI thread. Test it with
a deliberately cyclic fixture.

Chat renders `PathTo(Messages, ResolveLeaf(...))`. Everything downstream of
rendering (send, history truncation, token accounting, memory injection)
already operates on the rendered message list and needs no change. Confirm
that rather than assuming it: `TruncateHistoryToContextWindow` is called
with `Messages.Where(m => !m.IsStreaming)` at `ChatViewModel.cs:832-836`
and that call site becomes the active path.

## 1.3 Regenerate stops deleting

Regenerate now:

1. Finds the assistant message being regenerated and its parent user message.
2. Generates a new assistant message with the **same `ParentId`**, making it
   a sibling of the old one.
3. Sets `ActiveLeafId` to the new message.

Nothing is removed. `InputText` is not touched, so a half-typed next
message survives a regenerate, which it does not today.

Delete the old code path entirely. Do not keep it behind a setting; a
setting whose "off" position destroys data is not a preference.

## 1.4 Edit and resend

A pencil action on any user message, in the same footer row as Copy and
Speak (`MessageControl.axaml:155-175`).

- Editing opens the text inline, prefilled from `OriginalContent`
  (`Message.cs:6`) rather than `Content`, so the display-only attachment
  summary does not leak into the edit box. Regenerate already knows to do
  this (`ChatViewModel.cs:1217`); copy that, do not reinvent it.
- Sending creates a **new user message as a sibling** of the original
  (same `ParentId`), then generates its reply as that message's child.
  The original user message and its whole subtree are untouched.
- Attached files carry over by default, with a way to drop them before
  sending. Reuse `AddContextFilesAsync`, which regenerate already calls
  (`ChatViewModel.cs:1226`).
- Cancel restores the original text and leaves nothing behind.

Only user messages are editable. Editing an assistant message would mean
the transcript no longer records what the model said, which is a different
and much worse feature.

## 1.5 The switcher

On any message that has siblings, a compact control in the footer row:

```
‹ 2/3 ›
```

- Left and right move `ActiveLeafId` into the previous or next sibling's
  subtree, descending to the newest leaf under it (newest `CreatedAt`,
  tie-broken on ordinal id comparison so it is deterministic).
- Hidden entirely when a message has no siblings, which is every message in
  every conversation that has never been branched. An unbranched
  conversation must look exactly as it does in 0.31.0.
- The arrows read as icons, so they get tooltips ("Previous version",
  "Next version") and the axaml guard test will insist on it.
- The count is text, not a dropdown, not a menu, not a graph.

**Delete a branch** is an explicit action on the switcher's overflow, with
confirmation naming how many messages go. It removes the subtree rooted at
that sibling. It refuses when there is only one path left; deleting the
last branch is deleting the conversation, and there is already a way to do
that.

## 1.6 What a branch means to the rest of the app

The failure mode of adding a tree is that half the app keeps thinking in
lines. Each of these is a decision, not an oversight, and each gets a test.

| Subsystem | Sees | Why |
| --- | --- | --- |
| Conversation FTS (`ConversationStore.RebuildFtsAsync`) | All branches | A message you wrote must be findable. Searching and getting nothing because you branched away is a bug. |
| Recall indexing (r24) | All branches | Same reason. Recall is "your own words in this app", and an abandoned draft is still your words. |
| Export (`ConversationExportService`) | Active path by default | A transcript that silently interleaves three abandoned drafts is not a transcript. Offer full-tree export as an explicit option. |
| Memory extraction (`ConversationMemoryService`) | Active path only | Extracting a durable memory from an answer the user branched away from is the "silently reclassifying a user's records" failure r24 rejected outright. |
| Token and context accounting | Active path only | Already true once rendering changes; assert it. |
| Backup and restore | All branches | It is the same JSON blob; verify a branched conversation round-trips. |

## Testing

Roughly 22 to 26.

Pure tree functions carry most of it and need no database: path from leaf,
children ordering, leaf enumeration, `ResolveLeaf` for empty/missing/valid
ids, and the cycle guard terminating.

Then: the backfill turning a seeded 0.31.0 flat `messages_json` into a
correct chain; a conversation that already has parents not being
re-backfilled; regenerate producing a sibling and deleting nothing;
regenerate not clobbering `InputText`; edit-and-resend producing a sibling
user message with the original subtree intact; switching branches moving
the active leaf to the newest leaf of the chosen sibling; subtree delete
removing exactly the subtree; subtree delete refusing on the last path;
FTS finding a message on a non-active branch; memory extraction ignoring a
non-active branch; export defaulting to the active path; an unbranched
conversation rendering the identical message sequence before and after the
change.

No test needs a model, a network call or a running app.
