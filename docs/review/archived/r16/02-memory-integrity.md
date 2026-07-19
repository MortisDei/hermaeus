# 02: Memory integrity

Full audit of the chat memory subsystem: `MemoryStore`,
`ConversationMemoryService`, `MemoryExtractionService`,
`MemoryInjectionService`, the ChatViewModel injection path, and
`SqliteLessonStore` (which came out clean and is used below as the
reference implementation for two fixes).

## 2.1 Forget does not forget: archived memories keep getting injected

**Severity: high. This is the round's memory headliner.**

`MemoryStore.SearchAsync` is the recall path chat injection uses
(ChatViewModel.cs:977). Neither of its lexical branches filters archived
rows:

- FTS branch (MemoryStore.cs:352-358):
  `SELECT m.* FROM memories m JOIN memories_fts f ... WHERE memories_fts MATCH $q`
  has no `AND m.is_archived = 0`.
- LIKE fallback (`SearchLikeAsync`, MemoryStore.cs:772-781): no filter
  either.

Only the hybrid rerank's non-FTS embedding scan filters archived
(:427). Everything else in the store excludes archived by default
(`GetAllAsync`, `GetRecentAsync`, ...), so this is an oversight, not a
policy.

Consequence chain: `[MEMORY_FORGET: id]` "retires" a memory by setting
`is_archived = 1` (ConversationMemoryService.cs:74-78);
`ArchiveStaleMemoriesAsync` and the per-conversation cap do the same.
All of them keep resurfacing in injection whenever the query matches
lexically. The model is told "forget this", complies, and the fact
comes back next turn.

**Fix:** add `is_archived = 0` to the FTS join's WHERE and to
`SearchLikeAsync`. Archived rows stay in the FTS index (unarchiving
must not require an FTS rebuild); the filter belongs on the `memories`
side of the join.

**Acceptance:**
- Save a memory, archive it (directly and via the forget-marker path),
  then `SearchAsync` with an exact content term: not returned, in both
  the FTS branch and the LIKE fallback (force the fallback with an
  FTS-hostile query string).
- MemoriesViewModel's own search UI still shows archived entries only
  where it explicitly asks for them today (it lists via
  `GetAllAsync(includeArchived:)`, unaffected).

## 2.2 The [MEMORY: ...] save marker is a phantom feature; wire it up

`MemoryInjectionService.GetMemoryInstructionPrompt()`
(MemoryInjectionService.cs:48-80) - the instruction telling the model it
can save `[MEMORY: ...]` - **is never called anywhere in the solution**.
No live-chat path calls `ExtractMemoriesAsync` on responses either; the
only writer of new memories is the post-conversation auto-summary.
Meanwhile the marker is half-handled downstream, in the worst way:

- When at least one memory was injected this turn,
  `ApplyInjectedMemoryMarkersAsync` runs and its final
  `CleanMemoryMarkers` (MemoryExtractionService.cs:198-205) strips
  `[MEMORY: ...]` blocks from the response - **silently deleting** a
  save the model attempted, without saving it.
- When zero memories were injected (`injectedMemoryIds.Count == 0`,
  ChatViewModel.cs:565), nothing runs and raw marker syntax is
  displayed to the user and persisted into the conversation.

**Fix (wire it, don't delete it):** the injection block already teaches
UPDATE/FORGET markers, so completing the triad is the consistent move.

1. In the chat send path, when `Memory.Enabled`, append
   `GetMemoryInstructionPrompt()` to the system prompt alongside the
   injected-memories block (also when no memories matched - saving must
   not depend on recall having hits).
2. After the response completes, extract `[MEMORY: ...]` markers and
   save them through `ConversationMemoryService`'s existing
   `MergeAndSaveAsync` dedupe (make it internal-visible or add a public
   `SaveExtractedAsync`; do NOT bypass dedupe with raw
   `_memories.SaveAsync`, or repeated saves of the same fact will pile
   up rows). Cap per-turn saves at 3, matching the spirit of the
   auto-summary's cap of 5 per conversation pass.
3. Run marker cleanup (`CleanMemoryMarkers`) on every response when
   memory is enabled, regardless of whether anything was injected or
   extracted, so marker syntax never reaches the persisted transcript.
4. Toast/status: reuse the existing memory status line refresh
   (`RefreshMemoryStatusAsync`) so a turn that saved memories updates
   the count the user already sees.

**Acceptance:**
- With memory enabled and zero injected memories, a response containing
  `[MEMORY: user prefers X]` produces one new memory row and displays
  with the marker stripped.
- The same marker text twice across turns produces one row with
  `FrequencyCount == 2` (dedupe path exercised).
- With memory disabled, no instruction is sent, no extraction runs, and
  the response is displayed verbatim.

## 2.3 Enforce ExpirationDate (currently written, never read)

`Memory.ExpirationDate` is set by auto-summary when
`Memory.AutoArchiveAfterDays > 0` (ConversationMemoryService.cs:134-135),
persisted, loaded... and **no code path in the solution ever reads it**
(verified by grep: writes and mapping only). The
`AutoArchiveAfterDays` setting is a placebo.

**Fix:** enforce at the existing lifecycle point:
`ArchiveStaleMemoriesAsync` (MemoryStore.cs:587-618) archives any
non-pinned row whose `ExpirationDate` is in the past, in addition to its
current staleness rule. MemoriesViewModel already calls this on load
(MemoriesViewModel.cs:52), so no new trigger is needed. Belt and braces:
exclude expired rows from `SearchAsync` results at read time (cheap
in-memory filter after mapping), so an expired-but-not-yet-swept row
cannot inject.

**Acceptance:** a memory with `ExpirationDate` yesterday is archived by
the next `ArchiveStaleMemoriesAsync` pass and never returned by
`SearchAsync` even before that pass; a pinned memory with a past expiry
survives (pin wins, consistent with every other lifecycle rule).

## 2.4 Embedding model changes silently kill hybrid recall forever

Stored memory embeddings carry no model/dimension identity. After the
user switches embedding models (r13 made that a first-class flow),
every stored vector has the old dimensionality;
`CosineSimilarity` returns 0.0 on length mismatch (MemoryStore.cs:636)
so every semantic score is silently zero, and the backfill only touches
`embedding IS NULL` rows (:507) - **nothing ever re-embeds**. Hybrid
recall quietly degrades to FTS-only for the rest of time, indistinguishable
from working. RAG datasets got exactly this guard in r10
(`ReindexRequired`); memories.db got nothing.

**Fix:**
- Schema v5 (additive): `embedding_dim INTEGER` column, set on every
  embedding write (save-path embed, backfill).
- During `HybridRerankAsync`'s scan, count rows whose vector length
  differs from the query vector's. If any: log one Warning per process
  (same pattern as `LogQueryEmbedFallbackOnce`) naming the counts, and
  expose the count via a new `IMemoryStore.GetEmbeddingMismatchCountAsync`
  (or piggyback on the scan's result) for the UI.
- `MemoriesViewModel`: a status line ("N memories were embedded with a
  different model") plus a "Re-embed memories" button that clears
  mismatched embeddings (`UPDATE memories SET embedding = NULL WHERE
  length(embedding) != current-dim * 4`, computed against a live probe
  embed) and triggers `RunEmbeddingBackfillAsync`. Destructive only to
  stale vectors, and only on click - no automatic wipe.
- Do NOT add a Doctor check; per the standing rule, subsystem knowledge
  stays with the subsystem (the Memories page is the surface).

**Acceptance:** store rows embedded at dim A, switch the fake embedding
service to dim B: recall still returns FTS results (no throw, as today),
the mismatch count is exposed, re-embed clears and backfills to dim B,
and the warning logs exactly once.

## 2.5 Injection scoring ignores lifecycle decay

`MemoryInjectionService.EffectiveScore` (MemoryInjectionService.cs:122-125)
blends relevance with **raw** `ImportanceScore`, while the archiver uses
`MemoryLifecycle.ComputeEffectiveImportance` (30-day half-life). A
memory can therefore be one day from stale-archival yet still outrank a
fresh, recently-recalled one at injection time. Use
`MemoryLifecycle.ComputeEffectiveImportance(memory)` in place of
`memory.ImportanceScore` in `EffectiveScore`. Pinned rows are unaffected
(decay exempts them) and still sort first.

**Acceptance:** two memories with equal relevance and equal stored
importance, one recalled today and one untouched for 90 days: the fresh
one wins the budget cut. Existing injection tests still pass.

## 2.6 Verified non-issues (do not "fix" these)

- `SqliteLessonStore` came out clean: correct RoundtripKind parsing,
  sound reinforcement/contradiction/flip math, counter-only evidence
  handled. It is the reference for 1.7's date fix.
- `SaveAsync`'s COALESCE keeps an existing embedding when a save-path
  embed fails; combined with the backfill this is correct, not a bug.
- The auto-summary dedupe matching archived rows and leaving them
  archived (a re-learned fact stays forgotten) is intentional: forget
  wins until the user unarchives. With 2.1 fixed this becomes fully
  coherent.
- `HybridRerankAsync` hydrating up to 100 non-FTS rows per send is
  bounded and measured-fine at memory-store scale; no vector index, no
  cap change.
