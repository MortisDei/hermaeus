# R31 Batch 17: Beta dogfood corrective closeout

Checked 2026-08-27 on the R31 round branch. This batch records bounded
corrective work only. It does not claim packaged Windows audio, Linux/COSMIC,
or a live long-stream scroll session without an owner-run dogfood check.

## Root causes and corrections

- Chat scroll state used the previous pinned flag when Avalonia reported an
  upward pointer offset and streamed extent growth in the same event. The
  state transition now gives a negative offset delta precedence, preserving the
  reader position until an intentional return to the bottom.
- GPU Fit withheld useful totals when one placement was Unknown. The total
  remains Unknown, while known GPU and RAM subtotals, component values, and
  explanations remain visible. The tested 98,304-context Auto Tune launch path
  was not changed.
- The telemetry flyout had request metrics but no route to the managed process
  identity. Chat can now ask Services for the owning server's exact PID and UTC
  start time. Process working set is sampled as process-scoped RAM. Per-process
  VRAM and other unavailable counters remain Unknown.
- Missing draft and companion paths were mixed into candidate lists. Valid
  files remain candidates; missing configured paths are marked degraded and
  can be cleared or browsed without substituting another file.
- Native Kokoro preview now validates non-empty mono 16-bit PCM WAV output,
  uses the owned PowerShell playback path on Windows, logs safe provider and
  backend diagnostics, and cleans its temporary file on success, failure, or
  cancellation.
- Task approval, completion, and failure audio cues now default off. Explicit
  persisted event choices still win. Known provider voices remain available to
  the per-channel selectors, with the default sentinel retained.
- Normal Settings fields now save after a short debounce and expose Saving,
  Saved, or Failed state. Data-root migration and Services process
  configuration remain explicit transactional flows. The general Settings Save
  button was removed, and the default prompt editor spans both content columns.
- Managed launch uses `--load-mode mlock` or `--load-mode none` only after the
  selected executable advertises `--load-mode`; older runtimes retain the
  legacy options. Proven runtimes also receive localhost-only CORS origins.
  Extra arguments and external profiles retain their existing precedence and
  scope.
- Runtime logging now suppresses consecutive identical Warning/Error entries,
  emits a state-change summary, retains bounded in-memory history, rotates at
  10 MiB, and keeps the newest ten archives. Locked-file failures remain
  best-effort and do not discard in-memory evidence.
- Companion lifecycle prefers an explicit `.hermaeus/companions.json` mapping
  in the linked Hugging Face repository, but also supports ordinary sibling
  `mmproj*.gguf` and `MTP/mtp*.gguf` layouts. The fallback requires
  same-revision tree entries, full LFS hashes, bounded GGUF role metadata, and
  deterministic model compatibility facts for automatic selection; ambiguous
  candidates remain unchecked for user review. Initial download choices,
  per-model automatic update policy, update/recovery sync, known sizes, and
  Keep files / Remove files / Cancel deletion confirmation are implemented.
  Third-party repositories need no Hermaeus-specific file.

## Verification

- `dotnet build Hermaeus.sln --no-restore`: passed, zero warnings and errors.
- Focused R31 tests: 81 passed, zero failed, zero skipped in the companion
  and selection regression pass.
- Added regression coverage for scroll transitions, partial GPU Fit output,
  missing draft selection, runtime help-gated launch arguments, settings
  debounce persistence, WAV playback backend reporting, and runtime-log
  suppression, rotation, FIFO pruning, and locked-file handling.
- Companion lifecycle tests cover hash-verified metadata, model-only initial
  downloads, enabled and disabled update behavior, missing-asset recovery,
  and explicit Keep/Remove policy choices.
- Full sequential suite: 2,241 passed, zero failed, zero skipped, 4m55s.
- Coverage gate: 63.87% line coverage (36,870 of 57,723 lines), above the 60%
  floor. Owner-run live Windows/Linux/COSMIC checks remain unverified.

## Security and privacy

The CORS change narrows browser origins for managed runtimes that prove the
option, but it is not authentication. Loopback binding, extra-argument trust
warnings, redaction, argument-list process launch, safe cleanup, and Unknown
semantics remain in force. Diagnostics omit preview text, secrets, and raw
model contents. Runtime telemetry retains local process and hardware evidence
in the existing local stores and backups.

## Baseline and deferred work

The observed startup and first-token timings are retained as a performance
baseline. No bounded Hermaeus-side accounting defect was established, so no
performance rewrite was included. Exact live per-process VRAM attribution,
packaged playback, Linux/COSMIC behavior, and the full Windows dogfood route
remain live checks rather than automated claims.
