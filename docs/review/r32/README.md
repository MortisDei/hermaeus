# Review round 32: Adaptation with receipts

Audience: the implementing agent. Read this file, then the numbered documents
in order. Doc 08 is the sequencing and owner-control contract.

## Why this round exists

R31 gave Hermaeus an evidence vocabulary, runtime and configuration identity,
an empirical experience store, analytical GPU Fit, direct telemetry, and an
isolated Lab. R32 makes those foundations useful across ordinary daily work:

`observations -> workload state -> bounded proposal -> explicit decision -> outcome`

The receipt is the important part. Hermaeus may adapt a local launch within a
user-approved envelope or recommend a better configuration, but it must show
which workload, hardware, runtime, evidence, constraints, and compromises
produced that result. It must not turn a successful probe into a universal
default, silently switch models, or hide a quality or context reduction.

R32 also gives stored knowledge a real history. Updating a fact should create a
reviewable successor with effective time and evidence, not overwrite the only
copy of what was previously believed. This is bounded temporal assertion
lineage, not a general knowledge graph or autonomous truth engine.

## Verified starting point

This contract was written against `b034182`, the `v0.38.0-beta` head of
`r32/round` and `origin/main` on 2026-08-31.

- `ModelFitPrediction` explains one selected model configuration. It does not
  reserve or total Chat, embeddings, reranking, projector, draft, speech, and
  other simultaneous consumers.
- `IRuntimeTelemetrySource` and live telemetry retain process/runtime identity,
  but there is no application-wide resource snapshot or admission decision.
- `ServerProcessManager.AutoTuneAsync` probes a fixed GPU-layer ladder and at
  most one analytically suggested context. A healthy start is its success
  condition; it does not compare whole-workload headroom, representative
  performance, correctness, or prior compatible Lab evidence.
- `EngineOptionPresets` is a static VRAM tier table. Benchmark usage insights,
  Doctor advice, Lab eligibility, GPU Fit comparisons, and tune profiles each
  produce useful but separate guidance with no shared recommendation receipt.
- The configured server row, actual launch arguments, upstream fitted values,
  and observed runtime state are not represented as four distinct identities.
- A matching `LlamaTuneProfile` is reapplied to the Services editor, copied
  into `ServerConfig`, and saved immediately before every ordinary start. That
  implicit mutation sits ahead of the argument builder but is absent from the
  current identity vocabulary.
- `MemoryStore` persists typed relationships and performs one-hop expansion.
  A `Supersedes` edge hides the older memory from ordinary recall, but memory
  updates still lack first-class revision identity, effective time, review
  state, and an as-of retrieval contract.
- Hugging Face search results are text buttons. `HfModelCard` retains revision,
  modified time, license, and downloads, but no model-card thumbnail or bounded
  artwork cache exists.
- The R31 external draft and EAGLE-3 workflow is implemented and runtime-gated.
  Its remaining real-pair exercise is a live validation gate, not unfinished
  product architecture.

## Current upstream facts checked for this contract

Checked 2026-08-31 against primary upstream sources. These describe upstream
`master` or the named release, not the capabilities of any installed binary.
Every use remains gated by the selected executable's observed help, identity,
and behavior.

- Current `llama-server` documentation says `--n-gpu-layers` accepts an exact
  count, `auto`, or `all`, with `auto` as its default. It also says `--fit`
  defaults on and may adjust unset arguments, with `--fit-target` and
  `--fit-ctx` bounding device margin and minimum context:
  <https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md>.
- `llama-fit-params` exposes the same projection as explicit CLI arguments and
  documents that fitting can reduce context, place layers across devices, and
  move MoE expert tensors to CPU:
  <https://github.com/ggml-org/llama.cpp/blob/master/tools/fit-params/README.md>.
- The server currently documents device selection, layer/row/tensor split
  modes, tensor split, main GPU, KV offload, CPU MoE and dense-FFN placement,
  context checkpoints, host cache RAM, unified KV, per-slot context limits,
  idle-slot caching, metrics, slots, and read-only `GET /props`.
- Release `v0.3.0` states that common fit now accounts for stream count and
  includes tensor-split work. Its release asset points to nightly `b10621`:
  <https://github.com/ggml-org/llama.cpp/releases/tag/v0.3.0>.
- Hugging Face model cards may declare a `thumbnail` URL in card metadata. The
  value is publisher-controlled and therefore untrusted remote input:
  <https://huggingface.co/docs/hub/en/model-cards>.

These facts establish the R32 semantic contract rather than postponing it.
Batch 0 verified the selected installed binaries against that contract and
marked unsupported capabilities unavailable or Unknown. Batch 1 now stores a
typed CPU/Auto/All/Exact intent, migrates legacy values on ordinary save,
treats tune profiles as non-authoritative evidence, renders explicit fit
ownership, and shares one configuration identity projection. Effective
placement remains an observed runtime fact and is still Unknown on a host with
no enumerated devices.

## Documents

| Doc | Architectural group |
| --- | --- |
| `01-whole-workload-resource-intelligence.md` | Resource inventory, snapshots, reservations, workload plans, attribution, and admission truth |
| `02-adaptive-local-inference.md` | Configured intent versus effective launch, upstream fit integration, bounded adaptation, recovery, cache, and multi-device policy |
| `03-experience-informed-guidance.md` | Shared recommendation receipts, evidence eligibility, review/apply/undo, and explicit model/profile choice |
| `04-temporal-knowledge-evolution.md` | Assertion revisions, effective time, contradiction review, current/as-of retrieval, and source-version lineage |
| `05-hugging-face-artwork.md` | Model-card artwork selection, untrusted-media handling, cache/privacy policy, and card presentation |
| `06-current-implementation-audit.md` | Repository-wide correctness, architecture, performance, security, UX, and test audit with scoped dispositions |
| `07-deferred-audit.md` | Disposition of every item that was open in `deferred.md` at R32 planning time |
| `08-roadmap.md` | Dependency spine, batches, verification, live gates, descope order, and explicit rejections |
| `09-adversarial-reconciliation.md` | Adversarial-review dispositions and the evidence-backed CI trigger/check topology |

## Scope traceability

| Direction | Contract |
| --- | --- |
| Adaptive local inference and VRAM intelligence | docs 01 and 02 |
| Whole-active-workload resource intelligence | doc 01, especially sections 1.2-1.7 |
| Experience-informed configuration | doc 03 |
| Explicit model and profile recommendations | doc 03 sections 3.5-3.8 |
| Temporal and evidence-grounded knowledge evolution | doc 04 |
| Hugging Face repository/model artwork | doc 05 |
| Broad current-implementation audit | doc 06 |
| Deferred backlog audit | doc 07 and `docs/review/deferred.md` |
| Current llama.cpp fit/cache/multi-device behavior | docs 01-02 and doc 08 Batch 0 |
| Duplicate branch/PR CI runs | docs 06, 08, and 09 |

## Standing implementation rules

- Configured intent, planned launch, effective launch, and observed runtime are
  distinct records. Never rewrite settings merely to describe what happened.
- Unknown resource usage is visible and prevents false precision. Whole-device
  totals are not silently attributed to one process.
- Every production allocation path enters admission at the lifecycle owner,
  not at a UI caller that another path can bypass.
- Adaptive behavior is opt-in, bounded by a saved user envelope, and recorded.
  It never changes model identity, safety authority, network exposure, or
  provider without a separate explicit user decision.
- A context, cache precision, placement, or companion change that can affect
  quality or usable capacity is shown before the envelope is enabled and in
  every launch receipt where it occurs.
- Experience can support a recommendation. It cannot approve its own
  application, change Agent risk, or become a universal score.
- Knowledge recency is not truth. A contradiction remains visible until a
  deterministic rule or explicit user review establishes lineage.
- Content-bearing memory writes go through the revision authority. Legacy
  generic upserts are not a second mutable path around temporal semantics.
- Forget/delete means content removal. Temporal history is not an excuse to
  retain content the user explicitly removed.
- Remote artwork is untrusted content. No SVG, arbitrary redirect, external
  host beacon, startup fetch, executable content, or unbounded decode belongs
  in the image path.
- No new NuGet package is expected. Any exception needs written justification.
- No em dashes. Tests remain sequential. Build output and test results stay
  outside the checkout where the documented command requires it.

## What R32 is not

R32 is not automatic model routing, a planner/executor/verifier model swarm,
continuous fine-tuning, a whole-workspace graph, GraphRAG, arbitrary multi-hop
retrieval, live mutation of a loaded llama.cpp model, or a GPU process killer.
Those boundaries are repeated in doc 07 so partial implementation cannot blur
them.

## Recovery if implementation is interrupted

Doc 08 contains the only implementation progress table. Update its Landed
column in the same commit as each completed batch. Use that table,
`git log --oneline origin/main..HEAD`, and batch evidence files as the recovery
procedure. Do not infer progress from planned documents alone.
