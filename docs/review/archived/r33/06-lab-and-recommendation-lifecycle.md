# 06. Lab and recommendation lifecycle

## 6.1 Existing product, not a new experiment framework

**RF:** `docs/lab.md`, `LabRecipeService`, `LabRecipeRunner`,
`LabExperimentService` and `IsolatedLabRuntimeHost` already provide frozen
baselines/candidates, capability checks, isolated loopback processes, fit
prediction, bounded repetitions, correctness constraints, fingerprints,
comparison evidence, failure retention, source suspension/restoration and
stale-guarded Apply. Recipe plans cap launches at eight and stop on bounded
failures/correctness mismatch. Retain those limits and one framework.

| Existing recipe group | Current evidence boundary | R33 action |
| --- | --- | --- |
| CPU/partial/all GPU layers; adjacent context | Already present; prediction and runtime results differ | Reuse for fit/recovery validation, not new recipes |
| KV precision; Flash Attention | Exact capability/compatible baseline required; low-bit KV needs quality evidence | Keep correctness gate; clarify Unknown reason |
| CPU-MoE | Already present; expert tensor accounting can remain Unknown | Improve discovery, not recreate it |
| External draft/EAGLE3 and draft tuning parameters | Requires exact mechanism, verified pair and compatibility | Retain; broader pairs are conditional evidence |
| Prompt-prefix reuse | Controlled hash-only prompt identity and output comparison; timing effect is not a direct reused-token counter | Supplemental counters only if structured evidence actually supplies them |
| R32 cache/checkpoint/per-slot controls | Capability/resource-accounted controls exist | Measure relevant scenarios, do not rebrand controls as new |

**P selected new research:** doc 05 JSONL observability, at most one dense-FFN
placement experiment and one DFlash/DSpark compatible-pair experiment if their
individual gates pass. Expert prefetch/cache remains a watch. No new recipe is
required merely to fill a catalogue row. Failed capability or absent hardware
is a legitimate descope result.

The effective-launch boundary is now explicit in the production path. Lab
enables a transient local properties endpoint for each owned runtime, retains
the PID and redacted argv, reads current nested context and slot fields, and
associates GPU-layer placement with the runtime's bounded startup offload line.
Missing or invalid process association, or missing or mismatched effective
fields, finish the workload as `Inconclusive`, which cannot produce a
recommendation or Apply review.

## 6.2 Discovery and capability state

**Problem:** the Experiment view presents manual candidate fields, frozen JSON,
recipe inspection, recipe prompt/table and Apply controls in one long flow.
Owner wants to know what is testable with the current model/runtime, not which
implementation switches exist. Existing recipe capability states already provide
a starting point; some generic recipes are marked Available before actual launch
viability is known (`LabRecipeService:72,83`).

**P outcome:** select a baseline, then see Available, Not available and Unknown
experiments, with reasons. Explain whether capability syntax, model shape,
companion, runtime identity, runtime-loaded state or resource admission is
missing. Available means the declared prerequisites are established; it is not
a guarantee of performance, free VRAM or successful allocation at launch time.
Distinguish selected configured server from currently loaded model/runtime.

**Boundary:** reuse capability/recipe plan records and invalidate inspection on
model/executable/configuration changes. Explicit inspection may probe; merely
opening a catalogue must not download, install or launch a model. Add a clear
next action for Unknown (inspect capability, choose verified pair, run baseline).
Results show baseline -> candidate -> evidence -> correctness -> decision.
Advanced knobs and raw identities sit behind disclosure. Preserve unavailable
entries for explanation without presenting their Run action as usable.

**Dependencies:** runtime identity and UI-independent experiment lifecycle.
**Verification:** switch runtime/model during inspection; no stale availability;
missing baseline, incomplete pair, runtime help unavailable, fit refusal,
unsupported flag and known supported flag; state reason visible before launch.
**Non-goals:** automatic winner/application, benchmark-system replacement,
model routing or unrestricted experiment scripts.

## 6.3 Repair configuration and recommendation transactions together

**RF:** settings and recommendation transaction records already exist.
`ManagedServerRecommendationPatch.Create` rejects an empty delta. Apply checks
status/eligibility/current fingerprint, clones settings, saves them, then records
an applied decision and Accepted status. `ReconcileAsync` repairs pending
transactions after interruption. Do not propose those as absent features.

Three distinct problems need separate tests:

1. **Editor versus persisted configuration:** `ServerProcessViewModel.RebindConfig`
   replaces `_config` but deliberately preserves all bound fields, to protect
   unsaved edits. This does not distinguish a dirty field from an untouched field
   that should adopt an external Apply. `StartCoreAsync` calls `SyncToConfig`,
   so an old displayed value can overwrite the applied one. The source mechanism
   is established; tie the owner's exact symptom to an isolated reproduction.
2. **Decision publication ordering:** SettingsChanged can refresh Services before
   the transaction becomes Accepted. There is no final recommendation-change
   notification in that save event. A late/stale refresh can remain visible.
3. **Semantic satisfaction and details:** a previously nonempty patch can become
   satisfied by another decision/manual save. Pending-only recovery does not
   reconcile all current proposals. `RecommendationReviewViewModel.InspectTarget`
   navigates to a page string, not a retained evidence identity. Apply/Undo ignore
   the returned success/refusal message.

**P outcome:**

```text
Lab evidence -> proposal -> review -> applied settings + durable decision
 -> reconciled editor/proposals -> retained details/history -> explicit restart
```

Saved is not Running. Accepted is not proof of a runtime outcome. AlreadySatisfied
is non-actionable without pretending that the owner approved this particular
proposal. A decision and its retained evidence survive page refresh/restart.

**Boundary:** versioned managed-server configuration projection with per-field
edit provenance and expected base revision. On external save, untouched fields
adopt the new configuration; real dirty conflicts show an explicit review choice.
Do not clobber unsaved edits or silently re-save stale fields. Reuse normal
Settings save; avoid full-snapshot lost updates from unrelated concurrent saves.
Audit Services, Models tuning/defaults, Doctor update, Setup and Lab Apply/Undo.

Publish a committed decision event only after durable status is recorded, and
make consumer refreshes generation-aware. Reconcile the target's other current
recommendations against actual supported-field values and evidence validity.
Run the same satisfaction/staleness check at query/review/apply, not only at
startup. Empty initial patches remain rejected. A nonempty patch whose values
already match is not an offered change. Preserve reason/history and eligibility;
do not delete the evidence to hide stale cards.

Deep links contain recommendation/evidence/run/target ids and a destination
contract. Details can open retained evidence after restart, show missing/deleted
source evidence explicitly, and return to the original list. Historical evidence
stays read-only until a fresh compatibility review succeeds. Display transaction
refusal/failure instead of merely refreshing. Preserve current pre-image rollback
and stale-Undo protections; no automatic runtime restart.

**Dependencies:** shared operation/configuration identity; existing settings and
recommendation stores. This is mandatory before optional runtime experiments.
**Verification:** Lab Apply with an already-open clean Services row; dirty row
conflict; next Start does not undo Apply; rapid saves/out-of-order refresh;
manual satisfaction of another proposal; zero delta; stale Undo; save failure;
crash after settings save before decision finalize; reopen with retained details;
evidence deleted; runtime still on old configuration until explicit restart.
**Non-goals:** automatic recommendations become commands, concurrent database-
wide transaction abstraction, new recommendation store or automatic routing.
