# 03. Activity that links

## Why

Activity (r24) answers "did that actually work". It is a reverse
chronological list of deterministic facts about the app's own background
work, each with an explicit outcome, sharing the trace store rather than a
parallel one. The design decision that makes it trustworthy is stated in the
type itself (`ActivityModels.cs:14-18`):

> One deterministic fact the app observed about its own background work
> (doc 04 4.2) - **never a model-written summary**.

That stays. This document does not touch it, and anything in this round that
seems to want a sentence explaining *why* two rows are related is out of
scope rather than under-specified.

What Activity does not do is take you anywhere. A row saying a RAG ingest
partially failed does not open the dataset. A row saying a server crashed
does not open its log. The information needed to navigate is already sitting
in the record: `ActivityEvent.SourceId` (`ActivityModels.cs:24`) is populated
with the artifact's own identifier at every call site (`AgentService.cs:680`
writes the task id, `RagQueryService.cs:575` writes the dataset id,
`ChatTraceService.cs:77` writes the conversation id). Nothing reads it back
for navigation.

And the navigation primitive exists too. `RecallTarget`
(`RecallModels.cs:10-16`) is described as "a typed instruction rather than a
string so navigation can never point nowhere", and
`MainWindowViewModel.NavigateToRecallHitAsync` (`MainWindowViewModel.cs:257`)
already routes one to the right panel. The command palette has used it since
r24.

So Activity has the identifier, the app has the navigator, and the two have
never been introduced.

Separately, r24 shipped Activity's coverage as an admitted subset.
`features.md:909-911` says so, and `IActivityRecorder`'s own summary names
the full intended set: "managed servers, downloads, ingest, Doctor, backup,
memory sweeps". Downloads, backup and memory sweeps have never been wired.

## Work items

### 3.1 An Activity row knows where it points

Add a resolver that maps an `ActivityEvent` to a `RecallTarget?`, keyed on
`Operation` and using `SourceId`:

- agent task operations to `RecallTarget(TaskId: SourceId)`
- RAG ingest and refresh operations to `RecallTarget(DatasetId: SourceId)`
- chat operations to `RecallTarget(ConversationId: SourceId)`
- memory operations to `RecallTarget(MemoryId: SourceId)`
- anything else, and anything with an empty `SourceId`, to null

Pure, static, no I/O, in `Hermaeus.Core` beside the models it maps between.
The `Operation` strings are the ones call sites already pass; enumerate them
from the call sites rather than inventing a taxonomy.

A row whose resolver returns null shows no link. It does not show a disabled
link, and it does not guess.

Tests: each mapping; empty `SourceId` yields null; an unrecognised operation
yields null; the mapping is total over the operation strings actually in use
(a test that enumerates call sites and asserts each maps or is deliberately
listed as unmapped, so a new operation cannot silently lose its link).

### 3.2 The Activity view uses it

Rows with a resolved target become activatable, routing through the existing
`NavigateToRecallHitAsync` path rather than a second navigator.

The affordance is the row itself, not a new icon column. Rows without a
target stay inert and look inert.

Reuse, do not fork: if `NavigateToRecallHitAsync` needs a `RecallHit` and
Activity has an `ActivityEvent`, construct the hit at the call site or
factor the target-handling half of that method out. Two navigation
implementations that can disagree about where a task lives is the failure
this item exists to avoid, and r26 rejected an approval-history panel for
the same reason: a third rendering of the same thing.

### 3.3 The four missing event sources

Wire `IActivityRecorder` into the sources r24 named and did not reach:

- **Model downloads.** Started, completed, failed, cancelled, with the model
  name in the title. r27 4.1 made a download a file set, so a partial set is
  a `Partial` outcome and the reason names what is missing.
- **Backup and restore.** Both directions, with the outcome and, on failure,
  the reason `BackupService` already produces.
- **Memory auto-archive sweeps.** With the count archived, which r24 doc 04
  named explicitly ("memory auto-archive sweeps, with counts"). A sweep that
  archives nothing still records, because "it ran and found nothing" and "it
  never ran" are the two states this panel exists to separate.
- **The voice backend.** Start, stop, and failure of the managed voice
  process.

Each is one `RecordAsync` call at a point where the outcome is already
known. None of them needs new state, and none of them may change the
behaviour of the operation it observes: a recorder failure must never fail a
backup. `ActivityRecorder` already redacts before persisting
(`ActivityRecorder.cs:43` area), so no call site does its own redaction.

Tests: one per source asserting the row appears with the right outcome; a
failing recorder does not fail the underlying operation.

### 3.4 Adjacent rows are visibly adjacent

Group consecutive rows that fall within a short window (60 seconds is the
suggestion) under a single time heading.

This is arithmetic on timestamps. It says "these four things happened
together", which is a fact about the clock. It does not say they are
related, does not order them causally, and does not summarise them.

The reason this is worth doing at all: the owner's daily question is "did
that actually work", and the answer is often spread across three rows
written by three subsystems within the same few seconds. Putting them under
one heading is the entire deterministic content of what a synthesis feature
would have claimed, without the claim.

If this reads as decoration during implementation, descope it. 3.1 through
3.3 are the document.

## Deliberately out of scope

**A model-written summary of a group of events, or of anything.** Ruled out
by `ActivityModels.cs:14-18` and by r26's rejection of an LLM-written run
summary. The reason is worth restating because it will keep coming up: a
plausible wrong "why" is consumed faster than a right one and costs more,
and benchmark and timing data on a desktop under unknown load supports
almost no causal inference at all.

**Correlation, causation, or a "likely cause" field.** Same reason. 3.4
groups by clock time and says nothing about relationship.

**Alerts, badges, or notifications on failed activity.** Activity is a
record you consult, not a system that interrupts you. r26 rejected
auto-switching tabs when an approval arrives for the same reason.

**A new nav panel.** Activity already has its place.

**Retroactively linking existing rows.** The resolver is pure and runs at
display time against `SourceId`, which historical rows already carry. If an
old row's `SourceId` is empty, it shows no link, and that is correct.
