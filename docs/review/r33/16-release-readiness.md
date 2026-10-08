# R33 release readiness

Updated: 2026-10-08 (Australia/Brisbane).
Branch: `r33/round`. Version: `0.41.0-beta`.

**Status: Windows repairs pass automated verification; remaining packaged
owner and Linux release checks remain open.** This is a current summary,
not a declaration that every platform or device has passed.

Earlier implementation and native-crash evidence remain in
[14-owner-dogfood-closeout.md](14-owner-dogfood-closeout.md) and
[15-windows-native-crash-investigation.md](15-windows-native-crash-investigation.md).

## Automated verification

| Scope | Result |
| --- | --- |
| Current local tree: Debug and Release solution builds | Passed, zero warnings/errors |
| Final Chat/Lab bound-picker and Markdown regressions | 38 passed, zero failed/skipped |
| Current local tree: complete Debug suite | 2,862 passed, zero failed/skipped, in 3m40s; `r33-bound-pickers-full.trx` |
| Current win-x64 package | Built; checksum matched, no PDB files; Avalonia 12.1.3 |
| Final precommit instrumented suite and coverage, 2026-10-08 | 2,862 passed, zero failed/skipped, in 14m25s; 66.49% (54,425/81,851 lines), above the 60% floor |

Test results are outside the checkout under
`%TEMP%\hermaeus-r33-desktop-tests`. The coverage script checks the Cobertura
counts and removes its external temporary report.

Today's native binding regression reproduced Chat sampling overrides resetting
when a model-list refresh temporarily cleared the bound picker selection.
The repair suppresses side effects across publication, preserves reported
usage for the same model, and retains defaults for genuine model changes.
A delayed draft estimate can no longer overwrite final provider usage.
Choices made while model discovery is pending are retained. The same bound
picker failure reset Lab's displayed candidate during source status refreshes;
the frozen definition stayed correct. Both pickers now preserve their drafts.
Native Markdown tests now cover real tab detachment, abandoned parses and
explicit disposal without composing the owner's application services.

The rebuilt package uses the reviewed tree based on `01b884e`, including the
repairs described here. Archive SHA256:
`91292d02aeca6530ac255099a469e9c36a7d640291555afdaf1bd386f8f7247c`.

Historical GitHub Branch CI passed on Windows and Ubuntu for `8e2c72c`, before
these repair commits. Check the published tip's CI separately; the earlier
result does not validate these repairs. At this receipt there is no open R33
pull request. Branch CI is feedback; required merge-context CI still comes
from the pull-request workflow.

## Confirmed dogfood results

- Real local Chat answered `17 × 23` as `391`.
- The previously running package (`3542f30`, Avalonia 12.1.2) answered
  `19 × 23` as `437`, rendered Markdown again after navigation, and completed
  Doctor with no errors. These observations do not validate today's package.
- The owner reported that the missing taskbar icon eventually appeared.
  Delayed appearance is confirmed; its cause is not established.
- With Avalonia 12.1.3, local Chat answered `29 × 17` as `493`. Navigation
  through RAG and back preserved Markdown, temperature 0.7 and the provider's
  903-token count. Light and System themes rendered correctly; System was restored.
- The new package answered a local RAG question with five citations. Diagnostics
  disclosed the existing embedding mismatch and keyword fallback. No dataset
  was reindexed or deleted.
- The final package kept the Lab candidate display at 2,048 during source
  suspension, running and cancellation/restoration. The source returned to
  `Restored`; only the configured Chat and embedding processes remained.
  A subsequent real Chat request returned `READY` with provider-reported usage.
- A real second launch exited with code 0 while one desktop process remained.
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
recording cues retain short single tones. The rebuilt package includes this
timing; the owner listening check remains open.

## Dependency follow-up

Dependabot PRs #17-#19 proposed overlapping updates on `main`. R33 consolidates
all four Avalonia framework references at 12.1.3. AvaloniaEdit remains at its
latest published release, 12.0.0, whose dependency permits the newer framework.
Windows Chat, RAG, theme and Lab checks supplement restore/build evidence;
they do not close Linux or device acceptance. See
[the upgrade policy](../../avalonia-upgrade.md).

## Remaining release gates

- Confirm three audible notes for runtime-ready/completion and investigate any
  repeatable taskbar-icon delay. Complete tray hide/restore and focus/DPI checks.
- Remaining device, focus/DPI and telemetry lifecycle checks, plus unattended
  stability. Process VRAM remains Unknown where direct attribution is unavailable.
- Linux/COSMIC native validation, including the unresolved AvaloniaEdit flicker.
  Windows results and Ubuntu CI do not close native Linux desktop gates.
- Obtain green CI for the published tip, then complete owner-controlled PR
  review, versioning, merge, tag and release.

If the historical Windows crash recurs, collect native crash evidence before
assigning causality. Tags, releases and repository settings remain owner-only.
