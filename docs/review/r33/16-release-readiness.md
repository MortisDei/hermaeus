# R33 release readiness

Updated: 2026-10-06 (Australia/Brisbane).
Branch: `r33/round`. Version: `0.41.0-beta`.

**Status: R33 repairs include audio and dependency follow-ups; packaged
release checks remain open.** This is a current summary,
not a declaration that every platform or device has passed.

Earlier implementation and native-crash evidence remain in
[14-owner-dogfood-closeout.md](14-owner-dogfood-closeout.md) and
[15-windows-native-crash-investigation.md](15-windows-native-crash-investigation.md).

## Automated verification

| Scope | Result |
| --- | --- |
| Combined audio, Settings test and Avalonia follow-ups: Debug and Release solution builds | Passed, zero warnings/errors |
| Focused audio, Settings lifecycle, docs, architecture, binding, Markdown and image tests | 121 passed, zero failed/skipped |
| Combined follow-ups: complete Debug suite | 2,855 passed, zero failed/skipped, in 5m50s |
| Combined follow-ups: final instrumented suite and coverage | 2,855 passed, zero failed/skipped; 66.32% (54,270/81,830 lines), above the 60% floor |

Test results are outside the checkout under
`%TEMP%\hermaeus-r33-desktop-tests`. The coverage script checks the Cobertura
counts and removes its external temporary report.

The first follow-up full run passed 2,854 tests and failed one Settings test
with a sharing violation while polling its temporary settings file. Five
neighbouring autosave tests now wait for the existing post-save notification
before reloading persisted values. Production settings code is unchanged.
The corrected tests passed the full suite before the Avalonia update; the
combined-tree receipt is `r33-avalonia-final.trx`.

GitHub Branch CI passed on Windows and Ubuntu for `8e2c72c`, the remote branch
tip before the local repair commits. CI has not run for the later local commits.
There is no open R33 pull request. Branch CI is feedback;
required merge-context CI still comes from the pull-request workflow.

## Confirmed dogfood results

- Real local Chat answered `17 × 23` as `391`.
- Workspace keyboard entry and Ctrl+S saved a scratch file; a later external
  edit caused a stale save to be refused without overwriting the external edit.
- A real-provider Agent run prepared a scratch-file write, left the file absent
  before approval, then applied and read back the approved result.
- Lab cancellation restored the selected Chat source. Benchmark cancellation
  persisted its partial result. Unverified runtime evidence remained excluded
  from comparison and ranking claims.
- RAG used the question's selected dataset and exposed source revisions/hashes.
  It disclosed an embedding-model mismatch and keyword fallback.
- The owner rebuilt and launched the repaired package, dismissed the previously
  stuck parent task, and used Doctor to detect and update llama.cpp.
- Normal shutdown recorded `CleanExit:true`. The temporary Close to tray change
  was restored through Hermaeus Settings.

## Audio correction

The owner heard only two notes from the rebuilt runtime-ready/completion cue.
Runtime-ready diagnostics showed three generated pitches and successful native
playback. API success does not establish that all three notes were audible.

The shorter 250/150 ms production timing failed the listening gate. Semantic
cues now use 400 ms notes with 300 ms pauses, matching the earlier direct probe
in which the owner heard three rising notes. The longest cue is 1.8 seconds;
recording cues retain short single tones. The running package predates this
follow-up. The new production timing still needs a packaged
owner listening check.

## Dependency follow-up

Dependabot PRs #17-#19 proposed overlapping updates on `main`. R33 consolidates
all four Avalonia framework references at 12.1.3. AvaloniaEdit remains at its
latest published release, 12.0.0, whose dependency permits the newer framework.
Restore and build evidence establish package compatibility, not native desktop
acceptance. See [the upgrade policy](../../avalonia-upgrade.md).

## Remaining release gates

- Rebuild and confirm three audible notes for runtime-ready/completion, then
  check Markdown updating after switching tabs. Exercise theme switching, tray,
  window chrome, startup and second launch with Avalonia 12.1.3.
- Short Chat/RAG and Lab run/cancel checks against the updated llama.cpp runtime.
  Existing embedding mismatch remains a disclosed configuration issue; the
  owner's dataset has not been reindexed or deleted.
- Remaining device, focus/DPI and telemetry lifecycle checks, plus unattended
  stability. Process VRAM remains Unknown where direct attribution is unavailable.
- Linux/COSMIC native validation, including the unresolved AvaloniaEdit flicker.
  Windows results and Ubuntu CI do not close native Linux desktop gates.
- Push the intended reviewed commits, obtain green CI for that exact tip, then
  complete owner-controlled PR review, versioning, merge, tag and release.

If the historical Windows crash recurs, collect native crash evidence before
assigning causality. Tags, releases and repository settings remain owner-only.
