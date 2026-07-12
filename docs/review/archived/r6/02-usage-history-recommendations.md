# 02 - Usage-History Recommendations

The item r5 deferred (`docs/review/archived/r5/02-benchmark-insights.md`,
"Deferred" section): "Based on your usage history, switch to X for RAG
tasks" needs per-feature model-usage data that did not exist. r6 adds it.

## Current state (verified)

- The shared trace store already records exactly the right envelope per
  call: `TraceRecord` carries `Kind` (Chat/Rag/Agent/LocalApi), `ModelId`,
  `Operation`, timing, and token counts
  (`src/Aether.Core/Models/TraceRecord.cs:16-41`).
- But traces are count-capped at 500 per kind, pruned on every append
  (`src/Aether.Services/SqliteTraceStore.cs:14,107-115`), so raw traces
  cannot serve as durable usage history. A busy week of chatting erases
  last month's pattern.
- `BenchmarkInsightsMath.BuildReport` already produces per-tag leaderboards
  and takes explicit inputs, so it can accept usage data as another
  parameter without touching storage
  (`src/Aether.Core/Models/BenchmarkInsightsModels.cs:123`).

## Items

### 2.1 model_usage rollup table

Add a `model_usage` table to the existing traces database, maintained by
`SqliteTraceStore.AppendAsync` in the same transaction as the insert and
never pruned:

- Columns: `kind TEXT`, `model_id TEXT`, `day TEXT` (UTC yyyy-MM-dd),
  `call_count INTEGER`, `total_tokens INTEGER`, primary key
  (kind, model_id, day). Upsert increment per append.
- Skip rows with empty `ModelId` (some LocalApi/agent operations).
- Schema versioned through the existing `aether_schema_versions`
  mechanism, additive migration only (see `storage-and-data-root` skill).

Acceptance criteria:

- Two appends same kind/model/day = one row, count 2, tokens summed.
- Append with empty ModelId creates no row and no error.
- Pruning traces does not touch model_usage.
- Pre-r6 traces.db migrates in place; existing traces still readable.

### 2.2 IModelUsageService

Small read service (Core interface, Services implementation, registered in
`AetherServiceRegistration`) exposing:

- `GetUsageAsync(TraceKind? kind, int days)` returning per-model call and
  token totals over the window.
- A convenience shape for insights: for each kind, the dominant models
  with share of calls (e.g. Chat: 78% model A, 22% model B).

Acceptance criteria:

- Window filtering respects the day column, UTC.
- Empty table returns empty results, no exception.

### 2.3 Usage-aware insights

Extend `BenchmarkInsightsMath.BuildReport` with an optional usage input
(pure data, keeping the math class deterministic and unit-testable):

- New report section `UsageInsights`: for each TraceKind with >= 20 calls
  in the last 30 days, one sentence naming the user's dominant model for
  that activity and, if a tag leaderboard relevant to that activity exists
  (Chat -> overall, Rag -> `rag` tag, Agent -> `coding` tag) and its top
  model differs from the dominant model by more than 10 RankingScore
  points (same threshold as the r5 doctor advisory), a recommendation
  sentence: "You mostly use <A> for RAG queries; your benchmarks rank <B>
  higher for rag tasks."
- Below 20 calls in a kind: that kind is silent. Never recommend from
  benchmark data alone here; that is the r5 advisory's job.
- Mapping from TraceKind to tag is a named constant table next to the
  other scoring constants, one file of scoring truth as in r5.
- UI: one "Based on your usage" card in the existing Insights tab, only
  visible when at least one UsageInsight exists.
- Doctor: extend the existing r5 advisory in `DoctorService.ScanAsync` to
  include at most one usage-aware sentence when the same conditions hold.
  Info severity only, never auto-switches, matching the r5 rejection list.

Acceptance criteria (pure unit tests over synthetic usage + runs):

- 19 chat calls: no chat usage insight. 20: insight present.
- Dominant model = leaderboard top: descriptive sentence only, no
  recommendation.
- 15-point gap: recommendation sentence rendered with both model names.
- No benchmark data at all: usage sentences still describe usage, no
  recommendation, no exception.

### 2.4 Privacy Audit line

Usage counters are local aggregation, but they are new persistent
behavioral data. Add one Privacy Audit item: "Model usage counters:
per-feature daily counts stored locally in traces.db; never transmitted."

Acceptance criteria:

- Item appears in Privacy Audit with the rest (no toggle in v1; the data
  is the same class as the traces the audit already discloses).
