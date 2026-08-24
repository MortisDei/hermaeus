# Review round 31: Experience, not intuition

Audience: the implementing agent. Read this file, then the numbered documents
in order. Doc 07 is the sequencing and release contract.

## Why this round exists

R30 made Hermaeus public and established evidence-backed capability discovery,
benchmark fingerprints, separate reasoning transport, and truthful Unknown
states. R31 turns those foundations into a product rule:

`normalized outcomes -> empirical experience -> measured optimisation / improved prediction`

The order is load-bearing. A tuning workflow built before normalized evidence
would optimize strings and exit codes. An estimator changed directly from a
handful of observations would turn local measurements into folklore. R31 first
defines what happened, then stores the evidence and its provenance, then lets
bounded consumers compare prediction with observation.

This is not an autonomous learning round. Experience may inform a model or a
user, but it never changes a safety decision, silently selects a model, rewrites
an analytical formula, applies a runtime configuration, trains an adapter, or
promotes an inference into a fact.

## Verified starting point

The contract was written against `fd63b3e`, the R30 merge on `main`, released as
`v0.37.0-alpha`.

- `AgentToolResult` preserves summary, source, command exit code, and timeout,
  but has no shared semantic outcome. Policy refusals and unavailable MCP tools
  are still represented through several different paths.
- `SourceReference.EvidenceOrigin` currently has `DirectObservation`,
  `UserProvided`, and `Inferred`. Its documentation explicitly groups
  deterministic calculation with observation and model inference with
  heuristics. That is incompatible with R31's five-category rule and must be
  corrected before the experience store consumes it.
- Benchmark runs already retain `EmpiricalProfileFingerprint` and a direct
  observation source. This is a useful seed, not an experience store.
- GPU Fit already reads GGUF layer, KV-head, key/value dimension, context, and
  sliding-window pattern metadata. `KvCacheMath` accounts for interleaved
  sliding-window attention. The remaining gap is a complete, inspectable
  breakdown for the selected runtime configuration, companion models,
  placement, measured overhead, and prediction-versus-observation comparison.
- Runtime capability discovery already uses bounded GGUF metadata, the selected
  executable's `--help`, and a healthy server's `/props`. It discovers unknown
  speculative type names but its cache identity is still path, size, and mtime,
  and the capability shape is fixed around R30 features.
- General speculative launch arguments and vocabulary checks already exist.
  The exposed workflow is intentionally limited to n-gram and MTP.
- Benchmarks already carry llama-server prompt/decode timings, TTFT, draft and
  accepted-token counters, quality results, cold/warm phases, and exact known
  configuration. Lab should share those measurement primitives without turning
  Benchmarks into a settings sweeper.
- Projects are metadata and defaults in `projects.db`; there is no explicit
  reviewable current-state record.
- Approved orchestration children inherit `AgentWorkspaceOptions.ModelId`.
  Neither `AgentSubTaskSpec`, child task state, transcript, nor synthesis owns a
  child model identity.
- Local API exposes chat, embeddings, memory, RAG, models, and capabilities.
  It has no Agent surface, token scope, or non-desktop approval protocol.
- Chat already tracks whether its message scroll is pinned to the bottom and
  stops following when the user scrolls away. Item 24 is therefore a regression
  and live-verification item, not a greenfield rewrite.
- `AudioPlayback` is a shared cross-platform WAV launcher, but there is no cue
  policy, event vocabulary, volume/mute setting, non-audio equivalent, or
  arbitration with TTS.

## Upstream facts checked for this contract

These are upstream state, not promises that the owner's installed runtime has
the feature. Every live use remains gated by the selected executable's observed
capabilities.

- Current llama.cpp documentation names `draft-simple`, `draft-eagle3`,
  `draft-dflash`, `draft-dspark`, `draft-mtp`, and several n-gram mechanisms:
  <https://github.com/ggml-org/llama.cpp/blob/master/docs/speculative.md>.
- EAGLE-3 and DFlash have documented target-specific conversion and launch
  workflows. Filename shape is not capability evidence.
- The generic MTP implementation exists, but current GLM-family support is not
  established end to end. A GLM GGUF carrying NextN tensors must remain Unknown
  unless the selected runtime both advertises the mechanism and produces direct
  drafting evidence.
- llama.cpp publishes semantic prereleases beside rapid b-numbered builds and
  states that semantic versioning is still work in progress:
  <https://github.com/ggml-org/llama.cpp/releases/>. R31 does not label that
  channel Stable.
- Runtime reconfiguration without unloading weights is still a proposal:
  <https://github.com/ggml-org/llama.cpp/discussions/25674>.
- A public high-level speculative C API is still an open request:
  <https://github.com/ggml-org/llama.cpp/issues/27469>.

## Documents

| Doc | Architectural group |
| --- | --- |
| `01-evidence-and-experience.md` | Five evidence categories, deterministic normalized outcomes, raw-evidence preservation, and the empirical experience store |
| `02-runtime-identity-and-gpu-fit.md` | Extensible capability registry, runtime/configuration identity, analytical GPU Fit, observed memory, MTP evolution, and update-channel truth |
| `03-lab.md` | Lab ownership and experiment protocol, bounded sweeps, speculative mechanisms, prefix/KV/MoE work, correctness, comparison, and explicit application |
| `04-project-state-agent-and-api.md` | Reviewable Project State, per-subtask model selection, and the Agent Local API approval contract |
| `05-live-telemetry-and-daily-use.md` | Live telemetry pop-out, restrained health notifications, Chat scroll anchoring verification, and audio feedback |
| `06-quality-and-research.md` | Measured test work plus reconfiguration, public speculative API, DFlash, MoE streaming, and reconstructable-KV watch work |
| `07-roadmap.md` | Dependencies, batches, acceptance gates, test budget, Linux checks, descope order, and explicit rejections |

## Scope traceability

No scope item is implied by an umbrella. Each row has an explicit home.

| # | Scope item | Contract |
| ---: | --- | --- |
| 1 | Normalized model-facing tool outcomes | 01 sections 1.2-1.4 |
| 2 | Empirical experience store | 01 sections 1.5-1.9 |
| 3 | GPU Fit empirical learning | 02 sections 2.3-2.7 |
| 4 | Workspace / Project State | 04 sections 4.1-4.4 |
| 5 | Per-specialist / per-subtask model selection | 04 sections 4.5-4.8 |
| 6 | Agent Local API | 04 sections 4.9-4.13 |
| 7 | Lab View | 03 sections 3.1-3.4 |
| 8 | Empirical engine-profile optimisation | 03 section 3.5 |
| 9 | General external speculative draft models | 03 section 3.6 |
| 10 | EAGLE-3 | 03 section 3.7 |
| 11 | Speculative tuning recipes | 03 section 3.8 |
| 12 | Prompt/shared-prefix reuse measurement | 03 section 3.9 |
| 13 | KV/context memory budgeting | 02 section 2.5 and 03 section 3.10 |
| 14 | MoE expert caching experiments | 03 section 3.11 |
| 15 | MoE expert prefetch / streaming research | 06 section 6.4 |
| 16 | Advanced KV-cache experiments | 03 section 3.10 |
| 17 | Speculative deterministic-equivalence validation | 03 section 3.12 |
| 18 | Lab correctness principle | 03 sections 3.2-3.4 and 3.12 |
| 19 | Extensible runtime capability registry | 02 sections 2.1-2.2 |
| 20 | GLM MTP / current llama.cpp MTP evolution | 02 section 2.8 |
| 21 | llama.cpp update channels | 02 section 2.9 |
| 22 | Live Model Telemetry pop-out | 05 sections 5.1-5.3 |
| 23 | Runtime health notifications | 05 sections 5.4-5.5 |
| 24 | Chat streaming scroll anchoring | 05 section 5.6 |
| 25 | Audio feedback | 05 sections 5.7-5.10 |
| 26 | Measured quality/coverage work | 06 sections 6.1-6.2 |
| 27 | Reconfigurable llama-server runtime | 06 section 6.3 |
| 28 | Public llama.cpp speculative API | 06 section 6.5 |
| 29 | DFlash | 03 section 3.13 and 06 section 6.6 |
| 30 | KV-Direct / reconstructable KV research | 06 section 6.7 |

## Classification

- **Mandatory spine:** outcomes, evidence taxonomy, experience storage,
  capability/runtime identity, GPU Fit breakdown and observed comparison, Lab
  protocol/correctness, and evidence-gated runtime measurement.
- **Mandatory independent groups:** Project State, explicit subtask model
  selection, telemetry, notifications, audio, scroll regression coverage, and
  measured quality work. They may land after the spine but are not silently
  absorbed into it.
- **Mandatory design, conditional execution:** Agent Local API approval
  protocol, general external drafting, EAGLE-3, GLM MTP, prompt-reuse counters,
  advanced KV types, and MoE caching. Hermaeus ships the workflow only where a
  selected runtime proves the required contract. Unknown is a valid result.
- **Research/watch:** semantic update-channel promotion, runtime
  reconfiguration, public speculative API, MoE prefetch/streaming, DFlash
  production integration, and reconstructable KV. Investigation and dated
  findings ship; unstable integration does not.

## Standing implementation rules

- Observed facts, deterministic calculations, user-provided information,
  extracted information, and model inference are distinct serialized values.
- A normalized outcome is derived by deterministic code from raw evidence. It
  cannot be supplied or overridden by a model.
- Raw evidence remains authoritative and recoverable. Derived experience keeps
  references, not lossy replacements.
- Capability means `Available | Unavailable | Unknown`, with evidence and an
  exact runtime identity. Failure to probe is Unknown, not Unavailable.
- Experience never enters `AgentSafetyGate`, approval fingerprinting,
  workspace policy, or API authorization.
- Lab never writes settings while running. Applying a result is a separate,
  explicit, reviewable action through the normal settings flow.
- No result is a universal winner. Comparisons show conditions, measurements,
  correctness, missing counters, and trade-offs.
- No new NuGet package is expected. Any exception requires a written reason
  showing why the existing BCL, SQLite, Avalonia, and current services are
  materially inadequate.
- Never store credentials, raw secrets, private environment dumps, or
  unredacted command lines in experience, Lab, telemetry, traces, exports, or
  documentation.
- Linux/COSMIC is a live release target, not only a CI compile target. Doc 07
  names the required live checks per batch.
- No em dashes. Zero-warning build. Tests remain sequential and write results
  outside the checkout.

## Recovery if implementation is interrupted

Doc 07 contains the only progress table. Update its Landed column in the same
commit as each completed batch. `git log --oneline main..HEAD`, that table, and
the persisted acceptance evidence are the recovery procedure.
