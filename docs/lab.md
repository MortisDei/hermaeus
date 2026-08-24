# Lab

Lab is Hermaeus's controlled measurement and empirical-evidence workspace. It
is separate from Benchmarks, which measures reusable suites, and from Memories,
which stores user/model knowledge.

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

The experiment runner and Apply workflow are added by later R31 batches. Until
those land, Lab exposes Evidence and shared GPU Fit evidence only.
