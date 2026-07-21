# 05 - Roadmap, sequencing, tests, rejections

## Ship shape

- Version: **0.23.0-alpha** (bump in `Directory.Build.props` only,
  once doc 01 is settled - do not bump for the uncommitted diff alone
  if 01's fixes change its behavior first).
- Tests: doc 01 mostly repairs/replaces existing tests (no net-new
  count expected beyond a handful for the debounce/in-place-update
  behavior in 1.1). Docs 02-04 expect roughly 25-40 new tests: shard
  grouping in 3.2, memory/citation pill split in 3.3, any
  loosened-check tests in 2.1, `BuildLaunchArguments` emission and
  ExtraArgs-precedence cases plus KV-cache-type fit math in doc 04.
  All fake/fixture driven, no live model, no live HF network calls
  (existing HF client test doubles apply); the doc 04 flag
  verification (4.0) is a manual step against the bundled
  llama-server, recorded in the doc, not a test.
- Zero-warning build throughout; run the full suite before finishing -
  the working tree at the start of this round has 1 failing test
  (`VoiceTempFileCleanupTests`, see 01.5); that must be green before
  anything else lands.

## Sequencing

1. **Doc 01 entirely first.** Nothing in this round should be built on
   top of a keystroke-storm save bug or a stale doc comment describing
   the wrong contract. Resolve 1.3's design fork (upward context
   suggestion: keep or revert) before touching anything downstream of
   `SuggestContextSize`.
2. **2.1** (scenario check correctness) before **2.2** (view layout) -
   knowing whether the 3 failures are real logic bugs or harness
   artifacts changes whether 2.2's response panel needs to show
   anything beyond what it shows today.
3. **3.1** (HF scroll fix) is a pure XAML fix, no dependencies - do it
   any time. **3.2** (shard grouping) needs the "verify against the
   user's actual model directory" step in doc 03 before writing
   grouping logic - do not build regex-based grouping speculatively.
   **3.3** (memory pill collapse) is independent of 3.1/3.2.
4. **Doc 04 after doc 01's 1.3/1.4 are settled**: the KV-cache-type
   wiring (4.2) feeds the same `KvCacheMath`/`SuggestContextSize` path
   1.3 decides the contract for, so land the contract first. Within
   doc 04: 4.0 (flag verification) strictly first, then 4.1
   (`ServerConfig` + emission), 4.2 (fit math), 4.3 (preset helper),
   4.4 last.

## Docs to update when behavior lands

- `docs/features.md`: conversation auto-save behavior, Auto Tune
  context-suggestion direction (whichever 1.3 lands on), chat memory
  disclosure UI, shard-grouped model list entries if implemented, the
  new engine options and preset helper (doc 04) with their defaults
  and the nothing-is-forced rule.
- `docs/agent.md`: any Scenario check changes from 2.1, Agents view
  layout change from 2.2.
- `CHANGELOG.md` (keep the 10-version FIFO).

## Explicit rejections (do not implement, do not re-propose)

- **No LLM judge revival.** Standing since r7 (see every prior round's
  roadmap). `ServiceTests.cs:212-219` pins the contract.
- **No auto-save that skips debouncing.** 1.1 requires either an
  explicit-trigger save or a debounced timer; a raw per-keystroke save
  that also avoids the full-list-reload bug is still wrong because it
  writes to SQLite on every character typed - unnecessary I/O even if
  the reload bug is fixed separately.
- **No speculative shard-grouping regex without confirming the actual
  file pattern first** (3.2) - if the real cause of small model-list
  entries turns out to be something else, building shard-detection UI
  for a problem that doesn't exist is wasted surface.
- **No forced or default-on KV cache quantization, flash attention,
  or any other engine flag** (doc 04) - defaults stay byte-identical
  to v0.22.0-alpha; recommendations go through the preset helper the
  user explicitly clicks and can decline.
- **No `--host` other than 127.0.0.1 from any Aether-generated flag or
  preset** - localhost binding is a standing security invariant; the
  tuning guide's `0.0.0.0` example is explicitly not adopted.
- **No `--rpc` VRAM pooling this round** (doc 04, 4.5) - future-round
  candidate, needs its own security review (remote compute peer trust,
  failure handling) before any implementation.
- **No draft-model speculative decoding this round** (doc 04, 4.4) -
  second-model management and dual-model VRAM math are a round of
  their own; only the zero-VRAM n-gram variant may ship, and only if
  the shipped llama-server build verifiably supports it.
