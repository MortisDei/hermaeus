# Lab

Lab is Hermaeus's controlled measurement and empirical-evidence workspace. It
is separate from Benchmarks, which measures reusable suites, and from Memories,
which stores user/model knowledge.

## Controlled experiments

The Experiment tab freezes one immutable definition before it starts anything.
The definition names the protocol, exact v2 runtime/model/hardware/configuration
fingerprint, target Services server, baseline, bounded candidates, workload,
sampling and repetition policy, required metrics, capabilities, timeout, stop
conditions, and correctness rule. Existing extra arguments are represented by
an opaque SHA256 in persisted evidence, not copied as raw command text. Editing
the draft after a run creates a new
definition and hash rather than changing the run already in progress.

Start clones the selected Chat server into a dedicated `ServerProcessManager`,
reserves a temporary port, and binds the process to `127.0.0.1`. Network and
port overrides in extra arguments are refused. The active Chat server is not
stopped or reconfigured, and `settings.json` is not saved. Each temporary
process has a run ownership id, PID, start time, executable hash, and atomic
local ownership record. Normal cancellation stops only that owned session.
Startup recovery kills an abandoned process only after PID, start time, and
executable content all match; otherwise cleanup remains `Unknown` and the
process is not touched.

Observations keep value and missing reason separate, so an absent counter never
becomes zero. Every observation names unit, source, evidence origin, trust,
timestamp, repetition/case, and all four v2 fingerprint components.
Comparisons list uncontrolled fingerprint differences and refuse a headline
delta when any remain. Timing metrics show median, observed range, repetition
count, and source. There is no universal score or statistical-significance
claim.

Correctness compares token ids when both sides expose them and falls back to an
exact UTF-8 output hash at a weaker declared level. It reports `Equivalent`,
`Different`, or `Unknown`. A deterministic mismatch blocks an Apply
recommendation even when performance improved. Speed-only protocols are
explicit and can never recommend Apply.

Start and completion are separate immutable `lab-run` experience records.
Completion retains observations, failures, comparisons, output hashes, and
provenance, but omits prompt/output bodies and token values from the exportable
record. Cancellation preserves partial evidence and normalizes the result as
`Cancelled` or `PartiallySucceeded`.

**Apply to Services** is a separate review after a controlled, correctness-
passing result. It shows every persisted field that would change and captures
the current server configuration plus runtime/model identity. Confirmation
rechecks all three, clones `AppSettings`, and uses the normal settings save
flow. A stale review is refused, and applying never deletes prior configuration
or experiment evidence.

The first shell protocol proves definition, isolation, lifecycle, evidence,
comparison, correctness, and Apply boundaries. Bounded engine, KV, speculation,
prefix, and CPU-MoE recipes land in subsequent R31 batches on this same core.

## Evidence

The Evidence surface reads `{DataRoot}/experience.db`. It indexes typed
operational evidence in three initial domains: Agent tool outcomes, GPU Fit
observations, and Lab runs. Source task files, transcripts, runtime samples and
Lab run files remain authoritative; deleting an experience does not rewrite
those sources.

Filter by domain, project or opaque workspace scope, model/runtime fingerprint,
normalized outcome, evidence origin, status and date. Selecting a row shows its
canonical context, action/configuration, normalized outcome, provenance links,
fingerprints and correction status. Missing values remain visibly absent or
`Unknown`; the UI does not substitute a guessed result.

Correction is a typed outcome/detail operation, not a raw JSON editor. It
creates a new current record, links it to the prior record and marks the prior
record superseded in one transaction. Remove is a confirmed hard delete. It is
refused while a correction depends on that record, and its Activity entry keeps
only the opaque id, domain and timestamp.

Export prepares versioned, redacted JSON for checked rows, or the selected row
when none are checked. Experience persistence rejects API keys, bearer-like
secrets and user-home paths through the shared redactor. Agent command text and
raw command output remain only in the source task evidence, not the empirical
index.

Experience never grants authority. Safety gates, workspace containment,
approval fingerprints, remembered approvals, Local API scopes and destructive
confirmations do not consume the experience store or normalized outcomes.

## GPU Fit prediction and observation

The Services card exposes GPU Fit as an analytical prediction over the current
editor values. It names weights, separate K/V allocation, runtime overhead,
companion files, GPU/RAM placement, and policy headroom. A material missing
fact withholds the total instead of being treated as zero. CPU-MoE placement is
therefore Unknown until tensor placement evidence exists, and low-bit KV maths
requires runtime advertisement of that exact format.

Runtime observation uses the shared telemetry source. Each sample carries a
source and trust state and belongs to one runtime process instance. A restart
starts a new series. Process working set is process-scoped RAM evidence. GPU
memory is `Unknown` when no trustworthy per-process source exists; a
whole-device total is retained only as a device total and is not called model
VRAM.

GPU Fit experience persists the immutable prediction with a bounded observation
summary and retained exact samples. Discrepancy is shown only for an exact v2
fingerprint match. Compatible and incompatible observations remain separate,
and neither changes the analytical formula or saved settings.

## Storage and portability

`experience.db` is an additive SQLite database under Data Root and therefore
participates in the existing data-root migration and backup file enumeration.
Writes that add a record and all provenance links are transactional. There is
no automatic retention deletion.

Lab runtime ownership state is stored separately under Data Root only while an
isolated process exists. It contains opaque ownership/run ids, PID/start time,
port, and executable hash, not executable/model paths. The record is deleted
when the owned process stops.
