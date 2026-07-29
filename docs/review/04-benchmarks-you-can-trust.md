# 04. Benchmarks you can trust

## The problem

The Insights tab leads with a "Best overall" card
(`BenchmarkView.axaml:317-326`). It comes from:

```
public ModelAggregate? BestOverall => Models.Count == 0 ? null : Models[0];
```

(`BenchmarkInsightsModels.cs:40`), where `Models` is `overallRanked`,
which is `aggregates.OrderByDescending(a => a.QualityPerSecond)` (`:170`).

`BuildModelAggregates` (`:243-280`) groups every comparable run by
`ModelId|Quantization|RuntimeKind` and takes a case-weighted average over
**whatever cases that model happened to have run**. Nothing anywhere
requires two models to have sat the same exam.

The only gate is `MinRunsForEvidence = 2` and `MinCasesForEvidence = 10`
(`:68-69`). That is a volume floor, not a comparability floor. A model that
ran two passes of one short, easy suite clears it and can outrank a model
that ran the full suite including the hard cases.

The tag leaderboards are honest within a tag, because they filter to
results actually carrying that tag before aggregating (`:293-316`). Overall
is the single board with no common denominator, and it is the one presented
as the headline answer.

So the owner's report is precise: Best overall is a per-model average over
uneven exams, presented as a cross-test winner.

## 4.1 Rank on a common case set

`BenchmarkResult` already carries `CaseId` and `CaseVersion`
(`BenchmarkModels.cs:117-119`), so this is computable from stored data with
no schema change.

- Compute the intersection of `(CaseId, CaseVersion)` across all candidate
  models. Rank only on results in that intersection.
- `CaseVersion` is part of the key deliberately. Comparing model A on v1 of
  a case against model B on v2 is the same unfairness one level down, and
  the field exists precisely so this is checkable.
- A model that has not run the common set is not ranked. Report it through
  the existing `Caveats` list, which already accumulates exactly this kind
  of message (`:164-167`, `:337-347`): "Model X is not comparable yet: has
  not run 6 of the 24 shared cases."
- Show the comparison basis on the card: "across 24 cases run by all 3
  models". A ranking whose basis is invisible is a ranking you cannot
  check.

**When the intersection is empty or below `MinCasesForEvidence`, there is
no Best overall.** The card says so and says why. This is the item's whole
point: an honest "not enough shared results to name a winner, run the same
suite on both" is the correct output, and it is worth far more than a
confident wrong one. Do not fall back to the current behaviour when the
intersection is thin.

## 4.2 The card opens

Clicking Best overall expands a per-case breakdown: case name, tags,
quality, tokens per second, and pass / fail / timeout, with the runner-up's
row for the same case beside it so the comparison is visible rather than
asserted.

Not a new panel. r24's rejection list is explicit that a new nav panel per
feature is an own goal in an app whose stated problem is having too many
panels; this is an expander inside the Insights tab, next to the leaderboards
already there.

## 4.3 Say which axis won

`QualityPerSecond` is `quality * log2(1 + tok/s)` (`:90-91`), a deliberate
blend of two things the user cares about separately. A model can be "best
overall" while placing second on raw quality, and today nothing on the card
says so.

- Show the two components alongside the blend.
- Flag explicitly when the quality leader and the overall leader are
  different models.

That flag is the most decision-relevant fact the page can show, because it
is the moment the headline stops matching what the user actually wants, and
it is currently invisible.

## 4.4 Doctor must agree

`DoctorService.Benchmarks.cs:39` reads `report.BestOverall`. Once that can
legitimately be null or restricted to a common case set, Doctor handles it
and says the same thing the panel says.

Two surfaces disagreeing about which model is best is worse than either one
being wrong on its own, because it destroys trust in both.

## Testing

Roughly 12 to 16, all against `BenchmarkInsightsMath`, which is already a
pure static class taking runs and returning a report (`:156-189`). No store
and no UI needed.

Two models with disjoint case sets produce no Best overall and a caveat
explaining why. Two models sharing 12 cases rank on those 12 only, and the
result differs from the current whole-average ranking for a fixture built
to expose exactly that. A model that ran an extra easy suite does not gain
from it. `CaseVersion` differences exclude a case from the intersection. A
model missing part of the common set appears in caveats and not in the
ranking. The comparison basis count is reported and correct. The quality
leader differing from the blend leader is flagged. Doctor produces a
sensible advisory when Best overall is null.
