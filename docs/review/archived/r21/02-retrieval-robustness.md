# 02. Retrieval robustness

Doc 01 puts retrieval on a path where failure is routine (embedding server
not started, stopped mid-session, model unloaded) and must never surface as
a failed send. Today the pipeline is brittle exactly there.

## 2.1 Embedding failure falls back to BM25-only, with a planner note

Verified at spec time: `RetrieveAsync` calls
`await _embed.EmbedAsync(plan.PrimaryQuery, ct)` (RagQueryService.cs:246)
with no try/catch. An unreachable embedding server throws
(`HttpRequestException` or similar) and the whole query dies with a raw
error. The pipeline already contains the correct degraded path, built for
the embedding-model-mismatch case (RagQueryService.cs:226-243): skip the
semantic scan, run BM25 alone, append a planner note.

Change: wrap the embed call; on exception, take the same BM25-only path
with a planner note of the form
`"semantic search unavailable: <exception message, one line>; used keyword search only"`,
and log one Warning to runtime logs (category Rag). Do not retry, do not
cache the failure (the next query should probe again; server may have
started). Cancellation (`OperationCanceledException`) must NOT be swallowed
into a fallback; rethrow it.

Consequences to preserve:

- `WouldRefuse` with zero semantic candidates already handles the
  BM25-only shape (it requires no BM25 term match AND sub-threshold
  semantic score to refuse; verify against the implementation at
  RagQueryService.cs:655-661 and keep behaviour identical to the existing
  mismatch fallback).
- The RAG panel benefits identically: today a stopped embedding server
  makes the panel's query button produce a raw error message. After this
  change it produces a keyword-search answer with an honest planner note in
  the trace.
- `BuildQueryPlanAsync` runs before the embed call; if the planner itself
  embeds anything (verify), give it the same treatment or let its failure
  flow into the same fallback. The alias/variant expansion is
  deterministic and must keep working without embeddings.

Tests: fake embed seam that throws; assert (a) query completes, (b) planner
note present, (c) BM25 candidates non-empty for a keyword-matching corpus,
(d) cancellation still propagates, (e) a subsequent query with a working
embed seam is fully semantic again (no sticky failure state).

## 2.2 Best-effort contract on the chat send path

Doc 01.3 requires the try/catch; this item pins the matrix so it gets real
tests instead of an incidental catch block. For each scenario, a chat send
with a dataset attached must complete normally, produce exactly one Warning
log entry (or a trace note where stated), and inject nothing unless stated:

| Scenario | Expected |
| --- | --- |
| Embedding server down | Injection proceeds BM25-only via 2.1; trace `RagNote` carries the planner note |
| Dataset id resolves to no dataset (deleted since attach) | No injection; trace note "attached dataset no longer exists"; see doc 03.2 for the picker's honesty |
| Dataset has zero chunks | No injection; no warning spam (this is a normal state, one Info at most) |
| Store/DB throws (locked, corrupt) | No injection; one Warning; send completes |
| Retrieval slower than the send (no timeout today) | Acceptable for this round: retrieval is awaited pre-stream and its cost is visible as RagMs. No timeout/cancellation beyond the send's own token. Explicitly rejected: a background/racing retrieval design (doc 04). |
| Cancellation (user hits stop during pre-stream) | Propagates; no half-injected state |

Implementation note: distinguish "no injection because weak retrieval"
(Info-level trace note, expected daily) from "no injection because error"
(Warning). Never toast either; chat's surface for this is the trace and the
runtime log.

## 2.3 One dataset-list read per send, not per layer

`RetrieveAsync` loads the full dataset list to find one dataset's config
(RagQueryService.cs:224). Chat additionally needs the dataset (name for the
block header, config for UseParentChild) before calling it, which would
naively double the reads per send.

Change: add
`public async Task<RagDataset?> GetDatasetAsync(string datasetId, CancellationToken ct = default)`
on `RagQueryService` (or reuse if the store already exposes a single-row
read; check `SqliteRagStore`). Chat uses it once per send and passes what it
learned; `RetrieveAsync` keeps its own internal read (fine, it is one small
table). Do not build a cache; the table is tiny. This item is about the
public seam being right, not performance.

## Acceptance criteria

1. RAG panel query with embedding server stopped: answers via keyword
   search, planner note visible in the trace viewer, no raw exception text
   anywhere in the UI.
2. Every row of the 2.2 matrix has a test asserting the send completed and
   the stated log/trace outcome.
3. No behaviour change for the fully-healthy path: existing RAG tests pass
   unmodified (except where they asserted the old raw-exception behaviour).
