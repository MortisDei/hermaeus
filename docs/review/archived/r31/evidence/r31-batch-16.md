# R31 Batch 16 evidence

Checked: 2026-08-26 on Windows 11. Automated gates and the public-diff audit
are complete. Live platform gates are intentionally still open because this
workspace cannot stand in for the owner's Linux/COSMIC machine or a packaged
Windows install.

## Completed

- Solution build: 0 warnings, 0 errors.
- Focused R31 gate: 31 passed.
- Full sequential suite: 2,203 passed, 0 failed, 0 skipped.
- Coverage ratchet: 63.79% line coverage, above 60%.
- `git diff --check`: passed after the close-out changes.
- Changed-file review checked for credentials, tokens, local paths, test and
  coverage artifacts, shell-string launches, unsafe process cleanup, and
  persistent telemetry. No in-scope defect was found.

## Still required on the published checkpoint

Windows:

- Packaged launch and settings round-trip.
- Audio cues at 25%, 100%, mute, and while TTS is speaking.
- Managed-runtime failure/recovery and no orphan player/runtime process.
- Telemetry flyout against an exact managed process identity, with missing
  counters shown as `Unknown`.

Linux/COSMIC:

- Published-branch launch, normal Chat, telemetry identity and health gate.
- Scroll pinning while streaming and after user scroll-up.
- Audio device playback at 25%, 100%, mute, and during TTS.
- Clean shutdown with no orphan runtime or player process.
- Existing R31 Linux gates from Batches 2, 4, 5, 6, 7, 9, and 10.

These are live verification items, not automated pass claims. The latest
published revision and CI state must be recorded when the owner pushes the
reviewed checkpoint.
