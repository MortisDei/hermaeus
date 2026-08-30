# R31 Batch 0 evidence

Checked: 2026-08-24 on Windows 11. This file records bounded facts required by
Batch 0. It contains no executable, model, Data Root, user, or host path.

## Repository baseline

- Branch: `r31/round`.
- Starting implementation commit: `c4afbf4`.
- `main` and `origin/main` were `fd63b3e` when the implementation pass began.
- The worktree was clean and `origin/r31/round` matched `c4afbf4`.
- The R31 terms in docs 01 through 06 were accepted without redesign. The
  exact frozen vocabulary is listed below.

## Selected Windows runtime observation

The current saved Chat server and executable were inspected read-only through
the normal Hermaeus settings location. The owner-selected Data Root is outside
the checkout; its literal path is intentionally omitted.

| Fact | Observation | Evidence state |
| --- | --- | --- |
| Runtime version | `0.2.0-dev`, build `10590`, commit `6657ded4f` | Direct runtime observation from `--version` |
| Executable identity inputs | file present; 9,216 bytes; modified `2026-08-22T22:09:22Z` | Direct filesystem observation; portable path omitted |
| Help probe | completed within five seconds | Direct runtime observation |
| Advertised speculative types | `draft-dflash`, `draft-dspark`, `draft-eagle3`, `draft-mtp`, `draft-simple`, `ngram-map`, `ngram-mod`, `ngram-simple` | Extracted from the selected executable's `--help` |
| Prompt threads | `--threads-batch` advertised | Extracted from `--help` |
| Performance diagnostics | `--perf` advertised | Extracted from `--help` |
| Reasoning format | `--reasoning-format` advertised | Extracted from `--help` |
| Reasoning preservation | paired preserve/no-preserve flags advertised | Extracted from `--help` |
| Live `/props` | unavailable because the selected server was not running | Unknown, not Unavailable |
| Selected model asset | present | Direct filesystem observation; name/path omitted |
| Selected configuration | context 64,512; GPU layers 999; prompt threads 0; one slot; KV `q8_0`; Flash Attention on; CPU MoE layers 0; no speculative type configured | Extracted from saved settings; no settings were changed |

The installed runtime is newer than the planning snapshot. Help output proves
that the binary contains generic mechanisms. It does not prove that the
selected model engages MTP, that an external drafter is target-compatible, or
that any mechanism is correct or beneficial. Those states remain Unknown until
their model-specific live gates run.

## Current upstream observation

Checked against primary upstream sources on 2026-08-24:

- llama.cpp's current speculative documentation lists simple, EAGLE-3,
  DFlash, DSpark, MTP, and n-gram implementations, with target-specific
  metadata and launch requirements for EAGLE-3 and DFlash:
  <https://github.com/ggml-org/llama.cpp/blob/master/docs/speculative.md>.
- The release page's newest observed rapid build was `b10605`, published as a
  prerelease. This does not establish a Stable update channel:
  <https://github.com/ggml-org/llama.cpp/releases/>.
- `b10603` reports MTP support for GLM-4.5-Air. The selected `b10590` predates
  that release, and neither the release note nor generic MTP help proves
  GLM-5.2 support. GLM-family capability therefore remains model/runtime-pair
  evidence-gated.
- Runtime reconfiguration remains discussion-level work:
  <https://github.com/ggml-org/llama.cpp/discussions/25674>.
- A stable public high-level speculative C API remains absent. A newer open
  issue reports the same gap for in-process DSpark/speculative consumers:
  <https://github.com/ggml-org/llama.cpp/issues/27089>.

No upstream observation contradicts the R31 gates. No planning contract
correction is required before Batch 1.

No Linux runtime was reachable from this Windows environment. Selected Linux
runtime version, help, capabilities, and COSMIC live behavior remain unverified
until a published checkpoint is tested on the owner's Linux system.

## Coverage baseline

Command: `pwsh ./scripts/coverage.ps1`. The script's ignored report directory
was removed after extracting aggregate evidence.

- Tests: 1,899 passed, 0 failed, 0 skipped in 5 minutes 6 seconds.
- Overall line coverage: 61.96% (`31,363 / 50,614`).
- Overall branch coverage: 56.64%.
- The 60% ratchet passed.

| Project | Line coverage |
| --- | ---: |
| `Hermaeus.Composition` | 100.00% |
| `Hermaeus.Agent` | 91.44% |
| `Hermaeus.Core` | 91.24% |
| `Hermaeus.LocalApi` | 89.57% |
| `Hermaeus.Rag` | 79.99% |
| `Hermaeus.Services` | 71.86% |
| `Hermaeus.Mcp` | 65.15% |
| `Hermaeus.Voice` | 65.12% |
| `Hermaeus.ViewModels` | 63.91% |
| `Hermaeus.Desktop` | 1.25% |

The R31 target classes currently measure:

| Class | Line coverage | R31 selection consequence |
| --- | ---: | --- |
| `Services.SystemInfoService` | 33.11% | Retain as the primary measured hardening target, especially shared telemetry seams and unavailable platform probes. |
| `Services.LocalAiSetupService` | 46.03% | Retain only for R31-touched cleanup, redaction, or Linux setup paths. |
| `ViewModels.MainWindowViewModel` | 60.09% | Cover Lab navigation and telemetry lifecycle introduced by R31; avoid unrelated refactoring. |
| `ViewModels.ServerProcessViewModel` | 66.30% | Cover capability drift, Lab-clone/apply boundaries, and telemetry identity handoff. |
| `Services.LlamaServerSetupService` | 68.73% | Cover release-channel parsing and truthful prerelease handling. |
| `ViewModels.AgentViewModel` | 74.16% | Cover only R31 outcome, model-identity, and Project State behavior. |

These figures replace the historical R29/R30 figures for R31 prioritization.
They do not justify percentage padding or unrelated hot-spot refactors.

## Frozen R31 terminology and schema decisions

The following names are implementation contracts:

- Evidence origins: `DirectObservation`, `DeterministicCalculation`,
  `UserProvided`, `Extracted`, and `ModelInference`. Legacy serialized
  `Inferred` reads as `ModelInference`; new writes use `ModelInference`.
- Normalized outcomes: `Succeeded`, `PartiallySucceeded`, `NoEffect`,
  `Unavailable`, `Denied`, `Blocked`, `Failed`, `Cancelled`, `TimedOut`, and
  `Unknown`.
- Capability state: `Available`, `Unavailable`, or `Unknown`. A failed probe is
  `Unknown`.
- Initial experience domains: `agent-tool-outcome`, `gpu-fit-observation`, and
  `lab-run`; the initial database schema version is 1.
- Experience corrections supersede derived experience and preserve raw source
  evidence. Explicit privacy removal is a hard content deletion after
  dependency review.
- Identity version 2 has separate runtime, model, hardware, and configuration
  identities. Version 1 stable ids remain readable and explicitly incomplete.
- GPU Fit prediction, runtime observation, and optional empirical adjustment
  remain separately labelled values.
- Lab correctness states are `Equivalent`, `Different`, and `Unknown`.
- Lab never mutates active Chat runtime state or saved settings during a run.
- Project State item kinds are `AcceptedDecision`, `RejectedApproach`,
  `Constraint`, `UnresolvedQuestion`, `ImportantArtifact`, and `NextAction`.
- An API token never carries approval authority. R31 has no Agent approval
  endpoint.
- Telemetry values retain source, trust, identity, timestamp, and missing
  reason. An absent counter is not zero.

## Batch 0 result

Batch 0 is complete. The observed runtime drift requires registry-based,
model-specific evidence exactly as planned. No accepted safety, evidence,
identity, Lab, API, telemetry, or research boundary was weakened or redesigned.
