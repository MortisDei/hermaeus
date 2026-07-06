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
3. Move the RAG eval harness (adds the retrieval score provider).
4. Retire duplicated export/ranking code paths.

Each step is independently shippable and each strictly deletes code after it
lands. Target: early 1.x, after the check/fix registry, before unified
memory (it is lower risk than either and builds confidence in the pattern).
