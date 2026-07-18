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

(Filled during implementation: 4.1 root-cause paragraph, verified
asset names for the chosen release tag, measured before/after send
timings on the owner's machine.)
