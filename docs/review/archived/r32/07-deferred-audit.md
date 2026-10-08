# 07. Deferred backlog audit

This document audits every row that was open in `docs/review/deferred.md` at
R32 planning time. The ledger is updated with the same classifications. No item
is pulled into R32 merely because it is old.

## 7.1 Selected for R32

### Whole-active-workload GPU Fit

Decision: pull in completely as doc 01's workload inventory, composition,
reservation, planning, and receipt contract.

Why now: R31's per-configuration fit, identities, telemetry, and direct
Chat-plus-embeddings dogfood make the next boundary both natural and testable.
Adaptive launch decisions cannot be correct without it.

### Hugging Face model artwork

Decision: pull in completely under doc 05's bounded untrusted-media contract.

Why now: R32 already changes model download cards, model inventory, fit labels,
and recommendation presentation. The card/revision identity is available and
Hugging Face has a documented thumbnail field. Privacy/cache policy is now
specified rather than hand-waved.

### Context checkpoints and cache RAM

Decision: pull in conditionally and split by demonstrated capability.

Why now: current upstream exposes explicit host-cache, checkpoint, unified-KV,
per-slot context, device, and split controls. Whole-workload RAM/VRAM planning
and Lab provide the missing evidence context.

Boundary: capability registry and resource accounting are mandatory. Each
control ships only with selected-runtime behavior, bounded Lab
benefit/correctness, and the platform gate in doc 02.

### Multi-device placement

Decision: select the consumer/device/allocation representation and capability
gates, but keep execution conditional on suitable hardware evidence.

Boundary: absence of two-device hardware produces an explicit Unknown and a
parked execution/UI remainder, not a shipped claim. Experimental tensor split
is never a default.

### Automated contradiction resolution and temporal knowledge engine

Decision: replace the broad item with doc 04's bounded temporal assertion and
source-revision work. Explicitly reject automatic truth resolution.

Why now: R30's typed relationships and R31's evidence origins support revision
lineage, while the current delete/dangling-supersedes and non-atomic RAG replace
paths establish concrete correctness needs.

Boundary: explicit review, current/as-of/history projections, exact citations,
one-hop retrieval, and hard deletion. No background whole-store reconciliation,
newer-means-true rule, or GraphRAG.

### Same-repository CI run de-duplication

Decision: select the bounded workflow correction in doc 09.

Why now: the current workflow and live required-check ruleset demonstrate two
Linux/Windows matrices for one PR head commit. This is operational correctness
and resource efficiency, not a general CI rewrite.

Boundary: retain non-required branch feedback before a PR, required PR
merge-context checks, main and fork PR coverage, and existing security
boundaries. Do not change repository settings in the implementation batch.

## 7.2 Closed as implemented

### General external draft-model workflow

Decision: close as implemented by R31.

Evidence: runtime-gated `draft-simple` and EAGLE-3 adapters, verified manifest
identity, compatibility validation, bounded recipes, engagement counters,
memory prediction/observation, and exact-output comparison exist. The remaining
real compatible pair exercise is an owner live gate. A live validation gate is
not deferred feature implementation. New mechanism-specific work can be opened
only from new evidence.

## 7.3 Rejected by product direction

### Continuous local fine-tuning and adapters

Decision: close as rejected by design for Hermaeus' current direction.

Evidence: R31 explicitly rejected autonomous fine-tuning, LoRA/adapters,
self-modifying policy, and continuous training from experience. Keeping the
same idea in a ledger whose premise is “worth doing later” contradicts that
decision. A future owner request would need a new scoped review covering data
provenance, consent, licensing, storage, evaluation, rollback, and hardware.

## 7.4 Moved out of the deferred feature backlog

### Deterministic timing for two clock-dependent tests

Classification: operational test watch.

The 150 ms negative debounce assertion and MCP 5000 ms bound still exist, but
neither has demonstrated flakiness. Keep them visible without pretending they
are a product feature. Any new R32 time behavior uses `TimeProvider` or explicit
signals.

### Windows CI Defender exclusion

Classification: CI operational constraint.

The exclusion is already bounded to trusted pushes and is based on measured
runner cost. This is current CI policy to monitor, not postponed feature work.

### Loading Whisper ONNX graphs under test

Classification: platform/model validation gate.

The 291 MB pinned assets make this an integration/live evidence obligation.
Pure tensor-name, decode, and shape logic remains covered. Do not keep a real
asset exercise in the product feature queue; record the live result when run.

### COSMIC folder-picker or portal observation

Classification: unconfirmed observation.

No Hermaeus defect was reproduced. Move it out of the backlog and reopen only
with steps, environment, and evidence that distinguish portal/desktop behavior
from application state.

## 7.5 Parked feature work with explicit prerequisites

### Agent run/step endpoints on the Local API

Keep parked. R32 does not create the single serialized per-task mutation owner
needed by Desktop and the separate API process. Resource/recommendation state
must not be mistaken for Agent execution authority.

Prerequisite: one owner for run, steer, continue, cancel, and approval mutation,
plus explicit per-token scopes and a non-desktop approval protocol.

### MCP HTTP and SSE transport

Keep parked. The client remains stdio-only and no demand is demonstrated.

Prerequisite: a real server/workflow that needs remote transport, plus auth,
TLS, reconnect, cancellation, event ordering, and trust semantics.

### llama.cpp backend sampling and internal performance instrumentation

Keep parked. Current upstream documents backend sampling as experimental.
`--perf` advertising alone does not establish a stable machine-readable
diagnostic contract or benefit.

Prerequisite: selected-runtime structured evidence and a controlled
`--parallel 1` Lab/Speed Check showing the effect. Ordinary launches stay clean.

### Knowledge-graph expansion and multi-hop retrieval

Keep parked. R32 temporal lineage solves a demonstrated history/current-state
problem without multi-hop graph traversal.

Prerequisite: a retrieval or inspection failure that one-hop relationships and
source revisions cannot solve, with bounded traversal, ranking, explanation,
privacy, and deletion semantics.

### Automatic model/profile selection and workload routing

Keep parked and explicitly separate from R32 recommendations/adaptive launch.

Prerequisite: task-specific comparable evidence, an explainable policy,
stability/hysteresis, user override, failure behavior, and proof that explicit
selection is materially inadequate. R32 never switches model identity.

### TLB behavioural evidence interchange

Keep parked as an external contract opportunity, not an internal Hermaeus
feature batch.

Prerequisite: a versioned producer-neutral experiment summary and a real import
sample. No TLB assembly reference, world/tick internals, or bespoke coupling.

## 7.6 Audit matrix

| Original open item | R32 disposition | Location |
| --- | --- | --- |
| Agent run/step endpoints | Parked, single-owner prerequisite unmet | 7.5 |
| MCP HTTP/SSE | Parked, no demand | 7.5 |
| Deterministic test timing | Operational watch | 7.4 |
| Windows Defender CI exclusion | CI operational constraint | 7.4 |
| Whisper graphs under test | Platform/model validation gate | 7.4 |
| backend sampling/internal perf | Parked, stable evidence prerequisite unmet | 7.5 |
| checkpoints/cache RAM/multi-device | Selected conditionally | 7.1, docs 01-02 |
| general external draft workflow | Closed as implemented | 7.2 |
| whole-workload GPU Fit | Selected | 7.1, doc 01 |
| Hugging Face artwork | Selected | 7.1, doc 05 |
| COSMIC folder picker | Unconfirmed observation | 7.4 |
| graph expansion/multi-hop | Parked | 7.5 |
| contradiction/temporal engine | Narrow temporal work selected; automation rejected | 7.1, doc 04 |
| automatic selection/routing | Parked | 7.5, doc 03 boundary |
| continuous fine-tuning | Rejected, removed from deferred backlog | 7.3 |
| TLB interchange | Parked external-contract opportunity | 7.5 |
| same-repository duplicate CI | Newly demonstrated, selected | 7.1, doc 09 |

## 7.7 Ledger rule after R32

At R32 close-out:

- move selected entries to Closed only when their bounded acceptance
  criteria actually land;
- split any capability-gated cache or multi-device work that remains Unknown
  rather than claiming the umbrella shipped;
- keep operational watches out of the feature table;
- do not reopen rejected autonomous learning or truth resolution through vague
  wording such as “experience improvements” or “knowledge evolution”.

## 7.8 Corrective audit deferments

### SQLite free-page maintenance

The reported free-page ratios are storage maintenance debt, not evidence of
corruption. The current stores use short-lived disposed connections and WAL;
missing `-wal` and `-shm` files after a checkpoint are therefore not by
themselves anomalous, and the inspected byte copies passed `PRAGMA
integrity_check`. This pass does not run `VACUUM` against a live database.

A safe implementation needs one application-owned maintenance coordinator that
proves the single-instance lock, waits for all store writers to quiesce, records
the database and schema identity, creates or verifies a backup, checkpoints or
vacuum-copies to a temporary file, verifies integrity and foreign keys, then
atomically replaces the database only during controlled shutdown or an equally
strict exclusive window. Failure must preserve the original and report whether
the temporary copy was discarded. That boundary is broader than this pass and
remains deferred. Current code logs database open and journal context where a
future maintenance decision will need it.

### Legacy `rag_query_traces` table

The table is still created by the RAG store in `conversations.db`, while current
trace records are written to `traces.db`. Its zero-row state does not prove that
all supported older installations have no reader, backup, or migration need.
It is retained in this pass because removal requires a versioned migration and
an explicit compatibility decision for existing databases. No casual `DROP`
was added. The split storage paths and their ownership remain documented by
the current stores.

### Existing dataset path spelling

The working `Hermaues` path is an existing user data identity. It is not
renamed or normalized silently; no product-facing defect was found that would
justify breaking it.
