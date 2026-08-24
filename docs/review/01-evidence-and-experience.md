# 01. Evidence and experience

This document defines the dependency root for R31. Lab, GPU Fit, Project State,
telemetry notifications, and Agent reuse may consume this work. None may invent
a competing outcome or provenance vocabulary.

## 1.1 One evidence taxonomy

Replace the current three-way `EvidenceOrigin` semantics with five explicit
origins:

| Origin | Meaning | Example |
| --- | --- | --- |
| `DirectObservation` | A runtime, operating system, user-visible operation, or instrument reported what occurred | process exit 0, measured VRAM peak, llama-server timing counter |
| `DeterministicCalculation` | Hermaeus produced the value from named inputs and versioned code without model judgement | analytical KV byte projection, normalized outcome derived from exit/timeout evidence |
| `UserProvided` | The user stated or edited it; Hermaeus has not independently established it | accepted Project objective, manually entered milestone |
| `Extracted` | A deterministic parser copied or transformed information from a source | GGUF layer count, parsed runtime build identity, imported experiment summary |
| `ModelInference` | A model inferred, summarized, proposed, or predicted it | proposed Project State update, synthesized guidance |

`Inferred` is already serialized in persisted `SourceReference` values. Do not
rename it destructively. Read legacy `Inferred` as `ModelInference`, write only
the new explicit value, and add round-trip tests for both old and new JSON. The
existing `DirectObservation` numeric/name representation must also continue to
load. If enums remain string-serialized, append values and use a compatibility
converter; do not rely on ordinal coincidence.

Do not add a sixth `Heuristic` bucket. A non-model heuristic is either a
deterministic calculation with named inputs or an unsupported guess that should
not be stored as evidence.

## 1.2 Normalized outcome vocabulary

Add one Core-owned, provider-neutral vocabulary:

| Outcome | Required meaning |
| --- | --- |
| `Succeeded` | The requested operation completed and evidence establishes the intended effect |
| `PartiallySucceeded` | Some independently identifiable requested effects completed and others did not |
| `NoEffect` | The operation completed but changed or produced nothing relevant, with direct evidence for that fact |
| `Unavailable` | The operation or dependency was not present/capable before an attempt could meaningfully run |
| `Denied` | A user or authenticated approval decision refused the action |
| `Blocked` | Deterministic policy or safety rules refused the action |
| `Failed` | The operation ran or was attempted and evidence establishes failure |
| `Cancelled` | The caller or user cancelled it before a conclusive completion |
| `TimedOut` | A configured deadline elapsed and the operation was stopped or abandoned |
| `Unknown` | The retained evidence cannot establish any stronger semantic outcome |

`Unknown` is not failure. `NoEffect` is not success. `Denied` is a user decision;
`Blocked` is a product policy decision. Cancellation and timeout remain separate
because their remediation and authority are different.

The normalized record is small and additive:

```text
NormalizedToolOutcome(
  Outcome,
  EvidenceCode,
  Detail,
  DerivedAtUtc,
  DerivationVersion)
```

`EvidenceCode` is a stable machine value such as `process-exit-zero`,
`workspace-policy-denied`, or `mcp-no-result-contract`. `Detail` is bounded,
redacted display text. Neither is model-authored.

## 1.3 Deterministic derivation by executor

Implement one registry of normalizers keyed by the actual executor/tool family.
Do not put a single string-matching switch over `ResultSummary` in
`AgentService`.

Minimum mappings:

- `run_command`: timeout -> `TimedOut`; exit 0 -> `Succeeded`; non-zero exit ->
  `Failed`; process start failure -> `Unavailable` only when the executable or
  runtime is proven missing, otherwise `Failed`; caller cancellation ->
  `Cancelled`.
- file read/list/search/glob: completed read/list -> `Succeeded`; a valid empty
  result -> `NoEffect`; missing requested file -> `Unavailable`; policy refusal
  -> `Blocked`; cancellation -> `Cancelled`; malformed/unclassified exception ->
  `Failed` or `Unknown` according to retained evidence.
- edit/create/apply/revert: all requested files changed -> `Succeeded`; a
  multi-file rewind with a mix of reverted and skipped files ->
  `PartiallySucceeded`; already-matching content with verified equality ->
  `NoEffect`; stale precondition -> `Blocked` only if a deterministic guard
  refused it, not generic `Failed`.
- approvals: explicit reject -> `Denied`; safety/workspace refusal -> `Blocked`;
  approval itself does not claim that later execution succeeded.
- MCP: use structured bridge status where available. A plain string response
  without a trustworthy completion contract remains `Unknown`, even if it
  contains words like "success".
- `plan_subtasks` and `set_plan`: successful state mutation -> `Succeeded`;
  duplicate-plan refusal -> `Blocked`; invalid model proposal -> `Blocked` with
  a stable validation code.

Preserve all current raw fields on `AgentToolResult`. Add normalized outcome as
an additive field. Pre-R31 task files load with `Unknown` and a
`legacy-no-normalized-outcome` code; do not reinterpret historical summaries.

The model-facing context uses the normalized label plus the bounded raw summary.
The raw summary is still present because the label cannot explain compiler
errors, file contents, or partial rewind details.

## 1.4 Safety boundary

The following types and services must not consume normalized outcomes or
experience when deciding authority:

- `AgentSafetyGate`
- `WorkspacePolicyEvaluator`
- `AgentApprovalFingerprint`
- remembered exact-command approvals
- Local API token authentication and scope checks
- destructive confirmations

A previously successful action does not become pre-approved. A model claim that
an action worked does not create a normalized success. A user correction to an
experience record does not rewrite task state or the run ledger.

## 1.5 Experience model

Add Core models and an `IEmpiricalExperienceStore` contract. Put the SQLite
implementation in Services because GPU Fit, Lab, Agent, and future consumers
cross the Agent project boundary. Do not reference Avalonia or Agent-specific
implementation types from the store.

One record is approximately:

```text
EmpiricalExperience(
  Id,
  SchemaVersion,
  Domain,
  ProjectId?,
  WorkspaceFingerprint?,
  ContextJson,
  ContextHash,
  ActionJson,
  ActionHash,
  Outcome,
  Provenance[],
  RuntimeFingerprint?,
  ModelFingerprint?,
  CreatedAtUtc,
  CorrectsExperienceId?,
  Status)
```

The JSON payloads are bounded, canonicalized, schema-versioned documents, not
arbitrary object dumps. Each domain owns its typed codec. The initial domains
are `agent-tool-outcome`, `gpu-fit-observation`, and `lab-run`; future domains
are additive. Consumers query typed projections, never deserialize another
domain's private shape by guesswork.

`ContextHash` and `ActionHash` support exact grouping. They do not replace the
human-inspectable canonical documents. `WorkspaceFingerprint` is an opaque
stable identifier, never a personal absolute path. Model/runtime fingerprints
follow doc 02.

## 1.6 Raw evidence and provenance

Experience is an index over evidence, not the only copy of evidence.

- Agent experience points to task id, step/transcript entry, tool result, and
  normalized derivation version. `task_state.json` and transcript remain
  authoritative.
- GPU Fit experience retains the analytical input/breakdown separately from
  the observed sample series. A discrepancy row references both.
- Lab experience points to the immutable Lab run and its observation rows.
- A `SourceReference` carries the five-way origin. Add provenance kinds only
  where necessary (`Experience`, `Lab`, and `RuntimeObservation` are likely);
  do not overload `Benchmark` for non-benchmark experiments.

An experience row can carry multiple provenance references. Every reference is
bounded and redacted before persistence. No row stores environment-variable
dumps, API keys, bearer headers, full process command lines, user home paths, or
unbounded stdout/stderr.

## 1.7 Persistence and migrations

Use `{DataRoot}/experience.db` with `SqliteMigrationRunner`. Version 1 creates:

- `experiences`, containing the canonical context/action documents, outcome,
  correction link, fingerprints, timestamps, and status;
- `experience_provenance`, one-to-many evidence references with origin;
- indexes on domain, project id, workspace fingerprint, context/action hashes,
  runtime fingerprint, model fingerprint, created time, and status.

Writes that create an experience and its provenance are one transaction.
Imports validate all bounds before the transaction. SQLite parameters are used
for every value. The database is covered automatically by data-root backup and
migration behavior, which must be verified rather than assumed.

Correction creates a replacement row whose `CorrectsExperienceId` points to the
prior record and marks the prior record Superseded in the same transaction. It
does not mutate raw source evidence. Explicit removal is a hard delete of the
selected experience/provenance rows after confirmation, because privacy removal
that secretly retains the content is not removal. The corresponding Activity
event may retain only the opaque id, domain, and timestamp, never removed
content. If another record references the target, the UI explains and requires
removing or superseding the dependent record first.

No automatic retention deletion in R31. The store is expected to remain small,
and silent expiry would make later comparisons unauditable.

## 1.8 Inspection and bounded reuse

The first user surface belongs in Lab as an Evidence tab, not Memories. Memories
are user/model knowledge; empirical experience is structured operational
evidence.

The surface supports:

- filters by domain, project/workspace scope, model, runtime, outcome, origin,
  and date;
- a detail view showing context, action/configuration, normalized outcome, raw
  evidence links, fingerprints, and whether this is current or superseded;
- correction through a typed domain editor where safe, never a raw JSON editor;
- explicit removal with dependent-record handling;
- export of selected records using a versioned, redacted JSON format;
- reuse only through a bounded query with exact domain and compatible
  fingerprints.

No generic "recommended action" field exists in the base schema. A consumer may
derive guidance at read time and label its origin correctly. Any persisted
guidance is a separate model-inference or deterministic-calculation record that
references the evidence it used.

## 1.9 Acceptance criteria

- All five origins serialize, deserialize, display, filter, and export
  distinctly; legacy `Inferred` records load as model inference without data
  loss.
- Every built-in Agent executor and approval/policy path returns one normalized
  outcome using deterministic evidence. Historical task files remain loadable
  and become Unknown rather than guessed.
- Raw result summaries, exit codes, timeouts, patch outcomes, and provenance
  remain unchanged and independently inspectable.
- The experience store round-trips every initial domain, uses atomic
  transactions, survives restart, and migrates an empty and pre-existing data
  root additively.
- Corrections preserve the earlier evidence chain. Explicit removal removes the
  selected content and leaves no content-bearing tombstone.
- A model-authored success string cannot produce `Succeeded` without matching
  executor evidence.
- Neither normalized outcomes nor experience can change risk, approval,
  workspace containment, or Local API authorization.
- Evidence inspection exposes missing/Unknown fields instead of substituting
  defaults.

## 1.10 Test and live-verification budget

Expected automated coverage: 35-45 tests.

- outcome enum/JSON compatibility and all deterministic mappings;
- untrusted result text cannot forge outcome;
- additive `AgentToolResult` and old task-state loading;
- SQLite schema, transaction rollback, typed query, correction, hard removal,
  dependency refusal, export redaction, and data-root switch;
- safety pins proving experience is absent from gate inputs;
- ViewModel filters, correction/removal confirmation, and Unknown rendering.

Linux/COSMIC live gate:

1. Run one read-only Agent task, one approved command that exits 0, one command
   that exits non-zero, one rejected action, and one workspace-policy refusal.
2. Inspect every raw result and normalized label in Lab > Evidence.
3. Restart Hermaeus and confirm the records remain, with no absolute home path
   or command output leakage in the exported experience JSON.
4. Correct and then remove a disposable record; confirm the source task
   transcript remains intact.
