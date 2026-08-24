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
Repeated observations and output hashes are split into bounded immutable
per-configuration evidence slices linked to the frozen start record. The final
completion stores only comparison decisions, failures, and links to those raw
slices, keeping every experience document inside its existing 32 KiB bound.
Prompt/output bodies and token values are omitted from exportable records.
Cancellation preserves partial evidence and normalizes the result as
`Cancelled` or `PartiallySucceeded`.

**Apply to Services** is a separate review after a controlled, correctness-
passing result. It shows every persisted field that would change and captures
the current server configuration plus runtime/model identity. Confirmation
rechecks all three, clones `AppSettings`, and uses the normal settings save
flow. A stale review is refused, and applying never deletes prior configuration
or experiment evidence.

## Bounded engine recipes

**Inspect runtime recipes** builds a fixed catalogue for the selected Chat
server. Every plan keeps the baseline, allows at most eight total launches, and
changes one declared dimension:

- GPU-layer placement uses CPU, partial where model block count is known, and
  all-GPU candidates;
- context uses adjacent values from Hermaeus's reviewed 2K to 128K ladder;
- KV offers only `f16`, `q8_0`, or `q4_0` when the exact runtime advertised
  both the baseline and candidate representation;
- Flash Attention appears only from `runtime.flash-attention` evidence;
- CPU-MoE placement appears only from `runtime.moe.cpu-placement` evidence and
  stays distinct from the still-Unknown expert-cache mechanism.

**Run selected recipe** freezes the plan and a SHA256 of the controlled prompt,
then launches baseline and candidates sequentially in separate owned Lab
sessions. It uses greedy temperature-zero sampling, one fixed seed, three
repetitions, and a 128-token cap. Two consecutive launch/workload failures,
user cancellation, or a deterministic output difference stop the remaining
candidates while preserving completed and failed evidence.

GPU Fit is evaluated for every configuration before launch and remains labelled
as deterministic prediction. After each repetition the shared telemetry source
captures process RAM and honest per-process GPU `Unknown` where no trustworthy
counter exists. Runtime response counters provide prompt/decode throughput and
token counts. The current buffered protocol cannot establish TTFT, so TTFT is
stored and displayed as missing rather than inferred from request duration.

The trade-off table shows speed, predicted RAM/GPU, observed memory peak,
correctness, and comparison refusal together. Low-bit KV requires a referenced
quality score; without one the missing `quality.score` evidence blocks Apply.
Loading success never becomes a quality claim. CPU-MoE analytical totals remain
Unknown when GGUF/runtime evidence cannot identify expert tensor placement,
while observed memory and throughput are still retained. No recipe chooses or
applies a winner automatically.

## External drafting and speculative tuning

External draft and EAGLE-3 plans use the same isolated baseline/candidate
protocol, but appear as Available only for a proven runtime and asset pair.
Inspection requires the exact `draft-simple` or `draft-eagle3` capability, two
distinct readable non-link GGUF files, verified model hashes from the model
manifest, vocabulary and tokenizer identity, and compatible model-family
metadata. EAGLE-3 additionally requires the companion's base-model name or
repository metadata to bind exactly to the target. Vocabulary equality alone
is never sufficient. A missing hash, tokenizer field, reduced-vocabulary
mapping, target binding, or failed runtime probe remains `Unknown`.

The controlled baseline disables speculation without changing the active Chat
server. The candidate records the companion's path-free identity and its
GPU/RAM allocation in GPU Fit. Runtime timing counters retain drafted and
accepted token counts, with acceptance calculated only when the drafted count
is positive. Zero drafted is an observed zero and an undefined acceptance
ratio; an omitted counter is Missing. The buffered endpoint still leaves TTFT
Missing, and exact output equivalence remains a required correctness gate.

Four separate one-at-a-time tuning plans cover draft maximum, draft minimum,
minimum probability, and draft GPU layers. They require the exact parameter
flag, one already-proven speculative mechanism, and an explicit baseline value.
Hermaeus never substitutes or assumes the runtime's current default. Candidate
ranges are reviewed and bounded, and stale capability, asset, or baseline state
is re-inspected immediately before launch.

The Windows asset inventory used during R31 implementation contained no general
external-draft or EAGLE-3 pair. The adapters and refusal gates are automated;
actual engagement, performance benefit, memory behavior, and equivalence remain
unverified until a known pair is run on the published revision.

## Prompt/shared-prefix evidence

The prompt-prefix recipe compares request-level prompt caching disabled and
enabled in separate isolated sessions. Each side receives the same three
reconstructed prompts: the user-controlled prefix plus a small deterministic
suffix. Only the three SHA256 prompt identities enter the frozen definition;
the prompt bodies are not persisted in Lab evidence. Exact output comparison
still gates the result.

Prompt processing milliseconds and throughput are direct runtime counters when
reported. Without a proven machine-readable reused-token schema, Lab labels the
comparison `ControlledTimingEffect` and keeps `prompt.reused.tokens` Missing.
It never converts a timing difference into a token count. A direct count can be
consumed only when an Available capability record names one of Hermaeus's
reviewed response fields for that exact runtime identity; the field's absence
from a response remains Missing rather than zero.

The selected R31 runtime exposes no stable direct reused-token counter, so the
direct-counter level remains Unknown. Optional prompt-diff log parsing was not
added: no observed stable, build-scoped format justified adding diagnostic
flags or a parser. Normal Chat launch arguments and Agent prompt construction
are unchanged.

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
