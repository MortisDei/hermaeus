# 04. Project State, Agent models, and the Local API

This group improves continuity without converting prose into silent authority.
Project State is explicitly user-owned. Subtask model choice is explicit and
approved. Local API identity is not approval authority.

## 4.1 Project State is not Project metadata

The existing `Project` remains a named container and set of defaults. Add a
separate `ProjectState` record with its own revision and timestamps:

```text
ProjectState(
  ProjectId,
  Revision,
  CurrentObjective,
  Milestone,
  Status,
  Items[],
  UpdatedAtUtc,
  UpdatedByOrigin)

ProjectStateItem(
  Id,
  Kind,
  Text,
  ArtifactLocator?,
  Order,
  Origin,
  Source?,
  CreatedAtUtc,
  UpdatedAtUtc)
```

Kinds are `AcceptedDecision`, `RejectedApproach`, `Constraint`,
`UnresolvedQuestion`, `ImportantArtifact`, and `NextAction`. Objective,
milestone, and status are top-level because they are singular and prominent.

This is not Recall, Memory, RAG, conversation history, task state, or a graph.
It may point to those things through provenance but does not copy their entire
content or rewrite them.

## 4.2 Persistence and review

Migrate `projects.db` additively through `SqliteMigrationRunner`:

- `project_state`, one row per project with revision and singular fields;
- `project_state_items`, ordered structured items;
- `project_state_proposals`, bounded proposed revisions with origin, source,
  base revision, status, and timestamps.

Saving a state revision and its items is one transaction with optimistic
revision checking. A proposal never edits accepted state. Accept shows the diff,
refuses if its base revision is stale, and creates a new accepted revision.
Reject records only proposal status/reason. Explicit deletion/removal is
supported for accepted items.

Deleting a Project follows the current product rule and does not delete bound
conversations/tasks/datasets/memories. It does delete the Project's own State
and pending proposals in the same local operation, because those records have no
meaning without the Project. Update `docs/projects.md` to make the distinction
explicit.

## 4.3 UI and proposals

Add a State section/tab to the existing Project editor, not a new navigation
panel and not a whole-workspace graph.

- All accepted fields are directly editable and saved through `IProjectStore`
  or a narrow `IProjectStateStore` service.
- Proposed updates appear as a review queue with field/item-level diff,
  provenance, Accept, Edit then accept, and Reject.
- Models can propose only through an explicit user command/action. Ordinary
  Chat/Agent output is never scraped after the fact.
- Model proposals have `ModelInference` origin. User edits have `UserProvided`
  origin. Artifact facts copied by a deterministic parser are `Extracted`.
- No auto-accept setting in R31.

## 4.4 Bounded context reuse and acceptance

Chat and Agent may consume a compact accepted Project State block when a Project
is active/bound. The context receipt names Project State separately and includes
revision and item sources. Proposals are never injected as accepted truth.

Acceptance criteria:

- Project State round-trips, revisions atomically, rejects stale proposals, and
  survives restart/data-root migration.
- A model response cannot change accepted state without a displayed proposal
  and user acceptance.
- Editing/removal is available for every field/item. Provenance stays visible.
- Chat/Agent context contains only accepted state and labels its source/revision.
- Project deletion removes only the Project-owned state/proposals and preserves
  the current non-destructive behavior for conversations, tasks, datasets, and
  memories.
- Empty Project State preserves pre-R31 behavior byte-for-byte at context
  construction boundaries.

Expected automated coverage: 15-20 tests for migration, revision conflict,
proposal accept/edit/reject, origin, context inclusion/exclusion, delete
semantics, and ViewModel commands.

Linux/COSMIC live gate: create a project state, generate a disposable proposal,
inspect/edit/accept it, restart, resume Chat and Agent under that project, and
confirm accepted state appears while the rejected proposal does not.

## 4.5 Explicit subtask model identity

Add model identity as persisted task/orchestration data:

- `AgentSubTaskSpec.ModelId`, optional in approved plan input;
- `AgentTaskState.ModelId`, frozen when a root/child task first starts;
- `AgentTranscriptEntry.ModelId` and context receipt source where applicable;
- subtask UI rows, approval preview, run ledger/report, and synthesis input.

Legacy tasks with no `ModelId` retain the caller-selected/inherited behavior.
Once frozen, a resume uses the persisted task model unless the user explicitly
changes it through a reviewed action before the next planner call. Do not let
switching the Chat or Agent picker silently change an in-flight task.

## 4.6 Selection sources

Two explicit sources are allowed:

1. a `plan_subtasks` proposal names a configured visible model id per child and
   the user approves the exact plan;
2. the user chooses a model for a specialist/subtask in the plan review UI.

Specialist profiles do not contain hidden model ids and there is no capability
score/router. The planner receives a bounded list of configured eligible model
ids/names if model selection is enabled. Unknown, hidden, removed, or currently
unavailable ids fail validation before approval materializes children.

An omitted child model explicitly means inherit parent. The preview renders
`inherit <parent>` rather than a blank field.

## 4.7 Execution and synthesis

`RunOrchestrationAsync` creates each child with the spec's resolved model id and
passes child-owned `AgentWorkspaceOptions`. Siblings may use different models.
The parent synthesis uses the parent's persisted model, not the last child.

Every child report shown to synthesis contains model id/display name and the
same result/reservations it already carries. Transcript and trace events record
the model that actually produced each planner response. If an explicitly
selected model is unavailable at execution time, the child pauses visibly with
an actionable decision; it does not fall back to another model.

Model selection changes no constraints, tools, risk classification, approvals,
workspace policy, read budget, or orchestration depth/budget.

## 4.8 Subtask-model acceptance

- Approved plans show resolved model per child and reject unknown/unavailable
  ids before child creation.
- Inherit is explicit and preserves current behavior.
- Task state, recent list, subtask rows, transcript, trace, context receipt,
  report, and synthesis all retain the actual model identity.
- Restart/resume uses persisted identity. Removed models pause; no silent
  fallback occurs.
- Parent synthesis retains its own model.
- Safety/gate decisions are identical for the same action regardless of model.

Expected automated coverage: 15-20 tests, including legacy task loading, plan
validation, user override, mixed-model children, restart, missing model pause,
synthesis identity, and a safety-gate equivalence pin.

Linux/COSMIC live gate: with two configured local models, approve a two-child
plan selecting one model each, inspect both identities during execution and in
the final report, then repeat with one model stopped to confirm visible pause
without fallback.

## 4.9 Agent Local API threat model

The existing per-app token authenticates a caller. It does not prove a human is
present, authorize a workspace, approve a tool, or permit a destructive action.
R31 must add explicit per-token Agent scope, off by default, before any Agent
route is reachable.

The scope binds:

- allowed Agent operations;
- one or more pre-existing saved workspace profile ids, never arbitrary request
  paths;
- optional allowed model ids;
- whether task creation/start and steering are permitted;
- no approval authority.

Changing scope is a desktop settings action through the existing secret/token
flow. Raw tokens remain in `ISecretStore`; settings retain references/scopes.
Revocation takes effect on the next request.

## 4.10 Safe R31 endpoint surface

If the scoped authorization and service boundary are complete, ship:

- `POST /v1/agent/tasks`: create a New task against an allowed workspace
  profile, model, and optional Project. It does not start implicitly.
- `POST /v1/agent/tasks/{id}/start`: run until terminal, user question, pending
  decision, gate block, budget stop, or cancellation. Return `202` plus run id
  for long work.
- `GET /v1/agent/tasks/{id}` and `/runs/{runId}`: status, normalized outcome,
  active step, pending-decision summary, and safe links.
- `POST /v1/agent/tasks/{id}/steer`: enqueue user text under the same semantics
  as desktop steering. It carries no approval.
- `POST /v1/agent/tasks/{id}/continue`: continue only when no unsatisfied
  decision/user-answer contract is being bypassed.
- `GET /v1/agent/tasks/{id}/output`: final/partial report, reservations,
  provenance, and safe artifact metadata.
- `GET /v1/agent/tasks/{id}/decisions`: explicit read-only pending decisions
  with opaque id, fingerprint, risk, reason, and desktop-required status.

Do not ship an approval/deny endpoint in R31. Approval from a non-desktop caller
needs a separate user-presence and review contract; possession of the token or
echoing the fingerprint is not enough. The caller may observe that a decision
is pending and direct the user to Desktop.

## 4.11 Service boundary and concurrency

Local API currently runs in a separate process and resolves services from its
own DI root. It cannot safely instantiate a second independent Agent state
machine beside Desktop without concurrency ownership.

Before endpoints ship, define one owner:

- either move Agent orchestration behind a local authenticated coordinator used
  by Desktop and Local API;
- or make the Local API host the single Agent service and have Desktop call it;
- or ship only design/contracts this round.

Do not point two `AgentService` instances at the same `task_state.json` and hope
atomic writes solve scheduling, pending approval, or steer interruption races.
The chosen owner must serialize per-task mutations, expose cancellation, and
recover run ownership after process restart.

This architectural collision is the hard gate for execution endpoints. HTTP
mapping is not the gate.

## 4.12 API security and privacy

- Managed host remains loopback by default. Agent routes require authenticated
  named per-app token plus explicit Agent scope.
- Workspace ids resolve only through pre-approved profiles and existing
  containment/symlink/policy checks.
- Responses never expose absolute workspace/data-root paths, raw commands,
  secrets, unredacted logs, or unrelated task content.
- Task ownership records the verified token name. A token can inspect/steer only
  tasks it created unless desktop scope explicitly grants broader read access.
- Rate/concurrency limits prevent one caller from spawning unbounded local model
  work.
- Every call is traced by verified token identity. Self-reported client headers
  remain untrusted hints.
- API cancellation is request/run cancellation, not approval and not broad
  process kill.

## 4.13 API acceptance and fallback contract

Minimum mandatory R31 deliverable is the versioned request/response,
authorization, decision, ownership, concurrency, and restart contract with
tests over the pure policy. Execution endpoints ship only if single-owner task
mutation is implemented cleanly.

Acceptance for a shipped surface:

- an ordinary existing token receives 403 for every Agent route;
- scoped tokens cannot name arbitrary paths/models/projects or inspect another
  caller's tasks;
- start stops at the identical desktop approval gate and decisions are explicit;
- no API request can approve, fingerprint-bypass, or widen an action;
- steer/continue carries no authority and cannot run through a pending decision;
- restart leaves task/run state truthful and one owner resumes it;
- revocation blocks the next request;
- capabilities reports Agent API unavailable with a reason when only contracts
  ship.

Expected automated coverage: 20-30 tests for scope defaults/migration,
workspace/model allowlists, ownership, route auth, pending-decision stop,
steer/continue rules, concurrency, restart, cancellation, redaction, and proof
that no approval endpoint exists.

Linux/COSMIC live gate if endpoints ship: create a scoped token in Desktop, run
a read-only task from a local client, reach an approval-required action, confirm
the API cannot approve it, approve in Desktop, retrieve the report, revoke the
token, and confirm the next request fails. Inspect traces for verified caller
identity and no private paths.
