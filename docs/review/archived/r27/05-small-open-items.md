# 05. Small open items

Four items that are each too small for a document and too real to drop.

## 5.1 The conversation list stops reading every message

`ConversationStore.GetAllAsync` (`:145-156`) is `SELECT * FROM conversations`,
and `Map` (`:312-339`) turns each row into a full `Conversation`:

```csharp
Messages = JsonSerializer.Deserialize<List<Message>>(GetString(r, "messages_json")) ?? [],
...
ConversationTree.BackfillLinearChain(conversation.Messages);
```

Every message of every conversation is deserialised, and then walked again
to backfill parent links, in order to draw a sidebar that shows titles,
folders, tags, and pinned and archived flags. `SearchAsync` does the same on
every keystroke-driven reload, and `RunRecallStartupBackfillAsync`
(`MainWindowViewModel.cs:342-346`) does a second full pass at startup.

**Be honest about the size of this.** The owner's `conversations.db` is
60 KB. This costs nothing today. It is a cliff, not a stall, and it is in
this round because it is four lines of SQL away from never being one, not
because anybody has felt it.

- A `ConversationSummary` projection: id, title, model id, created and
  updated timestamps, folder, tags, pinned, archived, project id, RAG
  dataset id, recall-excluded. No `messages_json`.
- `GetAllAsync` and `SearchAsync` gain summary-returning counterparts, and
  the list view model binds to those. `GetByIdAsync` is untouched: opening a
  conversation genuinely needs its messages.
- The recall backfill takes the projection too, and reads full
  conversations only for the ids it is actually going to index.
- `ConversationTree.BackfillLinearChain` runs when a conversation is
  **loaded**, not when it is listed. Verify no list-path caller depends on
  it having run: r25 introduced it and the sidebar has no business knowing
  about branch structure.

FTS search keeps matching message text (`ConversationStore.cs:240-241`). The
projection changes what is returned, not what is searched.

## 5.2 A README that is nine releases out of date

`README.md:328` says:

```
**0.24.0-alpha**
```

`Directory.Build.props:3` says `0.33.0`. The gap opened at r24 and no
close-out since has closed it, on the front page of a repository that is
being prepared to go public.

r25 doc 05 added `DocsCoverageGuardTests` for exactly this class of drift,
and it passed the whole time, because it asserts that navigation panel
**names** appear in the README (`:53-60`) and never looks at the version.
The lesson is the general one: a guard covers what it asserts and nothing
else.

- Fix the README to the version this round ships.
- Extend `DocsCoverageGuardTests` to parse `VersionPrefix` out of
  `Directory.Build.props` and require it in `README.md`. Same file, same
  test class, same house style as the existing assertion.
- The failure message must say what to do, not just that it failed. The
  next person to hit it will be doing a version bump and should be told
  which line to edit.

This is deliberately mechanical. The owner has now raised README accuracy in
two consecutive rounds; a third reminder is not the fix.

## 5.3 Sequential post-setup timing, recorded honestly

Doc 01 1.5 surfaces the startup breakdown. One thing must be checked when it
lands rather than assumed: `StartupTimingFormatter.Format`
(`Hermaeus.Core/Services/StartupTimingFormatter.cs`) joins phases into a
single line, and its only current caller passes a list built in order.

Doc 01 makes three phases concurrent. Their durations will overlap and will
no longer sum to the total. The formatter is not wrong, but a reader adding
the numbers up will be. Label the concurrent block as one phase with its own
wall-clock duration, and record the individual store loads underneath it, or
do not record them individually at all. Do not emit three numbers that
appear to be sequential and are not.

This is a two-line concern that will otherwise produce a confusing screen
in doc 01 1.5 and a bug report against a number that is technically correct.

## 5.4 Docs

Per CLAUDE.md, and per r25's rule that documentation lands after the
behaviour exists rather than describing what is planned:

| File | What changes |
| --- | --- |
| `README.md` | Version (5.2). Major Features gains speculative decoding and the speed check. The known-issues section loses anything this round fixed |
| `docs/features.md` | Startup behaviour, the held message, speculative decoding, the speed check, per-model folders |
| `docs/rag.md` | The cache ceiling and what happens above it, FTS-backed candidate generation, the visible index size |
| `docs/benchmarks.md` | The speed check, what it measures, and the honest caveat about drafting gains varying by model pair and content |
| `CHANGELOG.md` | 0.34.0-alpha per the FIFO. Adding it pushes the oldest entry into `docs/changelog-archive.md`; check the ten-version limit |
| `docs/review/deferred.md` | Housekeeping, see doc 06 |

Run the doc-drift guard at the end. When editing `docs/rag.md` or
`docs/agent.md`, **verify with a full-file read**: a truncated check has
already let a dropped heading reach a live release in this repository once.
