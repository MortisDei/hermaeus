# Pull Requests

Starting with the r23 implementation, changes to `main` land via pull
request instead of direct pushes (the commit introducing this policy and
the r23 spec pack is the last direct push). Tags and GitHub Releases already flow from `main`
(`docs/packaging.md`); PRs put a review gate in front of that flow.

## The one rule that is not negotiable

**One open pull request per maintainer at any one time.**

Finish or close what is open before opening the next. This keeps review
serial, keeps `main` close to every open branch, and prevents the
half-merged-stack problem entirely. With a solo maintainer this means the
repository never has more than one open PR.

## Workflow

1. Branch from up-to-date `main`. Branch names: `rNN/<topic>` for review
   round work (e.g. `r23/execution-plan`), `fix/<topic>` for hotfixes,
   `docs/<topic>` for documentation-only changes.
2. Commit as usual (no AI co-author trailer). Keep the branch focused on
   one deliverable; unrelated fixes get their own PR later.
3. Before opening the PR: `dotnet build Hermaeus.sln` clean (zero
   warnings), `dotnet test` green, docs and `CHANGELOG.md` truthful.
4. Open the PR against `main` using the template
   (`.github/PULL_REQUEST_TEMPLATE.md`). CI must pass.
5. Merge method: **squash merge** for fix/docs branches, **merge commit**
   for review-round branches (their commit sequence documents the round).
   Delete the branch after merge.
6. If a release follows, tag `main` after the merge per
   `docs/packaging.md`. Tags are never pushed from PR branches.

## Scope guidance

- A review round may land as one PR for the whole round or a small series
  of sequential PRs (one at a time, per the rule above); the round's
  roadmap doc decides.
- Urgent hotfixes on a broken `main` are the one case where direct push
  is still acceptable; note it in the commit message and open a follow-up
  issue if anything was skipped.

## Review expectations

Self-review is real review here while the maintainer count is one: read
the full diff on GitHub before merging, not in the editor you wrote it
in. The checklist in the PR template is the merge gate; an unchecked box
means the PR is not ready.

## CI timing, and where the Windows leg spends its time

Measured in r28 (doc 04) rather than guessed at. Every CI run writes a TRX
per matrix leg and uploads it as `test-results-<os>`, so this can be
re-measured instead of re-argued.

From run `30620568409` (the first run carrying the logger):

| Step | ubuntu-latest | windows-latest |
| --- | --- | --- |
| Restore | 17s | 22s |
| Build | 67s | 73s |
| Test | 73s | 256s |

Build is the same on both platforms, which rules out compiler throughput,
project count and the Avalonia XAML compiler. The per-test durations in the
TRX account for 233s of the Windows leg's 256s, so the gap really is inside
the tests rather than in host startup or discovery.

What the timings say about its shape:

- It is **broad, not concentrated**. The 20 test classes with the largest
  gap own 86% of it, and every one of them touches `TempDir`, SQLite, or a
  spawned process. The 25 slowest individual tests own only 43%.
- The heaviest classes are `AgentHarnessTests` (62.5s vs 15.9s),
  `ServiceHarnessTests` (23.2s vs 3.6s) and `ServerProcessManagerTests`
  (13.1s vs 0.0s, because those tests only launch a real process on
  Windows). That last one is coverage, not waste.
- So the lever is per-write filesystem cost, not parallelism. A parallel
  collection would only cover the pure tests, which are the fast ones
  already, so r28 descoped that (doc 04 4.3) on this evidence rather than
  attempting it. `[assembly: CollectionBehavior(DisableTestParallelization
  = true)]` stays, and tests stay serial by default.

r28's one fix from this measurement: `SqliteEvalStore.SaveRunAsync` now
commits its insert and its retention prune in a single transaction, the
way `SqliteTraceStore.AppendAsync` already did. Two durable commits per
saved run is roughly twice the cost on Windows, and the retention test was
one of the two slowest in the suite.

The earlier r27-era figure of 491s for the Windows test step is not
directly comparable: the same suite measured 256s here. Hosted-runner
variance is real, which is also why this repository has no CI wall-clock
budget or regression gate. A timing assertion on a shared runner is a
flaky test with a stopwatch.
