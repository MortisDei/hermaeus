# Evaluation System — design note (1.x)

Benchmarks, Compare Models, and the RAG eval harness are one question asked
three ways: *"how good is this model or pipeline at this job, on my
hardware?"* This note defines the single system they become projections of.
Until this lands, the standing rule from the system map applies: **none of
the three may grow features independently.**

## Shared data model

```
EvalCase   { id, prompt, systemPrompt?, expectations }   // expectations: keywords, expected_sources, should_refuse, deterministic checks
EvalTarget { modelId | pipeline (model + dataset + retrieval settings) }
EvalRun    { id, caseResults[], target, startedAt, finishedAt, mode }
CaseResult { caseId, output, latencyMs, firstTokenMs, usage, scores{} }
```

This generalizes what already exists: benchmark suites are `EvalCase[]` with
deterministic checks; RAG eval files are `EvalCase[]` with retrieval
expectations; Compare Models is an ad-hoc single `EvalCase` against 1-4
targets.

## One engine, three projections

- **Quick compare** (today's Compare Models): one case, N targets, transient
  run (not saved) unless the user pins it.
- **Suite run** (today's Benchmarks): many cases, one target, saved run,
  rankings derived from stored `EvalRun`s.
- **Retrieval eval** (today's RAG eval harness): cases with retrieval
  expectations; scores include Recall@K, MRR, citation hit rate, refusal
  accuracy.

Execution, cancellation, timeout handling, latency measurement, usage
capture, storage (one `eval_runs` store with the existing benchmark
migration path), and export (MD/JSON/CSV) are written once. The three panels
become views that construct cases/targets differently and read different
score columns.

## What gets deleted

- `BenchmarkService`'s bespoke run/save/export plumbing (largest file in the
  repo) shrinks to suite management + score functions.
- Compare Models' private execution loop in `ChatViewModel`.
- The eval harness's own runner and export.

## What stays deliberately separate

- Deterministic quality checks (benchmark scoring) and retrieval metrics are
  score *providers*, not engine features — pluggable functions over
  `CaseResult`.
- GGUF discovery, auto-tune, and Doctor checks are runtime concerns, not
  evaluation concerns.

## Sequencing

1. **DONE.** Introduce the shared models + storage alongside the benchmark
   store (additive migration; benchmarks read/write through the new shapes).
   `EvalCase`/`EvalTarget`/`EvalRun`/`CaseResult` live in `Aether.Core`;
   `SqliteEvalStore` (`eval_runs.db`) is additive; `BenchmarkService` projects
   each saved run through `BenchmarkService.ToEvalRun` and writes it to both
   stores. No UI reads the new store yet.
2. **DONE.** Move Compare Models onto the engine (smallest surface, proves
   the transient-run path). `IEvalEngine.RunQuickCompareAsync` executes one
   case against N targets, sequentially, returning one transient `EvalRun`
   per target; `ChatViewModel.CompareSelectedModelsAsync` maps those runs
   onto the existing `ModelCompareResultViewModel` UI. Nothing is persisted
   (matches prior behavior); there is no pin-to-save affordance yet.
3. **DONE.** Move the RAG eval harness onto the shared shape. `RagEvalService.RunAsync`
   still runs retrieval/full-answer cases and writes its own `run.jsonl`/`report.md`
   (unchanged), but now also projects the run through `RagEvalService.ToEvalRun` and
   writes it to `IEvalStore`. Retrieval metrics (Recall@K, MRR, citation hit,
   unsupported answer, refusal accuracy, grounding, reranker delta) become score
   *providers*: entries in `CaseResult.Scores`, not engine features. No UI reads the
   new store for RAG eval yet.
4. **DONE, scoped.** Retired the one genuine duplicate: both exporters carried
   their own copy of a write-to-temp-then-rename helper (`BenchmarkService`
   had a private `WriteTextAtomicAsync`; `RagEvalService` had no atomic write
   at all, a latent partial-write bug on crash). Both now share
   `Aether.Core.Services.AtomicFile.WriteAllTextAsync`. Ranking has exactly
   one implementation (`BenchmarkService.Rank`, used only by
   `BenchmarkViewModel`), so there was nothing to retire there.

   What this step did **not** do, and why: doc 10's "What gets deleted"
   section describes shrinking `BenchmarkService`'s bespoke run/save/export
   plumbing and deleting the RAG eval harness's own runner/export entirely.
   That is not safe yet, because after steps 1-3 nothing reads `EvalRun`s
   back out of `IEvalStore` — Benchmarks and RAG eval both still write to the
   shared store, but the Benchmarks and RAG eval panels still read/render
   from their own richer, system-specific shapes (`BenchmarkRun` carries
   hardware snapshots, percentiles, and stability scores; `RagEvalResult`
   carries per-chunk retrieval detail) which the generic `CaseResult.Scores`
   dictionary does not hold. Deleting either store now would silently break
   its panel. Full retirement needs a reader (a shared history/compare view
   built on `IEvalStore`) before the old stores can be deleted; that is new
   feature work, not a refactor, and is not scoped by this design note.

Each step is independently shippable and each strictly deletes code after it
lands. Target: early 1.x, after the check/fix registry, before unified
memory (it is lower risk than either and builds confidence in the pattern).
