# 05 - Roadmap (r14)

Version: 0.19.0-alpha.

## Sequencing

1. **03/3.1 install-root fix** first: tiny, pure, and every later
   install/update test builds on correct paths.
2. **01 GPU runtime** (1.1 variant selection -> 1.2 CUDA companion +
   launch verify -> 1.3 offload defaults -> 1.4 advisory). This is
   the round's headline: it turns 191 s prompt reads into seconds on
   the owner's hardware.
3. **02 serving defaults** (2.1 slots, 2.2 cache, 2.3 context truth,
   2.4 batch clamp). Small arg-builder changes, big cache/context
   wins; independent of 01.
4. **03 remainder** (3.3 restart-to-apply, 3.2 prune, 3.4 variant
   passthrough; 3.4 depends on 1.1).
5. **04 latency truth** (4.3 log spam and 4.4 stop logging any time;
   4.1 requires a trace investigation before coding; 4.2 and 4.5
   after 4.1).

## Test expectations

Pure-function coverage everywhere the specs say so: vendor
classification, asset selection matrix, launch-arg matrix (slots,
cache-reuse, ngl -1/0/N, embeddings batch), install-root walk-up,
prune candidate selection, first-event/first-token split, log
gating. Runner: `dotnet test src/Aether.Tests/Aether.Tests.csproj`
per the build-and-verify skill; zero warnings.

## Security review touch

- New download assets (CUDA/Vulkan/cudart) keep the existing
  provenance posture: GitHub releases over HTTPS, tag-pinned or
  latest-API, extracted via the zip-slip-guarded ArchiveExtractor.
  Update docs/security-review.md asset inventory.
- Launch-arg changes stay inside `ArgumentList` (no shell), bound to
  127.0.0.1; nothing new is user-string-interpolated.
- Prune (3.2) deletes only tag-pattern directories under the resolved
  install root, confirm-gated, never a user model or data path.

## Explicit rejections

- **No background/auto update polling.** Update checks remain
  user-initiated (r13 decision stands for llama.cpp binaries too).
- **No /slots polling for live progress** (4.2 uses client-side
  phase/elapsed only); revisit only if phase display proves
  insufficient.
- **No ROCm/HIP/SYCL variants this round.** Vulkan covers AMD/Intel
  well enough for the alpha; adding more variants multiplies the
  install matrix before the two main ones are field-proven.
- **No automatic server restart without consent** (3.3 always asks),
  and never mid-generation.
- **Not chasing the 07-17 "viewmodels 25787 ms" startup outlier**:
  single occurrence, next-day runs show 8.4 s; note it and move on
  unless it recurs.

## Completion notes

Implemented for 0.19.0-alpha. 40 new tests (794 -> 834), zero-warning
build, full suite green. UI wired: the runtime-variant selector (Data
settings), the Slots advanced field and effective-offload label
(Services), the live phase-feedback placeholder on the streaming chat
bubble, and a confirm dialog for the update prune flow.

- **Verified asset names (live GitHub API, tag b10066, 2026-07-18):**
  CPU `llama-b10066-bin-win-cpu-x64.zip`; CUDA
  `llama-b10066-bin-win-cuda-12.4-x64.zip` and `-13.3-x64.zip` (the
  selector prefers 12.4 for driver compatibility); Vulkan
  `llama-b10066-bin-win-vulkan-x64.zip`; CUDA companion
  `cudart-llama-bin-win-cuda-12.4-x64.zip`. Windows ARM64 ships no
  CUDA/Vulkan build, so Auto there resolves to CPU via the null-asset
  fallback. The test fixtures mirror this list.

- **4.1 root cause (pending live trace):** the FirstEvent/FirstToken
  accounting and the phase split are implemented and tested, but the
  chat-trace investigation that names *which* channel the gemma stream
  emits during the invisible decode gap (reasoning deltas vs tool
  deltas vs transport buffering) requires a live send on the owner's
  machine and has not been run. If it turns out to be a reasoning
  channel, `LlamaCppService`'s `Delta` record must also parse
  `reasoning_content` and surface it via the existing thinking
  affordance; today only `content` is parsed, so reasoning-only chunks
  are dropped before they reach the orchestrator's FirstEvent stamp.

- **Still not wired (needs the running Avalonia app to build/verify):**
  3.3's one-click restart-to-apply *button*. The honest "running
  servers keep the old build until restarted" log line and a Doctor
  toast hint are in place, and the prune-confirm dialog is wired, but
  the one-click control that stops and restarts both managed servers
  onto the new binary (with the prompt queued until any in-flight
  generation finishes) is not built. The managed `ServerProcessManager`
  instances live in `ServicesViewModel`, not in Doctor, so this belongs
  on the Services page and needs live-app verification of the
  never-restart-mid-generation guard.

- **Before/after send timings on the owner's machine:** not measured
  (no access to the owner's hardware from this environment).
