# 06. Measured quality and active research

Quality work follows measured risk. Research rows produce dated evidence and an
integration gate, not unstable product code.

## 6.1 Coverage baseline and selection

The latest recorded component coverage in `docs/review/deferred.md` is from the
R29/R30 measurement and may drift as R31 lands. Re-run the repository coverage
script after the evidence/GPU Fit/Lab foundations exist, then choose tests from
uncovered behavior, not the percentage alone.

Priority candidates from the existing measured gaps:

| Component | Current evidence | Valuable R31 target |
| --- | --- | --- |
| `Services.SystemInfoService` | recorded 33.1%; current tests cover formatters, Windows registry parsing, and process-lifetime hardware-profile caching | injectable command/filesystem seams for Linux GPU/RAM observation, cancellation/timeout, multiple GPUs, unavailable tools, and no stale cache when a live telemetry source requires refresh |
| `Services.LocalAiSetupService` | recorded 43.5%; existing focused tests cover approval, provenance, and XTTS Python gating, plus harness coverage | action validation, partial/cancel cleanup, data-root/asset-root transitions, redacted errors, and Linux native setup paths encountered by R31 live work |
| `ViewModels.ServerProcessViewModel` in `ServicesViewModel.cs` | recorded 64.8%; tests cover orphans, defaults, model path, restart, speculative and MoE parsing | capability drift, temporary Lab-clone boundaries, failed launch truth, telemetry identity handoff, and config apply staleness |
| `Services.LlamaServerSetupService` | recorded 65.0%; install/hash/rate-limit coverage plus harness cases | release-channel parser/selection, semantic prerelease truth, cleanup/rollback, platform asset absence, and digest refusal |
| `ViewModels.AgentViewModel` | recorded 74.7%; workspace and orchestration tests exist | normalized outcome display, model identity, Project State proposal review, API-owned task visibility, and decision ownership where R31 touches it |
| `ViewModels.MainWindowViewModel` | recorded 58.5%; startup tests cover several background flows | Lab navigation/activation, Project State context bridge, telemetry lifecycle, and failure isolation introduced by R31 |

`LocalAiSetupService`, `AgentViewModel`, `MainWindowViewModel`, and
`ServicesViewModel` are large hot spots. Prefer extracting pure/service logic
that R31 actually needs over adding more branches solely to create test seams.
Do not refactor unrelated paths or split classes for line-count aesthetics.

## 6.2 Quality acceptance

- Run coverage before choosing the final targeted batch and record the dated
  component figures in the R31 close-out. Do not copy the R29 numbers forward
  as current.
- Every added test protects behavior, error handling, security, migration, or a
  demonstrated platform boundary. No getter/setter or constructor padding.
- Preserve sequential tests, external results directories, platform-skip
  honesty, injectable timeouts, and harness registration.
- New Linux-specific behavior is tested on Linux CI where deterministic and
  live-verified on Linux/COSMIC where it depends on drivers/audio/runtime.
- Windows-only APIs use `[WindowsOnlyFact]`; cross-platform logic should remain
  pure and test on both.
- Full coverage remains above the 60% ratchet. Raising the floor is not an R31
  goal unless measured headroom becomes large and stable.

Expected targeted-quality budget beyond feature tests: 20-30 tests. If current
coverage shows another component has materially higher risk in changed code,
record the evidence and substitute it rather than blindly following this table.

## 6.3 Reconfigurable llama-server runtime

Status at planning time: Research/Watch. Upstream discussion identifies the
same desired operations as R31, but also states that model, context,
projector/speculative, and placement lifetimes are still tightly coupled:
<https://github.com/ggml-org/llama.cpp/discussions/25674>.

Track:

- stable HTTP/API operations for context/KV precision, speculative companion,
  projector, and device placement changes;
- request draining, concurrent-slot behavior, rollback, and failure semantics;
- whether model weights truly remain loaded and how identity changes;
- how metrics/capabilities announce the new context identity.

Hermaeus impact when stable:

- Lab could replace temporary process restarts with context recreation;
- Services needs a runtime-context state separate from process/model state;
- telemetry and experience must create a new configuration identity at each
  reconfiguration;
- active Chat requests must drain/cancel safely;
- Apply still requires explicit user review.

Do not implement against a fork/prototype endpoint in R31.

## 6.4 MoE expert prefetch and NVMe streaming

Status: Research/Watch unless the selected upstream runtime advertises a stable
mechanism. Existing `--n-cpu-moe` is placement, not caching, prefetch, or
streaming.

Review primary/local-inference work for:

- expert-cache key and eviction policy;
- router/predictor evidence and hit-rate counters;
- RAM/VRAM/NVMe tier identity and measured bandwidth;
- prefetch accuracy versus transfer cost;
- mmap/page-cache interaction and destructive disk-thrash risk;
- correctness under cache miss and cancellation;
- model-specific versus general behavior.

The integration gate requires an upstream-supported mechanism, direct hit/miss
and transfer evidence, bounded configuration, clean fallback, and repeatable
benefit on local hardware. A custom patch against one llama.cpp commit is not a
production contract. Research findings may inform Lab recipe design but never
become a hidden automatic placement policy.

## 6.5 Public llama.cpp speculative API

Status: Research/Watch. The current high-level draft/verify orchestration lives
in llama.cpp common code, while a stable exported API remains requested:
<https://github.com/ggml-org/llama.cpp/issues/27469>.

Track whether a public API owns:

- drafter creation/lifecycle;
- draft generation and target verification;
- KV synchronization/rollback;
- sampling and deterministic behavior;
- MTP/EAGLE/DFlash differences behind one context;
- metrics and error contracts;
- ABI/version guarantees.

Hermaeus currently launches `llama-server`, so no binding rewrite is required.
If a stable API matures, evaluate whether it simplifies capability discovery or
an in-process Lab runtime. Do not add a native binding dependency in R31 merely
because the issue exists.

## 6.6 DFlash

Status: representable in the capability registry; Research/Watch for production
enablement. Upstream documents `draft-dflash` and target-specific conversion in
<https://github.com/ggml-org/llama.cpp/blob/master/docs/speculative.md>.

Track:

- supported target/draft formats and metadata stability;
- block size/parameter bounds;
- acceptance/direct counters and memory overhead;
- output equivalence/correctness expectations;
- backend/platform support and failure behavior;
- actual local benefit versus EAGLE/simple/MTP on comparable workloads.

Doc 03's external-drafter adapter is the only acceptable future product path.
No DFlash-only settings page or filename detector.

## 6.7 Reconstructable KV / KV-Direct

Status: Research/Watch. Do not confuse two different uses of the name:

- KVDirect for distributed disaggregated KV transfer is a serving-system paper
  and not the requested local reconstructable representation:
  <https://arxiv.org/abs/2501.14743>.
- "The Residual Stream Is All You Need" proposes exact KV reconstruction from
  retained residual state:
  <https://arxiv.org/abs/2603.19664>.

Track local-inference evidence for memory saved, reconstruction compute/latency,
bitwise or quality equivalence, model-architecture assumptions, quantized KV,
sliding/hybrid/recurrent attention, prompt reuse, and runtime integration.

The capability registry can represent `runtime.kv.reconstructable` and its
parameters without shipping it. Lab can add a recipe only after a selected
runtime provides a stable implementation and trustworthy memory/correctness
evidence. No Hermaeus-side KV reimplementation in R31.

## 6.8 Research deliverable

At R31 close-out, update a dated section in `docs/llama-cpp-features.md` for
each watch row with:

- upstream link and checked date;
- observed selected-runtime state;
- Available/Unavailable/Unknown evidence;
- what changed since this planning snapshot;
- exact product integration gate;
- decision: implemented conditionally, remains watch, or rejected with reason.

Research is complete when the evidence and decision are recorded. It is not
measured by code volume.

## 6.9 Research acceptance and test budget

- Capability ids for reconfiguration, public speculative orchestration,
  DFlash, expert cache/prefetch/streaming, and reconstructable KV can be stored
  as Unknown without settings/schema surgery.
- No watch feature is exposed as usable from a filename, remembered build, or
  planning document.
- Dated docs distinguish upstream support from the owner's selected-runtime
  observation.
- If any watch item crosses its gate, it gets the same capability, identity,
  Lab correctness, security, test, and Linux live gates as comparable shipped
  features.

Expected automated coverage is 5-10 registry/document guard tests unless a
watch feature crosses its gate. Research itself is verified by source review
and selected-runtime observation, not fake unit tests.
