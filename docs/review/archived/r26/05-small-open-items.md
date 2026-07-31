# 05. Small open items

Three items that are individually too small to lead a round and have
therefore been postponed repeatedly. Two of them come off `deferred.md`.

## 5.1 The local API says what this instance can do

Deferred by r1, still open at 0.32.0. `LocalApiEndpoints.MapLocalApiEndpoints`
(`src/Hermaeus.LocalApi/LocalApiEndpoints.cs:31-139`) serves:

| Route | Line |
| --- | --- |
| `GET /health` | 35 |
| `POST /v1/chat/completions` | 37 |
| `GET /v1/memory/query` | 72 |
| `POST /v1/rag/query` | 93 |
| `GET /v1/models` | 124 |
| `POST /v1/embeddings` | 139 |

A caller can ask what models exist. It cannot ask whether RAG has any
dataset, whether memory is enabled, or whether embeddings will work, so the
only way to find out is to send a real request and read the failure.

**Add `GET /v1/capabilities`**, authenticated like every route except
`/health` (see the `LocalApiTokenAuth` comment at `:33-34`), returning a flat
JSON object of what this instance can currently serve:

- The routes it exposes.
- Per feature, whether it is usable right now and, when it is not, one
  sentence saying why. Chat: is a model selected and reachable. RAG: are
  there datasets, and how many. Memory: is the memory store enabled.
  Embeddings: is an embedding model configured.
- The Hermaeus version, from the same assembly informational version the rest
  of the app reads.

**Three constraints.**

1. **It reports, it does not probe.** No model load, no server start, no
   network call, no embedding pass. It reads settings and store counts that
   are already in memory or a cheap query away. A capabilities endpoint that
   takes ten seconds and warms a GPU is a denial-of-service handle.
2. **It leaks nothing.** No file paths, no API keys, no secret-store
   references, no token values, no dataset contents. Dataset *count*, not
   dataset names, unless a name is already exposed by `/v1/models`-style
   surface. Everything goes through `RedactionService` before it is logged,
   as the other routes already do.
3. **It is traced like the others.** Every existing route takes `ITraceStore`
   and records the caller from `Caller(http)` (`:39`). This one does the
   same.

Tests: the shape round-trips; an instance with no datasets reports RAG
unusable with a reason rather than omitting the key; an unauthenticated
request is rejected; no settings value that could be a secret appears in the
response body for a settings object that has one populated.

## 5.2 The clock-dependent test that has actually failed

Deferred by r25 5.4. That item covered three cases; two of them are races in
principle that have never been observed to fail, and they stay deferred with
that reasoning intact. One has been observed to fail:

`ServicesViewModelTests.Removing_a_managed_server_disposes_its_view_model`
(`src/Hermaeus.Tests/ServicesViewModelTests.cs:241`) waits on a `Rebuild`
that `SettingsChanged` posts through `RunOnUi`, which is fire-and-forget by
design in production. Under full-suite load the wait can expire.

**The fix is to drain, not to wait longer.**
`Helpers.QueueingSynchronizationContext.DrainAll`
(`src/Hermaeus.Tests/Helpers.cs:187`) exists for exactly this and is already
used this way at `src/Hermaeus.Tests/LogsViewModelBatchingTests.cs:49`.
Install the queueing context for the test, trigger the change, drain, then
assert. The assertion becomes deterministic rather than probable.

Widening the timeout is explicitly rejected. It converts an occasional red
into a slower occasional red and hides the design fact that the work is
posted rather than awaited.

Leave the other two r25 5.4 cases (`MainWindowViewModelStartupTests`'s 150ms
debounce assertion and `McpTests`'s 5000ms bound) alone, and update their
`deferred.md` row to say that one of the three closed and why the other two
did not.

## 5.3 Documentation

Behaviour changed in four places this round, so four docs change.

- **`docs/agent.md`.** The panel it describes is being restructured (doc 02)
  and the review queue's semantics change (doc 01). Two sections need real
  edits rather than a touch-up: the workbench description, and whatever the
  document currently says about the review queue listing tasks with approval
  history. Also fold in doc 03's capability derivation, replacing any prose
  that restates the old hardcoded five lines. The document is 704 lines with
  26 headings; **read it whole before editing it.** A truncated check has
  already let a dropped heading reach a live release once in this
  repository.
- **`docs/benchmarks.md`.** Doc 04's cross-suite ranking, including the
  method (mean per-suite standing) and the conditions under which there is
  deliberately no answer. State the method, not just the result: r25's
  benchmarks doc set that precedent and it is why the numbers are trustable.
- **`docs/features.md`** and **`README.md`.** Per r25 5.1's guard, which now
  exists and now runs. The README's feature narrative is the thing that
  historically gets skipped at close-out; it is named here so it cannot be.
- There is no `docs/local-api.md`; 5.1's new route is documented in
  `docs/features.md`'s "Local API" section (`:914-939`), alongside the routes
  already listed at `:937-939`.
- **`CHANGELOG.md`** per the FIFO with `docs/changelog-archive.md`, ten
  versions maximum in the live file. The file holds exactly ten entries
  today (0.32.0 down to 0.26.0), so adding 0.33.0 **will** push 0.26.0 out.
  Move it to `docs/changelog-archive.md`; do not just delete it, and do not
  quietly leave eleven.
- **`docs/review/deferred.md`**, per 06's housekeeping.
