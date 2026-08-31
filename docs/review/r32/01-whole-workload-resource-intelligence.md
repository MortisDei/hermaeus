# 01. Whole-workload resource intelligence

This is the dependency root for R32. Adaptive inference and recommendations may
consume its facts. They may not invent a second resource inventory, silently
attribute device totals, or treat a configured value as observed usage.

## 1.1 The problem with model-only fit

Current GPU Fit answers whether one selected configuration appears to fit. The
desktop can simultaneously hold or start:

- Chat model weights, KV, compute buffers, projector, and speculative companion;
- a dedicated embeddings server and its KV/compute state;
- the in-process ONNX reranker;
- in-process Whisper speech-to-text sessions;
- local text-to-speech model/session state where the provider uses it;
- isolated Lab processes;
- other owned managed servers;
- unrelated device allocations that Hermaeus can observe only as a device
  total.

A useful fit decision must describe the active and requested workload together.
It must also retain Unknown for consumers whose process or device allocation
cannot be attributed reliably.

## 1.2 Resource consumer registry

Add one Services-owned registry behind a Core contract. A consumer descriptor
is stable product data, not a `ViewModel` snapshot:

```text
ResourceConsumerDescriptor(
  ConsumerId,
  Kind,
  Owner,
  OwningLifecycleService,
  PriorityClass,
  Reclaimability,
  SupportedResourceKinds[])
```

Initial `Kind` values are `ChatRuntime`, `EmbeddingRuntime`, `ManagedRuntime`,
`LabRuntime`, `Reranker`, `SpeechToText`, `TextToSpeech`, and `ExternalDeviceUse`.
Projectors and draft models are allocations within the owning runtime, not fake
processes. New kinds append without changing old serialized meanings.

`Owner` distinguishes Hermaeus-owned process, in-process component, configured
external endpoint, and unrelated/unknown device use. External endpoints do not
consume local VRAM merely because a URL exists. A loopback endpoint is not
claimed as owned unless process identity proves it.

The descriptor is immutable registration metadata. Mutable state belongs to an
allocation, not the consumer row. `ConsumerId` is a stable logical role such as
the configured Chat server id or `rag.reranker`; it is not a process id and is
never reused for a different owner.

## 1.3 Allocation ownership and per-device accounting

Every attempt or resident instance has exactly one allocation owner:

```text
ResourceAllocation(
  AllocationId,
  ConsumerId,
  AttemptId?,
  LifecycleState,
  RuntimeIdentity?,
  ModelIdentities[],
  ConfigurationIdentity?,
  ProcessIdentity?,
  Components[],
  StartedAtUtc?,
  Evidence[])

AllocationComponent(
  ComponentId,
  Kind,
  DeviceId?,
  PredictedBytes?,
  ReservedBytes?,
  ObservedBytes?,
  EvidenceState)
```

`LifecycleState` distinguishes planned, starting, active, idle, stopping,
released, failed, and unavailable. Configured is descriptor state, not an
allocation. Idle is not zero usage. Model weights, KV, compute buffers,
projector, draft model, ONNX session, and host cache are child components of
one allocation and cannot also appear as independent top-level consumers.

`DeviceId` is a snapshot-scoped stable key produced by the runtime/platform
adapter from backend plus enumerated device index and non-secret device facts.
Persist a bounded hardware fingerprint, not serial numbers. A component with
unknown placement stays unassigned and prevents a per-device Fits result. An
aggregate observation is never divided across devices or consumers.

Whole-device use and confidently attributed allocations are recorded
separately. Any residual is a snapshot calculation with lower/upper-bound and
Unknown state, not a synthetic process allocation. This prevents double
counting the same runtime log, process counter, and device total.

## 1.4 Resource observations

Use a common bounded observation shape:

```text
ResourceObservation(
  ResourceKind,
  ValueBytes?,
  CapacityBytes?,
  Scope,
  ConsumerId?,
  DeviceId?,
  Source,
  TrustState,
  ObservedAtUtc,
  EvidenceCode)
```

Initial resource kinds are device memory, system resident memory, system commit
or swap pressure where available, model weights, KV allocation, runtime/compute
overhead, host prompt cache, and companion allocation. CPU utilization and GPU
utilization remain telemetry, not byte budgets.

Evidence order for device memory remains the R31 rule:

1. a known structured runtime or platform process counter bound to exact
   process/runtime identity;
2. a build-scoped parsed allocation with exact runtime identity;
3. a platform process counter whose limitations are explicit;
4. whole-device used/free totals, labelled as device scope;
5. Unknown.

Windows WDDM per-process VRAM remains Unknown until a trustworthy counter is
demonstrated. Do not subtract whole-device snapshots and assign the delta to a
process when other allocators can race.

Every observation names its producer and observation id. A component selects
one authoritative observation per resource/device according to the evidence
order above. Lower-ranked observations remain corroborating evidence and are
not summed.

## 1.5 Snapshot and workload plan

A snapshot is immutable and short-lived:

```text
ResourceSnapshot(
  SnapshotId,
  HardwareIdentity,
  CapturedAtUtc,
  Consumers[],
  Observations[],
  Unknowns[],
  DeviceTotals[])
```

A workload request combines the snapshot with one proposed change. Examples:
start Chat, start embeddings while Chat remains active, open Whisper, enter a
Lab run, or restart Chat with a larger context.

The planner produces:

```text
WorkloadPlan(
  PlanId,
  SnapshotId,
  RequestedConsumer,
  ExistingConsumers[],
  ProposedAllocations[],
  PreservedReservations[],
  UnknownComponents[],
  HeadroomPolicy,
  Feasibility,
  Alternatives[],
  DerivationVersion)
```

`Feasibility` is `Fits`, `FitsWithBoundedAdaptation`, `DoesNotFit`, or `Unknown`.
Unknown is not DoesNotFit and must not trigger a destructive recovery.

Analytical allocations reuse `ModelFitPredictor`, `KvCacheMath`, model/runtime
identities, and companion handling. The whole-workload layer composes those
breakdowns. It does not duplicate their formulas. Observed active allocations
take precedence only for the exact compatible consumer identity and keep the
analytical prediction beside them.

The plan records an allocation ownership map. Publication fails if two
components claim the same `(AllocationId, ComponentId, ResourceKind,
DeviceId)` or if a child component is also registered as a top-level
allocation.

## 1.6 Reservations and priorities

Headroom is explicit policy. Keep separate reservations for:

- operating-system/device stability;
- active interactive Chat;
- a requested foreground action;
- in-process components that cannot be cheaply unloaded;
- unknown/unattributed device use.

Initial priority classes are `Interactive`, `Foreground`, `Background`, and
`Experiment`. They describe planning preference, not permission to kill or
rewrite anything. Chat is normally Interactive, a user-started Lab run is
Foreground, embedding backfill is Background, and controlled Lab sweeps after
their active case are Experiment.

No automatic stop, eviction, model unload, process kill, or provider switch in
R32. A plan may propose that the user stop a lower-priority consumer. It cannot
perform that action as a side effect of admission.

## 1.7 Admission and concurrency

One resource coordinator serializes planning decisions for Hermaeus-owned local
consumers. It does not become the lifecycle owner. Existing process/session
services still start and stop their own workloads, but their allocation entry
points require an admission lease. Admission is enforced at those owners, not
only in Services/Lab commands.

The mandatory production paths are:

- manual, auto-start, model-switch, restart, and resume starts of every managed
  llama.cpp server;
- isolated Lab session creation, which must use the injected managed-runtime
  factory instead of constructing `ServerProcessManager` directly;
- in-process reranker, Whisper, and Kokoro ONNX session creation;
- local XTTS/Kokoro process starts where their CPU/RAM allocation is in scope;
- background embedding backfill/warm-up before it acquires resident or
  foreground-conflicting resources.

`ServerProcessManager.StartAsync` and each in-process `EnsureLoadedAsync`
equivalent have no public production overload that can allocate without a
valid lease or an explicitly named bootstrap/test capability. Compile-time
constructor/factory boundaries and architecture guards pin that rule. A caller
cannot bypass it by skipping a ViewModel helper. Health probes and clients that
only use an already-owned remote endpoint do not acquire a second allocation.

Fixed-mode starts still pass through admission. They retain configured launch
semantics and may record `Unknown`; the coordinator does not adapt them. A
definite reservation conflict may refuse the concurrent start, while an
Unknown alone follows the documented Fixed-mode review/failure behavior rather
than silently changing settings.

The safe sequence is:

1. capture a fresh snapshot;
2. build a plan for the requested consumer;
3. refuse or request review when the plan is Unknown or exceeds the envelope;
4. acquire a short-lived reservation tied to the plan and caller;
5. pass the lease to the existing owner and transition it to Starting;
6. observe actual allocation and health;
7. convert the lease to the active allocation on success, or release it on
   every failure path.

Reservations are in-memory coordination tokens, not claims that VRAM has been
physically reserved. They prevent Hermaeus from concurrently approving two
plans against the same stale free-memory snapshot. They expire, cancel, and
release deterministically.

External device use may change after planning. A successful plan is not a
guarantee that the driver will allocate memory. Startup failure remains an
observed result and feeds the adaptive recovery contract in doc 02.

An adaptive retry never reuses this plan blindly. After the failed process is
fully torn down, its lease is released, a new snapshot is captured, and the
next candidate is revalidated and reserved against current workload state.

## 1.8 Whole-workload UI

Replace isolated fit language where the app is making a launch decision with a
compact Workload Fit receipt:

- active and requested consumers;
- per-consumer predicted and observed GPU/RAM allocations;
- device total, headroom, and unattributed use;
- Unknown components and why they are unknown;
- the requested launch and any proposed compromise;
- snapshot age and exact model/runtime/config identities.

The model download card may still show model-only fit before download, because
no active runtime configuration exists yet. Label it `Model fit estimate`, and
offer a whole-workload preview only after a concrete server role/configuration
is selected. Do not imply that a downloaded file alone reserves memory.

System Overview becomes the natural inspection surface for the complete active
workload. Services and Lab consume a smaller plan/receipt projection.

## 1.9 Persistence and experience

Persist only decisions and bounded evidence worth comparing, not a continuous
hardware surveillance log.

Add an `adaptive-launch` empirical experience domain containing the requested
workload identity, snapshot summary, plan, effective launch, startup outcome,
and bounded peak observations. Existing `gpu-fit-observation` and `lab-run`
records remain authoritative for their domains.

Do not persist hostnames, serials, absolute user paths, raw command lines,
environment dumps, unrelated process names, or per-second idle telemetry. An
external-use observation persists only device id, aggregate bytes, source,
timestamp, and trust state.

## 1.10 Acceptance criteria

- Every local AI consumer is either registered, proven remote, or explicitly
  Unknown. No configured endpoint is assumed local or resident.
- Chat plus embeddings plus at least one in-process ONNX consumer produces one
  composable plan without double-counting model/companion allocations.
- Two concurrent plan requests cannot both reserve the same apparent headroom.
- Every resident component has one allocation owner and at most one selected
  observation for each resource/device; duplicate evidence is not summed.
- Direct construction or start of a managed process/ONNX session cannot bypass
  admission in production composition.
- Cancellation, startup failure, timeout, and normal completion release the
  reservation and retain an honest receipt.
- Whole-device totals never become process-attributed evidence.
- Missing per-process VRAM yields useful RAM/analytical facts plus explicit
  Unknown, not a fabricated zero.
- Model-only download estimates remain distinct from whole-workload launch fit.
- No planning result can stop a process, change a provider, or bypass an
  existing lifecycle/approval owner.

## 1.11 Test and live-verification budget

Expected automated coverage: 30-40 tests.

- registry identity, local/remote/owned distinctions, and lifecycle state;
- allocation/component uniqueness, per-device ownership, residual Unknown, and
  duplicate-observation suppression;
- snapshot composition, double-count prevention, Unknown propagation, and
  analytical/observed compatibility;
- headroom/reservation math, concurrent requests, expiry, cancellation, and
  release on every failure path;
- architecture/DI guards over manual, auto-start, restart, model-switch, Lab,
  voice resume, ONNX load, and background-work entry points;
- workload plans for Chat only, Chat plus embeddings, companions, reranker,
  Whisper, Lab, and unrelated device pressure;
- redacted persistence and data-root switching;
- UI labels that distinguish model estimate, workload plan, and observation.

Owner live gates on Linux/COSMIC and Windows:

- capture Chat plus GPU embeddings, then start and stop one in-process AI
  component while observing the workload receipt;
- introduce known external GPU pressure and confirm it remains unattributed;
- confirm Windows retains per-process GPU Unknown if no trustworthy source is
  available;
- exercise a cancelled and a failed launch and confirm no stale reservation or
  ghost active consumer remains.
