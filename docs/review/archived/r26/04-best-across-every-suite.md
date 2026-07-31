# 04. Best across every suite

## The deferred item, restated

From `docs/review/deferred.md`, deferred by "r25 follow-up (owner request)":

> The owner asked for a first column naming the model that performed best
> across **all** suites, with per-suite scores on click, alongside renaming
> Rankings to Per-Suite Rankings. The rename shipped; the column did not.

r25 shipped the hard half of the honesty argument that this item depends on.
`BenchmarkInsightsReport.BestOverall`
(`src/Hermaeus.Core/Models/BenchmarkInsightsModels.cs:83-84`) is now null
unless there is a shared case set to rank on, `ComparisonBasisCaseCount`
(`:69`) states how big that set is, `ModelAggregate.Cases` (`:25`) carries
the per-case drill-down, and the Per-Suite Rankings tab
(`src/Hermaeus.Desktop/Views/BenchmarkView.axaml:194`) now says in its own
header text that a cross-suite verdict lives on the Insights tab.

What is still missing is the thing keyed by **suite**. The report has an
overall board keyed on a shared case set, and per-**tag** leaderboards
(`TagLeaderboard`, `:44`). Nothing groups by suite, even though every run
already carries `SuiteId` and `SuiteName`
(`src/Hermaeus.Core/Models/BenchmarkModels.cs:38-39`) and
`BenchmarkInsightsService.cs:90` already looks runs up by `run.SuiteId`.

## 4.1 Per-suite leaderboards

Add `SuiteLeaderboard(string SuiteId, string SuiteName, IReadOnlyList<ModelAggregate> Ranked, int ComparisonBasisCaseCount)`
alongside `TagLeaderboard`, and `SuiteLeaderboards` to
`BenchmarkInsightsReport` as an optional trailing parameter, exactly as
`UsageInsights` and `ComparisonBasisCaseCount` were added in earlier rounds.
Optional-with-default keeps every existing construction site compiling and
keeps the change additive.

Each suite's board is built with the **same rule r25 4.1 established for the
overall board**: rank only on cases every ranked model in that suite actually
ran, keyed on case id and case version, and record the size of that basis.
Reuse the existing shared-case-set logic in `BenchmarkInsightsMath` rather
than writing a second one; if it is not currently factored out of the overall
path, factor it out and have both callers use it. Two implementations of
"what did they both sit" is exactly the divergence r25 spent a doc removing.

A suite where no two models share enough cases produces a leaderboard with
`ComparisonBasisCaseCount == 0` and is reported as such rather than dropped
silently. A suite only one model ever ran is not a comparison and says so.

## 4.2 Best across all suites

The cross-suite winner is computed from the per-suite standings, not from a
pool of every case.

**Method: mean per-suite standing, over suites every ranked model ran.**

1. Take the suites that have a usable leaderboard from 4.1
   (`ComparisonBasisCaseCount > 0`).
2. Keep the models that appear in **every** one of those suites. A model that
   ran three of five suites is excluded from the cross-suite ranking and
   named in the caveats, the same treatment r25 4.1 gives a model with too
   little shared coverage.
3. Rank the remaining models by their mean position across those suites,
   breaking ties on mean `QualityPerSecond`, then on model id ordinally so
   the result is deterministic.
4. Record how many suites the answer rests on.

Pooling every case from every suite instead is the obvious alternative and it
is wrong here: a 40 case suite would outvote a 5 case suite four to one, so
"best across all suites" would silently mean "best on the biggest suite".
Ranking per suite and then averaging gives each suite one vote, which is what
the phrase means in English.

**When there is no honest answer, say so.** Fewer than two usable suite
leaderboards, or no model present in all of them, produces no winner and a
sentence explaining which is the case and what would fix it (run the same
suites against the same models). This is r25 4.1's "not enough shared
results" applied one level up, and it is not optional: the whole reason this
column was worth building is that the previous overall number was confidently
wrong.

## 4.3 The column, and what a click shows

On the Insights tab, beside the existing Best overall card:

- The cross-suite leader's name, and the number of suites the ranking rests
  on, stated on the card the way `ComparisonBasisCaseCount` is stated today.
- Its standing in each suite, one row per suite: suite name, position, the
  quality score and tokens/sec that produced it.
- Expanding a suite row reaches that suite's full leaderboard from 4.1.
  `ModelAggregate.Cases` already supports drilling from there to individual
  cases, so no new drill-down machinery is needed.
- Caveats: models excluded for not having run every suite, and suites
  excluded for having no shared case set, both named.

Keep it on the Insights tab. `BenchmarkView.axaml` already has four tabs
(`:189-515`) and this is the same question the Best overall card answers,
asked at a different grain. A fifth tab for one card would be the same
mistake doc 02 is fixing in the Agent panel.

## 4.4 Doctor reads the same report

r25 established that Hermaeus Doctor and the Benchmarks panel read one
report so the two cannot disagree (`DoctorService.Benchmarks.cs`). If Doctor
surfaces a "best model" statement, it uses `SuiteLeaderboards` and the
cross-suite result on the same terms, including the no-honest-answer case.
Check what Doctor currently says before adding anything; if it does not make
a cross-suite claim today, do not give it one.

## Tests

All pure, all against `BenchmarkInsightsMath` and the report record. Nothing
here needs a live run, a model, or a GPU.

- Two models, two suites, one model wins both: it is the cross-suite leader,
  and the card says two suites.
- Two models, two suites, one wins each: the tie is broken deterministically
  on mean `QualityPerSecond`, and the same input in a different order gives
  the same answer.
- Three models, one of which ran only one of three suites: it is excluded
  from the cross-suite ranking and named in the caveats, and the ranking of
  the other two is unaffected by its presence.
- One usable suite only: no cross-suite winner, and the sentence says a
  single suite is not a cross-suite comparison.
- A suite whose models share no cases: `ComparisonBasisCaseCount == 0`, the
  suite is excluded from the cross-suite computation, and it is named.
- Suite leaderboards use the same shared-case-set rule as the overall board:
  assert against a fixture where per-suite averaging over all runs and
  averaging over the shared set give different winners, and the shared set
  is the one that decides.
- An existing report constructed without `SuiteLeaderboards` still behaves
  exactly as it does at 0.32.0. The additive-parameter guarantee, asserted.

## Explicitly not in this doc

- **No pooled-case cross-suite ranking.** 4.2 states why.
- **No suite designer or case editor.** r25 rejected this and the reason is
  unchanged: a round that fixes a ranking should not also grow the thing
  being ranked.
- **No new tab.** 4.3.
- **No weighting knobs.** No per-suite importance slider, no user-tunable
  quality/speed blend. The blend is `QualityPerSecond`
  (`BenchmarkInsightsModels.cs:151`), it is named on the card since r25 4.3,
  and a configurable ranking is a ranking nobody can compare against anyone
  else's.
