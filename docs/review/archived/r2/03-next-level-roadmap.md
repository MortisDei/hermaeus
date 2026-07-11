# Next-Level Roadmap (r2)

r1's roadmap is closed. This one starts from the audit (doc 01) and the
items r1/v3.0 explicitly deferred, sequenced so each phase makes the next
one cheaper. Same principle as r1: composition over new verticals; features
appear only where an architectural change makes them nearly free.

## Phase 0 - Audit fixes (do first, small, independent) - DONE

Every P1/P2/P3 item in doc 01 landed, each with a test in the matching
existing test file. Two items surfaced additional fixes beyond the original
audit while implementing: P1-2's fix (route trace `DetailJson` through
`JsonSerializer.Serialize`) also folded in P3-5 (call the shared
`RagStreamProtocol` parser instead of a third private copy) in the same
change. P3-3 turned out to already be a non-issue on inspection
(`WorkspaceCommandRecipes.Executable` already used an `OrdinalIgnoreCase`
comparer, so the gate and executor already agreed); a regression test locks
that invariant in rather than "fixing" something that wasn't broken.

## Phase 1 - Provenance convergence (the deferred half of "provenance everywhere") - DONE

1. **RAG citations move to `SourceReference`-adjacent typed events - DONE.**
   `RagStreamProtocol`'s sentinel-string parser is deleted; `RagQueryService.StreamQueryAsync`
   now yields `RagStreamEvent` (`Token`/`Sources`/`Trace` via `Kind`, with a
   typed `RagTraceSummary` payload) instead of magic-prefixed strings.
   `RagViewModel`, `RagEvalService`, and `LocalApiEndpoints` all converted.
   Bonus fix found while converting: the trace event now actually carries
   `ExpandedQuery`/`QueryVariants`/`PlannerNotes`/`ContextPackingSummary`,
   which the persisted trace always computed but the old sentinel JSON never
   included in the live stream, so those Context Transparency fields in
   `RagViewModel` were silently always empty.
2. **Memory grows a structured source - DONE.** `MemoryStore` schema
   migration v3 (additive, `source_json` column). `MemoryExtractionService`
   populates a `SourceReference` with a content-derived title and the
   conversation locator (the "message locator" language above overstated
   what the extraction call site actually receives; it only ever had the
   conversation id, so that is what the locator carries). Legacy rows
   backfill a `SourceReference` from `SourceConversationId` at read time, no
   data rewrite.
3. **Chat consumes citations - DONE, plus the memory-injection wiring itself.**
   Turned out "chat consumes citations" wasn't pure composition: memory
   injection (`MemoryInjectionService`) existed but nothing in `ChatViewModel`
   ever called it (confirmed zero consumers before this pass), so this phase
   also had to wire the injection itself (search relevant memories, budget,
   append to the system prompt), not just render a panel over an existing
   flow. `MessageViewModel.Sources` + a chip row in `MessageControl.axaml` is
   the panel. RAG-in-chat and agent-tool-result citations are not wired into
   this panel (chat still doesn't call RAG directly); that remains future
   composition once chat gains a RAG-attachment path.

## Phase 2 - Local API: from demo to substrate - DONE

1. **Streaming - DONE.** `"stream": true` on `/v1/chat/completions` returns
   SSE in the OpenAI `chat.completion.chunk` shape. Buffered JSON stays the
   default.
2. **Per-app tokens - DONE.** `LocalApiSettings.Tokens` (named entries)
   replaces the single shared token; Settings > Local API adds/revokes with
   each action applying immediately. `LocalApiTokenAuth` records the
   matched entry's name; `LocalApiEndpoints` traces under that verified
   identity, with `X-Aether-Client` demoted to a recorded-but-unverified
   hint. A load-time migration converts an existing single token into a
   "Default" named entry. Not done: a last-used timestamp per token was
   considered and dropped deliberately - the trace store already answers
   "when was this token last used" via its per-caller history, so a second,
   settings-file-persisted timestamp would just be a second source of truth
   for the same fact, updated on every request at extra I/O cost.
3. **Sampling/profile parity - DONE** (closed in Phase 0 as audit P3-1,
   since it was already fully scoped there) **and an embeddings endpoint - DONE**
   (`POST /v1/embeddings`, wraps `IEmbeddingService`).
4. **Agent run/step endpoints - still explicitly deferred**, unchanged from
   the original reasoning: no non-interactive approval story yet.

## Phase 3 - MCP hardening - DONE (breadth still deferred)

- **Per-server tool allowlists - DONE.** `McpServerConfig.AllowedTools`
  (Settings > MCP Servers), empty = unrestricted, matching prior behavior.
  The client robustness half of this phase (drain stderr, fail fast on
  server death, preserve argument JSON types) was originally scoped here
  but actually landed in Phase 0 as audit P2-1/P2-2/P2-3, since those were
  concrete enough to fold into the audit fix pass rather than wait. Building
  the allowlist surfaced one more real gap beyond what was planned:
  `McpToolBridge.ExecuteAsync` never checked a tool name against what the
  server actually declared via `tools/list` before forwarding it; it now
  does, as defense in depth alongside the allowlist. Still no HTTP/SSE
  transport; still no `run_command` recipe growth beyond the fixed
  allowlist; workflow composition remains deferred pending real
  `run_command` usage telemetry, per r1 Opportunities #9 - none of that
  changed this round.

## Phase 4 - Product hardening (pre-1.0-rc) - crash journal DONE, the other two are standing practice, not code

- **Crash evidence, local-only - DONE.** `AppLifecycleJournalService`
  (`Aether.Core.Services`, not `Aether.Services`, so `Aether.Rag` and
  `Aether.Voice` can both depend on it without a reference against the
  established dependency direction) generalizes the Kokoro-specific
  preflight log into one small atomic-write JSON journal (session start,
  clean-exit flag, last operation). Wired into the RAG cross-encoder
  reranker's two ONNX session loads and into `App.axaml.cs` startup/shutdown.
  Kokoro's own `kokoro_native_install.log` preflight logging is left as-is
  (more detailed, already shipped and tested for that exact crash) rather
  than replaced. Doctor gained a "Previous session exited cleanly" check
  naming the last recorded operation when it wasn't.
- **First-run experience audit - not done, and can't be from this seat.**
  This is a manual walk on a clean VM, not a code change; noted here so it
  isn't silently dropped, but nothing to implement.
- **Suite time guard - not done as automation.** Also not a code change by
  itself (recording current CI wall-clock and deciding a threshold is a
  process/CI-config decision). Current full local run is ~25-27 seconds for
  263 tests as of this pass, well under the ~3 minute watch threshold in the
  assessment doc; revisit when it approaches that, not before.

## Rejected this round

- **Splitting Doctor/Setup/Benchmark further.** Content-heavy, registry
  pattern already in place; line count is no longer the metric (assessment
  item 1).
- **OpenAI-compatible surface for the whole local API.** Full compatibility
  (models/embeddings/completions shapes) invites clients we do not control
  sending shapes we do not support; we borrow the SSE chunk format for
  streaming and keep our own minimal contract elsewhere.
- **A plugin/extension API.** Still no. MCP is the extension surface.
- **Multi-machine sync.** Still deferred; user-owned file sync of the data
  root remains the design sketch, and nothing this round makes it cheaper.

## Definition of done for r2

Phase 0 fully landed; Phases 1 and 2 landed in order; Phase 3's hardening
half landed; Phase 4's crash journal landed, its other two items are
standing practice, not code. **All of the above is now DONE** as of
`0.9.42-alpha`. The project is ready for a 1.0-rc versioning conversation,
and the next review (r3) should be run against real usage traces rather
than code alone - in particular, `run_command` trace history (Phase 3) is
the concrete signal to watch for before considering workflow composition.
