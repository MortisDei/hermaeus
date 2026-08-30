# R31 Batch 14 evidence

Checked: 2026-08-26 on Windows 11. This file records automated close-out
evidence and does not claim Linux or packaged-audio behavior.

## Measured quality

- `dotnet build Hermaeus.sln --no-restore`: passed with 0 warnings and 0 errors.
- Focused R31 regression gate: 31 passed, 0 failed, 0 skipped.
- Full sequential suite: 2,203 passed, 0 failed, 0 skipped in 4 minutes 18 seconds.
- `pwsh ./scripts/coverage.ps1`: passed 2,203 tests in 11 minutes 33 seconds.
- Aggregate line coverage: 63.79% (`36,238 / 56,805`), above the 60% ratchet.

## Hardening and documentation

- Lab ownership manifest now distinguishes Missing, Known, and Unknown. Unknown
  evidence refuses mutation and process cleanup, preserves raw bytes, and emits
  only a safe diagnostic.
- Telemetry remains in memory and retains identity, source, trust, timestamp,
  and missing-value semantics. It does not persist process paths or counters.
- Audio feedback is bounded, explicit-event-only, cooldown-deduplicated,
  TTS-arbitrated, source-controlled, and cleaned up after playback.
- Windows playback passes the WAV path as an argument rather than interpolating
  it into a PowerShell command string.
- User-facing and workflow documentation was synchronized in `docs/features.md`,
  `docs/user-guide.md`, `docs/voice.md`, `docs/security-review.md`, and the
  review pack.

No new package reference, database migration, shell-string launch, test
parallelization, or persistent telemetry store was added.
