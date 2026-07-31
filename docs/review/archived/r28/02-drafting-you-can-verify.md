# 02. Drafting you can verify

## Why

r27 shipped MTP speculative decoding and a Speed Check to measure it. The
owner ran it and recorded the result in `docs/benchmarks.md` two days later
(`6ba9522`): `draft-mtp` at 70.2 median tok/s against `ngram-mod`'s 69.7,
first token 175 ms worse, one cold iteration per case. The entry says the
1.6% difference cannot be distinguished from noise, which is correct and is
exactly the honesty r25's benchmark round was for.

The commit message then names the problem this document exists to fix:

> uniform tok/s across content shapes this different is consistent with
> decode being bandwidth-bound and equally consistent with drafting never
> engaging, so a reproduction should confirm the draft model actually
> loaded.

The app cannot confirm that. `ChatServerTimings` (`ILlmService.cs:42-46`)
carries four fields: prompt tokens, prompt ms, predicted tokens, predicted
ms. `FillTiming` (`BenchmarkService.cs:841-856`) turns them into tok/s.
Nothing anywhere reads a draft statistic, because there was no draft
statistic to read until r27 and nobody added one when there was.

So the Speed Check currently cannot distinguish three very different
outcomes:

1. Drafting engaged and the acceptance rate was too low to help.
2. Drafting engaged, acceptance was fine, and decode is bandwidth-bound so
   it could not help.
3. Drafting never engaged at all and the app compared a configuration
   against itself.

Those demand three different responses and the measurement returns the same
number for all three. A null result you cannot interpret is not a finding,
it is a shrug with decimal places.

## The rule this document operates under

r23 2.3, restated in r26 and again in r27: **the app reports what happened,
it does not rate itself.** Everything below is a number the server produced
or a count the app kept. Nothing below is a grade, a confidence interval, a
significance claim, or a suggestion to change a setting. If an item here
looks like it wants to conclude something, it is wrong and the conclusion
belongs to the person reading it.

## Work items

### 2.1 `ChatServerTimings` carries draft statistics

Extend the record with two nullable fields:

```csharp
public sealed record ChatServerTimings(
    int? PromptTokens,
    double? PromptMs,
    int? PredictedTokens,
    double? PredictedMs,
    int? DraftTokens = null,
    int? DraftTokensAccepted = null);
```

Nullable and defaulted, so every existing construction site compiles
unchanged and every provider that reports nothing continues to report
nothing.

**Verify the field names against the running server.** llama.cpp emits
draft counters in its timings object when speculative decoding is active,
but the names have moved across builds and this pack did not read them off
`b10195`. Start the managed server with `--spec-type draft-mtp` and the
owner's `mtp-gemma-4-E4B-it-BF16.gguf`, send one completion, and read the
raw `timings` object. Map from what is actually there.

If the installed build reports no draft counters at all, **stop and record
that** rather than inventing a substitute. 2.2 and 2.3 still stand; 2.4
becomes "the server does not report it" and the round says so in
`docs/benchmarks.md`. A measurement the app fabricates is worse than one it
admits it cannot take.

Tests: parsing a timings payload with draft fields; one without them
(nulls, no throw); the existing four-field payloads unchanged.

### 2.2 The Speed Check runs more than one iteration

`SpeedCheck.Suite()` (`SpeedCheck.cs:23`) currently produces a suite whose
cases run one cold iteration each. The benchmark infrastructure already
supports repeated iterations with cold and warm phases
(`docs/benchmarks.md`, "Cold-only single-iteration runs, or cold and warm
phase attempts when suites use repeated iterations per case").

Set the Speed Check's iterations to a small fixed number (5 is the
suggestion; the implementer may pick differently and must say why in the
PR). Four cases at 5 iterations is 20 generations per side, which on the
owner's 70 tok/s setup is a few minutes, not an afternoon.

This is the cheapest possible improvement to the round's motivating problem:
r27's null result was a single sample per case and the entry said so twice.

### 2.3 A run reports its observed spread

`SpeedCheckSide` (`SpeedCheck.cs:63`) currently carries single values.
Where a case ran N iterations, record and display the median and the
observed minimum and maximum.

Displayed as, for example, `70.2 tok/s (66.8 to 71.9 over 5 runs)`.

That is a description of what was seen. It is **not** a confidence
interval, and the copy must not use the words "confident", "significant",
"within margin", or any phrasing implying a statistical test. If the range
of one side overlaps the other, the reader can see that for themselves,
which is the entire point and is where the app's job ends.

`SpeedCheckComparison` (`SpeedCheck.cs:78-98`) keeps its existing deltas,
computed from the medians.

### 2.4 Acceptance is shown beside the speed

When a run's timings carried draft counters, the Speed Check result shows
them: drafted tokens, accepted tokens, and the ratio.

This is the number that separates the three outcomes in "Why" above. It
needs no interpretation layer. `0 drafted` means drafting never engaged and
the whole comparison was between two identical configurations, which is a
fact the reader can act on immediately. A high acceptance rate beside a flat
tok/s means the bottleneck is elsewhere, which is also actionable and also
not the app's conclusion to draw.

When the run has no draft counters (a provider that does not report them, or
2.1 finding the build does not emit them), show nothing rather than a zero.
A missing measurement and a measured zero are different facts and the app
already knows the difference: `BenchmarkResult` distinguishes
`server-timings` from `chars-approx` for exactly this reason
(`BenchmarkModels.cs:145-146`).

### 2.5 A Doctor check for drafting that is on and doing nothing

r27 3.7 added a Doctor check for speculative decoding configuration. Extend
it: when speculative decoding is enabled in settings and the most recent
Speed Check run for that model recorded `DraftTokens == 0`, the check
reports that the feature is configured but did not engage on the last
measured run, and offers the Services page as the place to look.

Deterministic: it compares a setting against a recorded number. It does not
run anything, does not diagnose why, and does not propose a fix. If no Speed
Check has been run for the model, the check reports that instead, because
"never measured" is not "measured and found dead".

## What this changes about the recorded result

Nothing, retroactively. `docs/benchmarks.md`'s first recorded result stays
exactly as written; it is an honest record of what was known when it was
taken. Doc 06 adds a forward pointer noting that r28 made the follow-up
measurable, and the owner reruns it when they feel like it. The round does
not depend on the rerun happening, and no work item is conditional on what
it would show.

## Deliberately out of scope

**Automatic tuning, a settings sweep, or a "find the best configuration"
button.** Rejected in r25, r26 and r27, and rejected again here. r27's
stated reason was that a round which measures a thing should not also grow
the thing being measured, which no longer strictly applies now that the
measurement exists. The reason that does still apply is r23 2.3: picking a
winner is rating, and the app does not rate itself. This document makes the
existing measurement interpretable and stops there.

**Any recommendation attached to an acceptance rate.** "Acceptance is 12%,
consider disabling drafting" is a recommendation. `12%` is a fact. Ship the
fact.

**Changing the Speed Check's prompts, cases, or scoring.** r25 and r26 both
rejected growing the suite while fixing how it is read, and 2.2 changes
iteration count only. The four content shapes were chosen deliberately in
r27 3.5 because drafting behaves differently across them, and that choice
was vindicated: repetitive output was the only case that moved.

**Running the Speed Check automatically, on a schedule, or at startup.** It
restarts the managed server and costs minutes. It runs when a person asks.
