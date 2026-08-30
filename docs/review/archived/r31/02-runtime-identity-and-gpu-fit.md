# 02. Runtime identity and GPU Fit

This document makes runtime facts extensible and turns GPU Fit into an
inspectable analytical prediction that can be compared with local observation.
It does not make empirical correction a hidden new formula.

## 2.1 Extensible capability registry

Generalize `LocalModelCapabilities` and `RuntimeCapabilitySurface` behind one
Core contract:

```text
RuntimeCapabilityObservation(
  CapabilityId,
  State: Available | Unavailable | Unknown,
  EvidenceCode,
  Detail,
  RuntimeIdentity,
  ModelIdentity?,
  Parameters,
  ObservedAtUtc)
```

`CapabilityId` is a stable dotted identifier, for example:

- `speculative.draft.simple`
- `speculative.draft.eagle3`
- `speculative.draft.dflash`
- `speculative.draft.mtp`
- `speculative.ngram.mod`
- `runtime.prompt-threads`
- `runtime.performance-metrics`
- `runtime.prompt-reuse-counter`
- `runtime.kv.type.q4_0`
- `runtime.moe.cpu-placement`
- `runtime.moe.expert-cache`
- `reasoning.separate-output`
- `reasoning.preserve-template`
- `modality.vision`

The registry is data, not one property per feature. Typed feature adapters may
project convenient views for callers, but adding DFlash must not require fields
on every model/settings object. Unknown capability ids are retained and shown
as observed facts, not discarded.

Parameters are a bounded string map for evidence-backed facts such as supported
speculative type names or accepted cache types. They do not carry raw help text.
`Available` means the selected runtime positively advertised or demonstrated
the contract. `Unavailable` requires a successful authoritative probe that did
not contain it. Probe failure remains `Unknown`.

## 2.2 Runtime, model, hardware, and configuration identity

One fingerprint is not enough. Use composable identities:

- **Runtime:** kind, resolved executable hash where affordable, executable size
  and mtime, parsed `--version` build/tag/commit/compiler where reported,
  backend/build variant, and Hermaeus-managed release asset identity. Empty
  fields remain empty. Path is not part of a portable stable id.
- **Model:** resolved model manifest identity, SHA256 when already available,
  file size/mtime fallback, GGUF architecture/quantization, and companion model
  identity. Do not hash multi-gigabyte files on every launch; reuse verified
  manifest/hash evidence and expose when identity is a weaker fallback.
- **Hardware:** OS/architecture, GPU backend and device identity, VRAM totals,
  RAM total, relevant driver/runtime version when reliably available, and
  multi-device layout. Do not persist hostname, user name, home path, serial
  number, or other machine identifier.
- **Configuration:** context, GPU layers/placement, threads, prompt threads,
  slots, batch/ubatch where known, K/V type, Flash Attention choice,
  speculative mechanism/companion/parameters, MoE placement, and only parsed
  known extra arguments. Unknown extra arguments make the fingerprint
  Incomplete rather than being stored verbatim.

Extend `EmpiricalProfileFingerprint` by version rather than changing the meaning
of its current StableId. Historical v1 ids remain readable and explicitly
incomplete. New evidence uses v2 with runtime, model, hardware, and
configuration sub-identities. Capability-cache replacement must compare the
runtime identity, not only executable path, size, and mtime.

## 2.3 Analytical GPU Fit model

Retain `GgufMetadataReader` and `KvCacheMath` as the analytical foundation.
They already implement important requested facts: layer count, KV heads,
head/key/value dimensions, GQA/MQA/MHA effects through `head_count_kv`, and
sliding-window/interleaved attention metadata.

Replace the single tier/reason projection with a structured breakdown:

```text
ModelFitPrediction(
  PredictionVersion,
  Fingerprints,
  Inputs,
  WeightPlacement,
  KvAllocation,
  RuntimeAndComputeOverhead,
  CompanionAllocations,
  Headroom,
  GpuRequiredBytes,
  SystemRamRequiredBytes,
  UnknownComponents,
  Tier,
  Explanation)
```

Every component has bytes, placement (`Gpu`, `SystemRam`, `Split`, or Unknown),
origin, and explanation. Totals are not displayed with false precision when a
material component is Unknown.

## 2.4 Weight and placement calculation

- Use actual GGUF file/tensor metadata where available. File size remains the
  bounded fallback for mapped weights, clearly labelled.
- Account for `GpuLayers`, full/partial CPU offload, `CpuMoeLayers`, and known
  multi-device placement. Do not treat all model bytes as GPU-resident when the
  selected config explicitly offloads them.
- Dense and MoE models need different explanations. Do not infer active expert
  count or expert tensor placement without metadata/runtime evidence.
- External draft, EAGLE-3, DFlash, MTP sidecar, and projector files are separate
  companion allocations. Embedded NextN tensors already included in the target
  file are not double-counted.
- Memory-mapped weights may occupy address space and page cache without being
  fully resident. Report resident placement only where measured; analytical
  fallback states the assumption.

## 2.5 KV, context, overhead, and headroom

- Calculate K and V separately even though the current user setting applies one
  type to both. Preserve the ability to represent runtimes/imports with
  different K/V types.
- Use actual block count, KV heads, key/value dimensions, context length,
  sliding-window size/pattern, slot count, and GPU/CPU KV placement where the
  runtime reports it.
- Treat low-bit formats as exact only after the selected runtime advertises the
  type and the byte representation is known. Unknown/future formats do not use
  f16 silently.
- Runtime/compute overhead is a named component. Prefer parsed startup
  allocations or controlled observed baselines over another global multiplier.
  `KvCacheMath.GpuHeadroomBytes` remains the explicit fallback until evidence
  replaces it, never a hidden correction.
- Headroom is policy, not measured usage. Show the reserved bytes/percentage and
  why they exist. GPU and system-RAM headroom are separate.
- The UI offers context/cache what-if controls and shows weights, KV, runtime,
  companions, and headroom in one breakdown without saving settings.

## 2.6 Direct memory observation

Create a shared `IRuntimeTelemetrySource` used later by Lab and the live
pop-out. A sample carries value, source, timestamp, process/runtime identity,
and trust state.

Preferred evidence order:

1. structured llama-server metrics or API fields whose selected runtime
   advertises a known schema;
2. parsed startup allocation records with a build-scoped parser;
3. process working set and platform GPU-process counters;
4. whole-device counters, explicitly labelled as device totals and unsuitable
   for attributing a precise model delta;
5. Unknown.

Do not subtract two whole-GPU samples and call the delta model VRAM when other
processes can allocate concurrently. A controlled Lab run may report that delta
as a lower-trust observation with the method named; normal GPU Fit should not.

Record current and peak values only while the matching process/runtime identity
is alive. A restarted server begins a new observation series. Sampling is
bounded and deduplicated; raw high-frequency samples may be summarized into
min/max/mean/count while retaining the exact samples needed around start/load.

## 2.7 Prediction versus observation

`GpuFitExperience` links one immutable `ModelFitPrediction` to one comparable
runtime observation series under exact v2 fingerprints.

Display:

- predicted GPU and RAM totals and every component;
- observed current/peak values with source and trust state;
- signed discrepancy and percentage only when quantities are genuinely
  comparable;
- the number and recency of compatible observations;
- incompatible observations separately, with the fingerprint differences.

Bounded reuse may show the distribution of prior discrepancies for exact or
explicitly compatible fingerprints. It must not rewrite the mathematical
prediction. If an empirical correction is offered, show analytical prediction,
empirical adjustment, sample count/range, and adjusted projection as three
separate values. No correction crosses runtime build, backend, model,
configuration, or hardware identity unless a reviewed compatibility rule says
why.

## 2.8 MTP and GLM-family status

R30 currently treats positive GGUF `nextn_predict_layers` plus runtime
`draft-mtp` help as embedded MTP Available. R31 must tighten that claim:

- GGUF NextN metadata proves that weights/metadata exist, not that an
  architecture's runtime graph uses them.
- runtime help proves the binary contains a generic mechanism, not that it
  works for this architecture.
- Available for a model/runtime pair requires an authoritative model-specific
  capability response or direct drafting evidence (`draft_n` reported and a
  controlled run engaged). Until then the pair is Unknown.
- GLM-family MTP remains evidence-gated. Current upstream discussion reports
  generic MTP support but incomplete GLM graph integration:
  <https://github.com/ggml-org/llama.cpp/discussions/25175>.
- Do not hard-code a GLM exception into the generic registry. The strengthened
  evidence rule handles GLM and the next partially wired architecture alike.

The selected runtime's actual behavior wins over this dated planning note.

## 2.9 llama.cpp update channels

Keep the current SHA256-verified pinned install and Latest workflow. During
R31, record the upstream channel investigation and make release parsing capable
of representing:

- b-numbered rapid/nightly builds;
- semantic-version prereleases/releases;
- unknown tag schemes.

Do not expose `Stable` while upstream says semantic versioning is work in
progress. If upstream establishes a stable semantic contract during the round,
add `Semantic` and `LatestBuild` as explicit user choices only after verifying
asset availability, platform coverage, hashes, ordering, rollback, and
capability probing for both. Existing configured behavior must not change
silently.

## 2.10 Acceptance criteria

- A new speculative/capability id can be observed and persisted without adding
  a property to `ServerConfig`, `LocalModelCapabilities`, or settings.
- Capability state always carries evidence and exact runtime identity; failed
  probes remain Unknown.
- v1 empirical fingerprints still load; v2 separates runtime, model, hardware,
  and configuration and never includes personal paths.
- GPU Fit shows an inspectable weights/KV/overhead/companions/headroom breakdown
  and names all Unknown components.
- Sliding-window/interleaved metadata, separate K/V math, selected context,
  placement, speculative companions, and MoE placement affect the prediction
  where evidence exists.
- Observed memory retains source/trust state and never masquerades as an
  analytical input.
- Empirical comparison refuses incompatible fingerprints and never rewrites the
  base formula.
- GLM/other MTP stays Unknown until model-specific direct evidence exists.
- No update channel is called Stable on the current upstream prerelease
  contract.

## 2.11 Test and live-verification budget

Expected automated coverage: 30-40 tests.

- capability registry unknown-id round trips, probe failure semantics,
  parameters, drift, and v1 cache migration;
- v1/v2 fingerprint stability, redaction, incomplete identities, and
  compatibility refusal;
- dense/GQA/MQA/MHA, sliding/interleaved, separate K/V, partial GPU, CPU MoE,
  companion, unknown-format, multi-slot, overhead, and headroom projections;
- telemetry sample lifecycle, process restart boundary, source trust, peak
  calculation, and no whole-device misattribution;
- GPU Fit comparison exact-match and mismatch cases;
- build-tag parser covering b tags, semantic prereleases, semantic releases,
  and unknown schemes without asserting an upstream Stable channel.

Linux/COSMIC live gate:

1. Use the owner's selected Linux llama-server and one known local GGUF. Save
   `--version`, capability observations, and GPU Fit input/breakdown without
   personal paths.
2. Start with at least two materially different context/cache configurations,
   observe process RAM and trustworthy GPU evidence, and compare each with its
   own prediction.
3. Confirm a restart creates a new observation identity and an older build's
   discrepancy is not applied as current truth.
4. If no trustworthy per-process VRAM source exists on that hardware/backend,
   verify the UI says Unknown instead of displaying a device-total delta as
   model usage.
