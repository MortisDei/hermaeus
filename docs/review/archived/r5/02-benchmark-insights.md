# 02 - Benchmark Insights

Goal: turn the benchmark store into a recommendation engine. Entirely
deterministic aggregation over data already persisted; no LLM, no
network, no new database. Output shapes the user asked for:

- "You've run 84 benchmarks across 9 models."
- "For coding on your RTX 4060 Laptop (8 GB), these three models give
  the best quality per second."
- "Gemma E4B scores 4% lower than Qwen but runs 2.3x faster."

## Current state (verified)

- Runs persist as JSON rows in `benchmarks.db`
  (`src/Aether.Services/BenchmarkService.cs:56-67`) and mirror to
  `IEvalStore` (`BenchmarkService.cs:430`). `BenchmarkRun`
  (`src/Aether.Core/Models/BenchmarkModels.cs:35`) already computes
  QualityScore, StabilityScore, ResourceScore, RankingScore,
  tokens/sec, percentiles, and carries `HardwareSnapshot`
  (GPU name + VRAM via `GpuInfo`) and `Metadata` (quantization,
  backend, GPU layers, context size).
- `BenchmarkScoring.RankingScore` (`BenchmarkModels.cs:196`) is the
  single existing composite (quality .4, speed .3, stability .2,
  resource .1).
- **Gap**: `BenchmarkCase.Tags` (`BenchmarkModels.cs:32`) never reaches
  `BenchmarkResult` (`BenchmarkModels.cs:114` has no Tags), so
  per-task-category aggregation cannot be computed from a stored run
  alone.
- No leaderboard, comparison, or recommendation code exists anywhere.

## Items

### 2.1 Tag propagation

- Add `public List<string> Tags { get; set; } = [];` to
  `BenchmarkResult` and copy `case.Tags` into every result when
  results are built in `BenchmarkService.RunAsync`. JSON-additive:
  old rows deserialize with empty Tags, new suites round-trip.
- Backfill for old runs: when computing insights, if a run's results
  have no tags but its `SuiteId` still resolves via `GetSuitesAsync`,
  join tags by `CaseId`. If the suite or case is gone, the run
  contributes to overall aggregates but not per-tag ones. No database
  rewrite.
- Starter suites: ensure seeded suites tag their cases with a small
  controlled vocabulary (`coding`, `reasoning`, `rag`, `writing`,
  `safety`) so per-tag insights work out of the box. Tags remain
  free-form for user suites.

Acceptance criteria:

- New run's results carry the case tags.
- Old run + surviving suite: tags resolved via join in insights.
- Old run + deleted suite: appears in overall stats, absent from
  per-tag stats, no exception.

### 2.2 BenchmarkInsightsService

New `BenchmarkInsightsService` in `Aether.Services` (interface
`IBenchmarkInsightsService` in Core if the ViewModel is tested against
a fake; follow the existing service/DI pattern in
`AetherServiceRegistration`). Pure function of
`BenchmarkService.GetRunsAsync()` + `GetSuitesAsync()`; computed on
demand, no caching in v1.

Model grouping key: `ModelId` + `Metadata.Quantization` +
`Metadata.RuntimeKind` (a Q4 and a Q8 of the same model are different
candidates; same model on llama.cpp vs Ollama is too).

Hardware comparability: two runs are comparable when the primary GPU
name matches and total VRAM matches within 10%, or both ran CPU-only.
The "current hardware" reference is `ISystemInfoService`'s live
snapshot. Runs from other hardware are excluded from recommendations
and counted in a `staleOrForeignRuns` figure so the UI can say "12
runs from different hardware were ignored".

Output model (Core, all deterministic):

```csharp
public sealed record BenchmarkInsightsReport(
    int TotalRuns, int ComparableRuns, int ModelCount,
    DateTime? OldestComparableRun,
    IReadOnlyList<ModelAggregate> Models,
    IReadOnlyList<TagLeaderboard> TagLeaderboards,
    IReadOnlyList<ModelComparison> Comparisons,
    IReadOnlyList<string> Caveats);

public sealed record ModelAggregate(
    string ModelId, string ModelName, string Quantization, string RuntimeKind,
    int RunCount, int CaseCount,
    double QualityScore, double TokensPerSecond, double StabilityScore,
    double RankingScore, double QualityPerSecond, // quality * log2(1+tok/s), see below
    DateTime LastRunAt, bool IsStale);

public sealed record TagLeaderboard(
    string Tag, IReadOnlyList<ModelAggregate> Ranked); // top 3 by QualityPerSecond

public sealed record ModelComparison(
    string ModelA, string ModelB, string Tag,       // Tag empty = overall
    double QualityDeltaPercent,                     // + means A better
    double SpeedRatio,                              // A tok/s / B tok/s
    string Sentence);                               // "A scores 4% lower than B but runs 2.3x faster"
```

Rules:

- `QualityPerSecond = QualityScore * Math.Log2(1 + TokensPerSecond)`:
  log damping so a 2x speedup at equal quality wins, but speed cannot
  buy its way past a large quality gap. Constant documented next to
  `BenchmarkScoring.RankingScore` so there is one file of scoring
  truth.
- Minimum evidence: a model needs >= 2 comparable runs and >= 10
  scored cases total to appear in leaderboards or comparisons; below
  that it is listed under a "needs more data" caveat with the exact
  shortfall ("1 more run needed"). Never recommend from one run.
- Staleness: `IsStale` when the newest comparable run is older than 60
  days or its `Metadata.AetherVersion` major.minor differs from the
  current app version. Stale models stay ranked but the UI badges them
  and offers re-run.
- Comparisons: generated for the top 3 of each leaderboard pairwise
  (max 3 sentences per tag). Sentences use one decimal for the ratio
  and whole percent for quality: "Gemma E4B scores 4% lower than
  Qwen3 8B but runs 2.3x faster." Deltas under 1% say "matches".
- Aggregation across runs of the same model weights by case count
  (a 40-case run counts more than a 5-case smoke run).
- All math lives in a static, unit-testable class
  (`BenchmarkInsightsMath` or similar); the service is a thin loader
  around it, mirroring how `BenchmarkRun` keeps stats as computed
  properties.

Acceptance criteria (all as pure unit tests over synthetic runs):

- Grouping: same ModelId different quantization = two aggregates.
- Hardware filter: run with different GPU name excluded from
  recommendations, counted in caveats.
- Min-evidence: model with 1 run never appears in a leaderboard.
- QualityPerSecond ordering: (q=.80, 20 tok/s) outranks (q=.84,
  6 tok/s); (q=.85, 15 tok/s) outranks (q=.40, 80 tok/s).
- Comparison sentence for q A=.80 B=.833, tok/s A=46 B=20 renders
  "scores 4% lower" and "2.3x faster" exactly.
- Weighted aggregation: 40-case run at q=.9 plus 10-case run at q=.5
  yields q=.82.

### 2.3 Insights panel in the Benchmark page

`BenchmarkViewModel` (`src/Aether.ViewModels/BenchmarkViewModel.cs`)
gains an Insights section, loaded on demand (button or tab, not on
page open; 84 runs of JSON deserialization should not tax page
navigation):

- Header line: "You've run {TotalRuns} benchmarks across {ModelCount}
  models on this hardware."
- Per-tag leaderboard cards: tag name, top 3 models with quality,
  tok/s, and stale badge; a "Best overall" card ranked by
  QualityPerSecond.
- Comparison sentences under each card.
- Caveats list (needs-more-data models, foreign-hardware run count).
- Each ranked model row offers "Re-run benchmark" pre-selecting that
  model and the relevant suite (wire to the existing run command).
- Empty state (no comparable runs): explain what to run first, offer
  the starter suites.

Acceptance criteria:

- ViewModel test with a fake insights service: report renders rows,
  empty report renders empty state, load command sets busy flags
  correctly (follow existing `BenchmarkViewModel` test patterns).

### 2.4 Active-model advisory check

`DoctorService` already implements `IInspectionCheckProvider`
(`src/Aether.Services/DoctorService.cs:15`). Add one Info-severity
inspection check (new small provider or extend an existing
registration point, whichever the inspection wiring makes cheaper):
when insights hold a leaderboard and the currently selected chat model
ranks below the top model overall by more than 10 RankingScore points
(0-100 scale: compare `RankingScore * 100`), report "Benchmark data
suggests <model> may serve you better overall" with the two scores.
Info only; never Warning/Error; never auto-switches anything.

Acceptance criteria:

- Current model = top model: no check emitted.
- Current model 15 points behind: Info check with both names.
- No comparable data: no check, no error.

## Deferred (recorded here so r6 does not re-litigate)

"Based on your usage history, switch to Granite 9B for RAG tasks"
requires knowing which task types the user actually performs, and no
per-feature model-usage counters exist today. Deferred to a future
round as a small local `model_usage` counter table (feature area x
model x count) that insights could join against. Not rejected, just
unblocked by data we do not yet collect. Everything else in this doc
works without it.
