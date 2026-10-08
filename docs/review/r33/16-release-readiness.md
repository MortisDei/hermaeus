# R33 release readiness

Updated: 2026-10-08 (Australia/Brisbane).
Branch: `r33/round`. Version: `0.41.0-beta`.

**Status: previous owner checks are accepted. The subsequent Linux tray defect
is repaired and the rebuilt package passed native tray, close, restoration,
rendering and clean Quit checks on Pop!_OS/COSMIC.** Windows execution and CI
for these changes remain open.

## Current repair verification

| Scope | Result |
| --- | --- |
| Debug and Release solution builds | Zero warnings/errors |
| Tray, activation, lock and audio regressions | 24 passed, no failures/skips |
| Full Debug suite | 2,853 passed, 18 platform skips, no failures; 2m35s |
| Final coverage gate | 66.06% (54,148/81,965 lines), above the 60% floor; 2,853 passed, 18 platform skips |
| Linux package | Checksum, layout and no-PDB checks passed |

Results are outside the checkout under `/tmp/hermaeus-oct08-tests`.
The Linux archive was built from the local repaired tree based on `5a7aa81`;
SHA256: `e2b9e49cd8d3045dd53d64ebf25da13b2a22fb2409ab884285aa2c1b745a1512`.

The real rebuilt package registered its StatusNotifier item with COSMIC.
Tray activation confirmed the integration, then a normal WM_DELETE_WINDOW
close hid the window. A second launch exited 0 and restored the same window
and process, retaining exactly the original two managed servers. Native
screenshots before and after restoration showed the full rendered Chat UI,
with no transparent surface; temporary captures were removed after inspection.

The exported **Quit Hermaeus** action exited 0, stopped both servers, removed
the tray item and persisted `CleanExit:true`. All three shutdown owners
completed without timeout. The app is closed. Existing tray and audio choices
were preserved; normal application startup/shutdown persisted state, and no
settings were manually rewritten.

## Defect and behaviour changes

Closing the earlier package hid the window despite COSMIC having no registered
tray item. The desktop and both servers remained alive; ordinary second
launches exited without restoration. Avalonia 12.1.3 requires icons to be
attached through the application's tray collection, which Hermaeus omitted.

The repair owns attachment and removal through that collection. Linux hiding
requires a tray interaction in the current session; otherwise close drains and
exits, while minimize retains the taskbar window. Disabling the tray restores
a hidden window and resets confirmation. A same-user, activation-only named
pipe restores the existing window on second launch without admitting another
data owner or accepting paths, settings or execution commands.

The initial low-level native remap did not restore Avalonia's hidden state and
produced a transparent surface. It was undone; the old stuck process and its
two verified servers were recovered with SIGTERM while the owner was trying
to quit. That recovery was not normal lifecycle shutdown. The subsequent real
rebuilt-package check above established clean shutdown through the tray menu.

Audio feedback was enabled by the code default and the owner's saved settings.
New/missing settings now default off; explicit saved choices remain intact.
This installation is still enabled. Disable it through Settings > Voice >
Enable supplementary audio feedback if desired. TTS is a separate setting.

## Owner acceptance, runtime logs and published CI

The owner accepted the previously listed packaged checks, including
Pop!_OS/COSMIC, audio, focus/DPI, devices and stability. That is owner-reported
acceptance of the earlier build. The later tray defect reopened that specific
check and is addressed by the native rebuilt-package evidence above.

The inspected owner session logged successful llama.cpp installation of
`b11491`. Both live servers used that binary, and two Chat turns subsequently
completed. Neither that session nor the rebuilt-package session contained
error-level entries. Warnings included recovered interrupted Agent runs,
embedding backfill before server readiness, and one slow Chat response.

Published `5a7aa81` passed
[Branch CI](https://github.com/MortisDei/hermaeus/actions/runs/37739921104):
Windows had 2,862 passed/no skips; Ubuntu had 2,844 passed/18 platform skips.
Both Release builds had zero warnings/errors. These results precede this
repair and do not validate it. No R33 PR was open when checked; required
merge-context CI comes from the eventual PR workflow.

## Remaining work

- Packaged minimize-to-tray and tray disable/re-enable preference changes were
  not exercised. Native control regressions cover those mechanics; the owner's
  saved minimize-to-tray choice remains off.
- Run the repair on Windows and obtain green CI for the changed tree.
- Owner-controlled PR review, merge, versioning, tag and release.

Historical Windows package and coverage receipts remain in the
[pre-repair readiness snapshot](https://github.com/MortisDei/hermaeus/blob/5a7aa81284550e5082ed1cb7bba0e430562b8614/docs/review/r33/16-release-readiness.md).
Implementation and crash evidence remain in
[14-owner-dogfood-closeout.md](14-owner-dogfood-closeout.md) and
[15-windows-native-crash-investigation.md](15-windows-native-crash-investigation.md).
If the historical Windows crash recurs, collect native evidence before
assigning causality.
