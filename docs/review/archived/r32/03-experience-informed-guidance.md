# 03. Experience-informed configuration and recommendations

R31 stored evidence and displayed several independent advisories. R32 defines
one recommendation contract so GPU Fit, Lab, Benchmarks, usage history, runtime
health, and adaptive launches can explain a proposed change consistently.

A recommendation is a reviewable proposal. It is not authority, a universal
winner, or a substitute for compatible evidence.

## 3.1 Existing seams to converge

The current implementation already has useful pieces:

- Benchmark Insights requires minimum runs/cases and comparable hardware, then
  can report model rankings and usage-aware gaps.
- Doctor repeats a bounded default-model advisory without auto-switching.
- Lab identifies correctness-eligible candidates and exposes Apply separately.
- GPU Fit compares analytical prediction with direct observation.
- tune profiles retain a verified start configuration keyed to a model file.
- `EngineOptionPresets` proposes context/KV settings from VRAM tiers alone.

The problem is not absence of advice. It is inconsistent identity, eligibility,
wording, freshness, and apply behavior. R32 replaces no useful evidence store.
It adds a common typed projection and receipt over them.

## 3.2 Recommendation record

Add a Core-owned, provider-neutral shape:

```text
ConfigurationRecommendation(
  RecommendationId,
  Kind,
  TargetIdentity,
  CurrentConfigurationIdentity,
  ProposedPatch,
  EvidenceReferences[],
  Conditions[],
  Tradeoffs[],
  Eligibility,
  EvidenceWindow,
  DerivationVersion,
  CreatedAtUtc,
  ExpiresAtUtc?,
  Status)
```

Initial `Kind` values are `RuntimeConfiguration`, `DefaultModel`,
`WorkloadPlacement`, `Retest`, and `ResourceConflict`.

`ProposedPatch` is a typed, allowlisted change document owned by the target
domain. It is never arbitrary JSON Patch and never contains a secret. A model
change names an existing visible `ModelProfile` identity. A runtime change may
name context, GPU layers, threads, prompt threads, K/V type, Flash Attention,
CPU-MoE placement, slots, or adaptive envelope fields only where the relevant
domain supports them.

`Eligibility` is `Actionable`, `ReviewOnly`, `InsufficientEvidence`,
`Contradicted`, or `Stale`. Do not invent a numeric confidence percentage. Show
the evidence count, compatibility rules, missing facts, and trade-offs instead.

`Status` is `Current`, `Accepted`, `Dismissed`, `Superseded`, or `Expired`.
Accept/dismiss history is useful for avoiding repeated noise, but dismissal does
not delete the underlying evidence.

## 3.3 Evidence eligibility

Each recommender declares exact requirements. Shared baseline rules:

- runtime, model, hardware, and configuration identities must be compatible
  for runtime-setting guidance;
- benchmark model comparisons require the existing shared-case, run-count,
  case-count, hardware, and staleness rules;
- usage history establishes frequency, not quality;
- a successful start establishes launch viability, not response quality or
  sustained performance;
- a Lab speed result is actionable only when its protocol requires and obtains
  the relevant correctness/equivalence result;
- analytical fit may propose investigation but direct compatible observation
  is stronger;
- missing evidence is listed. It is not substituted with a default;
- model inference may summarize or propose, but it cannot upgrade eligibility.

Recommendations retain references to immutable/raw evidence. Do not copy large
Lab runs, benchmark outputs, or telemetry series into recommendation rows.

Freshness is evaluated at one recorded `EvaluatedAtUtc` from evidence event
time, never file access time or the recommendation row's update time. Each rule
declares a maximum age per evidence kind. Identity drift invalidates evidence
regardless of age.

Eligibility uses this deterministic precedence:

1. missing target or target/configuration identity drift: `Stale`;
2. deleted/revoked required evidence: `InsufficientEvidence`;
3. compatible hard correctness failure or unresolved contradictory direct
   evidence: `Contradicted`;
4. expired required evidence: `Stale`;
5. missing minimum run/case/correctness facts: `InsufficientEvidence`;
6. otherwise the rule may produce `ReviewOnly` or `Actionable`.

Within an eligible rule, exact compatible direct observation outranks exact
controlled Lab evidence, then compatible benchmark evidence, analytical
prediction, and static heuristic. This order chooses the evidentiary basis; it
does not let a newer weak observation erase an older contradiction. Timestamps
break ties only between otherwise equivalent evidence.

## 3.4 Deterministic derivation registry

Use one registry keyed by recommendation kind and target domain. Every rule has
a derivation version and produces stable evidence/condition codes.

Minimum R32 rules:

- **Retune after identity drift:** model file, runtime, driver/backend, or
  relevant hardware identity changed since the tune/Lab evidence.
- **Use a compatible proven launch:** repeated compatible adaptive starts show
  one effective configuration avoids a resource failure while preserving the
  envelope.
- **Review a Lab winner:** exactly one candidate is correctness-eligible and
  materially improves the selected named objective without an excluded
  trade-off.
- **Review context/KV placement:** whole-workload plan shows a resource conflict
  and compatible Lab evidence supports a bounded alternative.
- **Review default model:** existing benchmark comparison thresholds and usage
  facts support the candidate, with shared cases and hardware stated.
- **Retest, do not recommend:** evidence is stale, contradictory, tied, lacks
  correctness, or comes from an incompatible workload/runtime.

Do not collapse throughput, quality, context, memory, latency, and stability
into one hidden score. A recommendation names its objective and exposes every
material regression. Ties produce no winner.

## 3.5 Experience-informed model guidance

R32 may make explicit model choice better without implementing automatic
routing.

For Chat, RAG, and Agent task categories, show:

- which model is currently used and how often;
- which comparable model has relevant benchmark evidence;
- shared protocol/case basis, age, hardware, runtime, and configuration;
- quality, latency/throughput, context, and resource trade-offs;
- whether a candidate is already downloaded/configured and available;
- a direct route to inspect evidence and a separate review/apply action.

Agent subtask model selection remains explicit at plan approval. A
recommendation may annotate the selector with compatible evidence. It cannot
populate or change a child model after approval and cannot choose a
planner/executor/verifier chain.

RAG embedding and reranker recommendations require their own task evidence.
Chat-generation benchmarks do not establish embedding or reranking quality.

## 3.6 Review, apply, and undo

Use one review-card vocabulary across Services, Models, Lab, Benchmarks, and
Doctor links:

- current versus proposed values;
- why now;
- evidence and missing evidence;
- expected benefit and trade-offs;
- restart/download/storage implications;
- freshness and exact target identity;
- Apply, Dismiss, Inspect evidence, and Retest actions as appropriate.

Apply flows through the existing owning service and `SettingsService`. It
validates a staleness token over the current configuration and target identity.
If anything changed since derivation, refuse and regenerate.

Before applying, retain a bounded rollback snapshot of only the changed fields
and their target identity. Undo is another explicit settings transaction and is
refused if later edits make the snapshot stale. Applying a server change does
not silently restart a running process. Existing restart review remains in
control.

An adaptive launch receipt can offer `Apply effective values as configured`.
That creates a fresh recommendation against current settings; it does not copy
runtime output directly into settings.

## 3.7 Persistence and privacy

Use dedicated additive recommendation tables in the Services-owned
`experience.db`, implemented by `SqliteRecommendationStore` through the
existing Services migration runner. Do not encode recommendations as an
`EmpiricalExperience` domain: the current experience contract has immutable
Current/Superseded evidence rows, while recommendations need target/patch
uniqueness, mutable review status, dismissal suppression, expiration,
evidence invalidation, and rollback/apply recovery.

Minimum normalized tables are:

- `configuration_recommendations` for target/current/proposed fingerprints,
  derivation, eligibility, status, timestamps, and a canonical patch hash;
- `recommendation_evidence` for bounded evidence ids/kinds and required state;
- `recommendation_decisions` for Apply/Dismiss/Undo actor, expected settings
  fingerprint, and result code;
- `recommendation_rollbacks` for the bounded typed pre-image and post-apply
  fingerprint, never a whole settings document.

The uniqueness key is target identity, current configuration identity,
canonical proposed patch, and derivation version. Dismissal and supersession
are durable rows, not in-memory UI state. Data-root switching, backup, redacted
export, evidence deletion, and retention behavior are part of this store's
contract.

Persist typed bounded patches, opaque identities, evidence references,
decision status, and timestamps. Do not persist raw prompts, generated answers,
absolute paths, environment state, or recommendation prose as authority. Prose
is regenerated from stable codes where practical.

Deleting source evidence makes dependent recommendations non-actionable and
explains why. It does not preserve deleted content in the recommendation.

Settings and SQLite cannot commit atomically together. Apply therefore records
a pending decision with the expected current fingerprint and bounded rollback,
uses the owning service plus `SettingsService` atomic write, then records the
post-apply fingerprint. Startup reconciliation marks an interrupted operation
Accepted only when current settings exactly equal the proposed fingerprint,
returns it to Current when they equal the pre-image, and otherwise marks it
Superseded/Stale for review. It never reapplies a patch during recovery.

## 3.8 Noise control

- Do not generate the same current recommendation repeatedly. Group by target,
  current identity, proposed patch, and derivation version.
- A dismissal suppresses the identical proposal until material evidence or
  identity changes.
- Doctor shows at most one summary per actionable domain and links to the
  detailed review. It does not become a recommendation inbox.
- Recommendations are refreshed after relevant Lab/benchmark/adaptive outcomes
  and on explicit inspection, not by a constant background model loop.
- Insufficient-evidence and Retest items belong in Lab/Benchmarks, not as noisy
  desktop alerts.

## 3.9 Acceptance criteria

- Every recommendation identifies its target, current state, proposed patch,
  evidence, missing facts, derivation version, and freshness.
- Usage alone cannot recommend quality; a healthy launch alone cannot recommend
  performance; analytical fit alone cannot claim observed success.
- Tied, stale, incompatible, correctness-unknown, and contradicted evidence
  cannot produce an Actionable winner.
- Apply is explicit, stale-guarded, settings-owned, and separate from restart.
- Undo cannot overwrite later user edits.
- Dismissal suppresses only an identical proposal and never alters evidence.
- Recommendation persistence has one selected store, survives restart/data-root
  switching, and reconciles an interrupted settings apply without guessing.
- Agent safety, approval, workspace policy, Local API scopes, and destructive
  confirmations do not consume recommendations.
- No automatic model/profile selection or workload routing is introduced.

## 3.10 Test and live-verification budget

Expected automated coverage: 30-40 tests.

- record/patch validation, serialization, stable derivation, and redaction;
- compatibility and freshness matrices across every evidence type;
- freshness/eligibility precedence and equivalent-evidence tie breaking;
- ties, contradictory evidence, missing correctness, usage-only, and
  launch-only refusal;
- deduplication, dismissal, expiration, supersession, and source deletion;
- staleness-guarded Apply and Undo with concurrent settings edits;
- crash points before/after settings write and recommendation decision commit;
- consistent projections in Services, Models, Lab, Benchmarks, and Doctor;
- safety and authority isolation pins.

Owner live gates:

- produce one Lab-backed runtime recommendation and inspect/apply/undo it;
- produce one workload conflict recommendation without stopping any consumer;
- inspect a benchmark/usage model recommendation and decline it;
- change the target settings between recommendation and Apply and confirm the
  stale proposal is refused;
- confirm no recommendation switches a live model or restarts a server.
