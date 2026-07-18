# 03 - Roadmap, sequencing, tests, rejections

## Ship shape

- Version: **0.22.0-alpha** (bump in `Directory.Build.props` only).
- Tests: expect roughly **30-40 new** from the current 884. All fake
  driven; no live server, no real model files (GGUF fixtures are
  hand-written bytes in temp files).
- Zero-warning build throughout; run the full suite before finishing.

## Sequencing

1. **1.1 GgufMetadataReader + 1.2 KvCacheMath** first; everything else
   in both docs consumes them.
2. **1.3 fit estimator**, then **1.4 Services warning**, then
   **1.6 training-context advisory** (it rides 1.4's note), then
   **1.5 Auto Tune** (largest surface, lands on a proven estimator).
3. Doc 02 in order **2.1 -> 2.2 -> 2.3** (they all touch
   `FillTiming`/`FillResources`/scoring and should be one coherent
   change to the result pipeline), then **2.4** (needs 1.1 for
   quantization), then **2.5**, **2.6** (reproduce first), **2.7**.

## Docs to update when behavior lands

- `docs/benchmarks.md`: measured-vs-estimated tok/s, neutral resource
  slot, metadata fields, cold semantics, no more dropdown restarts.
- `docs/features.md`: hardware-aware context fit warning, Auto Tune
  context behavior, fit chips with KV breakdown.
- `CHANGELOG.md` (keep the 10-version FIFO).
- `docs/security-review.md`: new subsection for the GGUF header parser
  as a parser over untrusted downloaded files: bounded allocations,
  structural bounds checks, null-on-failure contract, fixture/fuzz-style
  malformed-input tests, metadata-only (tensor data never read).

## Explicit rejections (do not implement, do not re-propose)

- **No LLM judge revival.** Standing since r7. The `UseJudge`/
  `JudgeModelId`/`JudgeScore` fields stay as round-trip-only storage;
  `ServiceTests.cs:212-219` pins that contract. Do not remove the
  fields either.
- **No automatic context or layer changes outside the user-clicked
  Auto Tune command.** The 1.4 warning and 1.6 advisory never edit
  values, never block Start.
- **No background VRAM polling or live GPU monitoring service.**
  `HardwareProfile` stays a process-lifetime snapshot.
- **No GGUF tensor-data reading, ever.** Header metadata only.
- **No new NuGet packages.** The GGUF reader is internal.
- **No remote GGUF header fetches.** HF browser and wizard fit chips
  stay size-based; only local files get KV-aware fit.
- **No migration or rescoring of stored benchmark runs.** Old runs keep
  their persisted per-result numbers; the only load-time touch is the
  2.4 `RuntimeKind` normalization for insights grouping, which does not
  write back to the store.
- **No change to chat's prompt caching.** `cache_prompt: true` remains
  the chat-path default; `DisablePromptCache` is benchmark-only in
  practice.
- **No 2D exhaustive Auto Tune search.** Context tuning adds at most
  one probe (the suggested-context candidate) before the existing layer
  descent; do not iterate the full ladder against live processes.
- **No per-model context sliders on the Benchmarks page** and no other
  new benchmark configuration surface this round.
