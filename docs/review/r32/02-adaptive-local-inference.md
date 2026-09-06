# 02. Adaptive local inference

Adaptive inference consumes doc 01's workload truth. It may choose an effective
launch within an explicit envelope. It may not silently rewrite configured
intent, choose another model, fall back from accelerated `Auto` to CPU, or call
a healthy process evidence that the selected compromise is useful.

## 2.1 Four identities, not one setting

Represent these separately:

1. **Configured intent:** the saved server/model/profile fields the user chose.
2. **Planned launch:** the exact bounded changes proposed against a resource
   snapshot.
3. **Effective launch:** the arguments and placement the runtime actually used,
   including upstream fit adjustments observed from a supported contract.
4. **Observed runtime:** health, allocation, context, throughput, cache state,
   and process identity after launch.

The configured server remains unchanged unless the user explicitly applies a
recommendation through normal settings. An adaptive launch receipt can say
`configured context 32768, effective context 16384`; it must never save 16384
back to settings merely because startup succeeded.

Extend configuration/runtime fingerprinting by version so each receipt keys on
the effective values. Historical fingerprints remain readable and incomplete.

## 2.2 Typed placement intent and legacy migration

Replace the overloaded `int GpuLayers` preference with a versioned shape:

```text
GpuPlacementIntent(
  Kind: Cpu | Auto | All | Exact,
  ExactLayerCount?)
```

The settings migration is semantic and deterministic:

- legacy `GpuLayers == 0` becomes `Cpu`;
- legacy `GpuLayers == -1` becomes `All`;
- legacy `GpuLayers > 0` becomes `Exact(N)`;
- values below `-1` are invalid and block launch with a repair action;
- absence of the new field never becomes `Auto`, because old JSON absence
  deserialized to CPU;
- new accelerated setup may choose `Auto` only after the user selects that
  behavior and the chosen runtime proves support. Existing settings do not.

During one compatibility schema version, read the legacy integer only when the
new field is absent and write only the new shape on the next ordinary
owner-initiated settings save. Do not rewrite settings merely because the app
started. Fingerprints record the migration origin.

Tune profiles receive the same legacy mapping, but they are evidence, not a
hidden higher-precedence setting. Current `ServicesViewModel.StartCoreAsync`
calls `ApplyTuneProfileIfAvailable`, copies profile values into the editor,
saves them, and then starts. R32 removes that implicit start-time application.
A matching profile may order an adaptive candidate or produce an explicit
Apply recommendation. Values already saved in a server remain configured
intent and are not reverted.

## 2.3 Authoritative precedence chain

Every managed launch follows this order, with one canonical projection object:

1. load and validate configured server intent, including the migrated
   CPU/Auto/All/Exact placement;
2. attach compatible tune/Lab/adaptive experience as evidence only;
3. apply one reviewed Hermaeus adaptive overlay within the saved envelope, or
   no overlay in Fixed mode;
4. decide which fields, if any, upstream fit owns and explicitly pin every
   other allocation/quality field;
5. render one conflict-free argument vector and launch it;
6. derive effective values from structured runtime output or a parser scoped
   to the exact runtime identity;
7. correlate per-device observations and retain configured, planned,
   rendered, effective, and observed receipts.

`ExtraArgs` never creates an eighth precedence layer. Core identity, safety,
network, placement, context, slot, and lifecycle flags are typed-owned. A
duplicate alias is canonicalized only when its value agrees; a conflict is a
save/launch error. Recognized non-core flags enter the configuration identity.
Unknown extras are retained as a bounded hash/list and make the identity
Incomplete.

Placement rendering is fixed by intent:

| Intent | Fit ownership | Required semantic result |
| --- | --- | --- |
| `Cpu` | fit off | explicit zero GPU layers using the selected runtime's proven form |
| `Auto` | fit on for placement only | placement fields left unset; configured context and every disallowed field pinned |
| `All` | fit off | explicit all layers using `all` or the proven legacy equivalent |
| `Exact(N)` | fit off | explicit exactly N layers |

When Hermaeus adapts All/Exact to a lower exact count, the planned overlay owns
that exact count and upstream fit is off. When an enabled multi-device Auto
plan deliberately delegates layer/tensor placement, fit may own only those
unset fields and receives the per-device target from doc 01. CPU intent cannot
be upgraded, and accelerated Auto cannot degrade to CPU.

## 2.4 Batch 0 installed-runtime verification

Before adding adaptive behavior, verify every selected supported runtime and
the current argument builder against sections 2.2-2.3:

- what omitted `--n-gpu-layers` means;
- accepted `0`, `auto`, `all`, and exact numeric forms;
- whether `--fit` exists, its default, and which arguments it may change;
- whether explicit values are preserved by fit;
- `--fit-target`, `--fit-ctx`, stream/slot accounting, and emitted diagnostics;
- device listing and split-mode syntax;
- KV offload, host cache, checkpoints, unified KV, and per-slot context syntax;
- whether effective values are exposed through structured `/props`, metrics,
  slots, or only build-scoped logs.

The contract does not wait on Batch 0. A selected binary that cannot preserve
or audit a requested intent reports that capability Unavailable/Unknown and
does not offer that mode. Add a regression matrix for older compatible
binaries and current managed binaries.

Do not enable upstream fit based on build number or filename. Add capability
ids such as `runtime.fit`, `runtime.fit.target`, `runtime.fit.minimum-context`,
`runtime.fit.report.effective`, `runtime.device.list`, and the specific cache or
split capabilities. Failure to probe is Unknown.

## 2.5 Adaptive envelope

Add a settings-owned envelope to each local managed server, default off:

```text
AdaptiveInferenceEnvelope(
  Mode: Fixed | Advise | AdaptAtLaunch,
  MinimumContext,
  MinimumGpuHeadroomBytes,
  AllowGpuLayerReduction,
  AllowContextReduction,
  AllowKvPrecisionChange,
  AllowCpuMoePlacement,
  AllowMultiDevicePlacement,
  PreserveAcceleratedBackend,
  PreferredEvidenceAge)
```

`Fixed` preserves today's explicit launch behavior after the semantic audit.
`Advise` produces a plan but launches only after the user reviews/applies it.
`AdaptAtLaunch` may apply only changes allowed by the saved envelope and records
every effective difference.

Defaults are conservative: no context reduction, no KV precision change, no
CPU-MoE placement, no multi-device placement, preserve accelerated backend,
and a platform-appropriate explicit headroom. The UI may offer well-explained
presets, but stores the expanded fields rather than an opaque mode name.

An envelope never permits model replacement, provider replacement, remote
fallback, network-binding changes, speculative mechanism changes, projector
removal, slot-count increase, or CPU fallback from configured/managed `Auto`.

## 2.6 Deterministic candidate construction

Build candidates from the configured intent, resource plan, GGUF facts,
runtime capabilities, and compatible empirical evidence. Use a deterministic
order and explain every candidate.

The first R32 candidate families are:

1. configured launch unchanged;
2. reduce GPU layers while preserving configured context, when allowed;
3. reduce context along a named ladder no lower than `MinimumContext`, when
   allowed;
4. use only runtime-advertised K/V types with existing quality evidence, when
   KV precision change is allowed;
5. place known MoE expert tensors on CPU while keeping attention offload, when
   model metadata and runtime capability support it;
6. use proven multi-device placement, only when multiple devices are identified
   and the user enabled it.

Do not brute-force the Cartesian product. The planner uses analytical
elimination, prior compatible failed/successful launch evidence, and bounded
candidate caps. Lab remains the place for controlled performance/correctness
sweeps. Normal startup is not a benchmark suite.

## 2.7 Upstream fit integration

Current llama.cpp fit is useful because it understands backend allocation and
can account for stream count. It is not the application-wide planner.

Use it under these rules:

- Hermaeus first reserves headroom for the rest of the active workload. Pass a
  fit target derived from the user envelope and whole-workload plan, not merely
  upstream's default margin.
- Pin every value the envelope does not permit fit to change. Pass `--fit off`
  whenever Hermaeus or configured intent owns all placement fields; do not rely
  on an upstream default.
- Set the minimum context explicitly when context reduction is allowed.
- Prefer a structured effective-configuration response. If only logs expose
  the result, use a runtime-identity-scoped parser and retain raw bounded lines
  as evidence. Parser failure means effective configuration Unknown.
- A runtime that accepts `--fit` but cannot report what it changed may be used
  only in `Advise`/Lab investigation until the effective launch is auditable.
- `llama-fit-params` is optional capability evidence, not a new required binary.
  Do not assume managed archives contain it.
- Hermaeus prediction and upstream projection are kept side by side. A
  discrepancy is evidence for review, not permission to silently tune a
  constant.

## 2.8 Startup, recovery, and stability

For `AdaptAtLaunch`:

1. create the doc 01 plan and reservation;
2. select the first allowed candidate;
3. launch through the existing process owner;
4. require health plus an auditable effective configuration;
5. capture bounded post-start observations;
6. record the outcome and release/finalize the reservation.

If allocation/startup fails, try the next deterministic candidate only when
the failure evidence is compatible with resource exhaustion and the envelope
permits the next compromise. Executable missing, model corruption, invalid
arguments, authentication, unsafe paths, and unknown failures do not trigger a
resource fallback.

Limit attempts. Preserve the original failure and each candidate receipt.
Never loop indefinitely or leave several processes alive. Existing owned-child
cleanup, loopback binding, safe arguments, and health identity remain mandatory.

Before the next candidate, await complete teardown, release the failed lease,
capture a fresh resource snapshot, and revalidate the remaining candidate plus
its evidence and envelope. Acquire a new reservation against that snapshot.
If workload identity changed, headroom became Unknown, or the candidate no
longer fits, stop or replan visibly. Never reuse free-memory or reservation
state from the failed attempt.

Do not adapt a running server in R32. Current upstream `/props` supports global
property changes only when enabled, and the required model/context placement
changes remain restart-scoped. Normal Services restart semantics and explicit
unsaved-change review remain authoritative.

## 2.9 Experience and hysteresis

Compatible direct observations can reduce repeated failed probes:

- exact runtime, model, hardware, and relevant effective configuration;
- equivalent whole-workload consumer set and headroom policy;
- non-stale model/runtime files and capability observation;
- successful health plus known effective placement;
- optional Lab correctness/performance evidence when the proposal changes KV
  precision or another quality-sensitive field.

A prior success may order candidates. It does not guarantee current free memory
or skip the new snapshot. A prior resource failure may exclude an identical
candidate only while identities and failure class remain compatible.

Use hysteresis so transient free-memory changes do not make consecutive starts
oscillate between nearby contexts/layer counts. Prefer the most recent proven
effective launch within a bounded margin; switch only when it no longer fits or
a materially better evidence-backed plan is available. Persist the reason.

## 2.10 Context checkpoints, cache RAM, and multi-device placement

These deferred controls now fit naturally, but only as capability-gated parts
of resource planning.

### Host prompt cache and context checkpoints

- Account for host cache RAM and checkpoint retention in system-memory plans.
- Do not enable them merely because help advertises flags. Lab must demonstrate
  the target workload's reuse benefit and restart/correctness behavior.
- Recurrent/hybrid models remain Unknown unless selected-runtime behavior is
  directly established.
- Cache content follows existing local privacy expectations. Clear/retention
  controls and data-root backup semantics must be explicit before persistence.
- Never infer reused-token counts from timing.

### Multi-device placement

- Enumerate devices from runtime-supported output and correlate them with the
  hardware snapshot without serial numbers.
- Treat `layer`, `row`, and experimental `tensor` split as distinct capability
  and evidence contracts.
- Use per-device totals, margins, and observed allocations. One aggregate VRAM
  number is insufficient.
- No default tensor split. It requires explicit enablement, a reviewable plan,
  and one real multi-device correctness/performance gate.

Backend sampling remains outside normal launches. Its upstream flag is marked
experimental and R32 has no stable machine-readable diagnostic contract that
earns production use.

## 2.11 Effective-placement audit

An effective-launch receipt contains, per field and per device:

- configured intent, Hermaeus overlay, rendered argument/source, effective
  value, observation, and evidence state `Proven | Inferred | Unknown`;
- exact runtime executable/build/help fingerprint and parser version;
- upstream-fit enabled state, target/minimum context, and every field delegated
  to fit;
- requested and effective context, layer count, split mode/tensor split,
  device ids, KV placement/types, CPU-MoE placement, companions, and slots;
- bounded startup log evidence ids and `/props`/slot/metric observation ids,
  without raw command lines or user paths.

Structured runtime facts win over a compatible build-scoped parser. Logs can
prove only the fields their parser recognizes. A healthy endpoint does not turn
an unaudited placement into Proven. Any field fit was allowed to change but
cannot be observed makes the effective configuration Unknown and prevents
AdaptAtLaunch acceptance for that candidate.

## 2.12 Acceptance criteria

- Explicit CPU, automatic placement, all layers, and exact layer count preserve
  their saved meanings across every supported runtime in the test matrix.
- Legacy server and tune-profile integers migrate exactly, and a tune profile
  cannot mutate configured settings merely because Start was invoked.
- Fixed mode launches the audited configured semantics without adaptive
  changes.
- Advise mode never mutates settings or launches a changed configuration.
- AdaptAtLaunch changes only allowed fields, never goes below the envelope, and
  records configured/planned/effective/observed identities distinctly.
- Resource failures can trigger a bounded fallback; unrelated/Unknown failures
  cannot.
- Every fallback uses a refreshed snapshot and new reservation.
- Upstream fit cannot make an unauditable context or placement change.
- Compatible experience can reorder candidates but cannot bypass a fresh
  workload snapshot or safety/lifecycle checks.
- A quality-sensitive recommendation cannot auto-apply without compatible
  correctness evidence.
- Cache/checkpoint and multi-device controls remain absent or Unknown when
  their exact runtime contract is not proven.

## 2.13 Test and live-verification budget

Expected automated coverage: 35-45 tests.

- runtime-help/argument semantic matrices, especially explicit CPU and fit;
- legacy placement migration, invalid values, tune-profile non-authority, and
  canonical precedence/ExtraArgs conflicts;
- envelope defaults, validation, serialization, and forbidden changes;
- deterministic candidate order, caps, headroom, and Unknown behavior;
- fit target/minimum context construction and effective-result parsing by
  exact runtime identity;
- failure classification, bounded recovery, cleanup, cancellation, and no
  duplicate process;
- stale-plan rejection and workload refresh between resource candidates;
- compatible/stale experience, hysteresis, and no settings mutation;
- checkpoint/cache RAM accounting and multi-device per-device plans.

Owner live gates:

- current managed CUDA and Vulkan builds on the Linux GTX 1660 Super host;
- current managed Windows CUDA build;
- Fixed explicit CPU and accelerated Auto behavior;
- one constrained-VRAM adaptive start showing configured versus effective
  context/layers and preserved whole-workload headroom;
- one resource failure and bounded recovery with no orphan;
- host-cache/checkpoint behavior on a repeated-prefix workload if that batch
  ships;
- real multi-device validation only on hardware where two supported devices
  are available. Otherwise the feature remains Unknown and unapplied.
