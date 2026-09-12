# 02. Application authority and lifecycle

## 2.1 Existing architecture, actual missing boundary

**RF:** `src/Hermaeus.Composition/HermaeusServiceRegistration.cs` registers the
non-UI graph used by both hosts. Core has contracts; Services/Agent/Rag/Voice
have substantial production implementations. `src/Hermaeus.LocalApi/Program.cs`
is a real headless host, not a whole-product application driver.

Desktop `App.InitializeAppAsync` and ViewModels own meaningful application
work: startup/recovery sequencing, server-row lifetime, watched-source scheduling,
Recall backfill, source-service suspension around Lab/RAG work, task commands,
model preparation, post-Apply refresh, and shutdown. Moving registrations alone
would leave these behaviors unreachable or different in another host.

**P, decision A:** retain Composition as the graph/wiring boundary. Introduce
small capability-specific application contracts in Core (or Agent contracts for
Agent-specific state), with implementations in the owning subsystem. Compose a
thin application lifecycle coordinator from these owners. Do not create a
universal service locator, event bus, workflow engine, or second storage stack.
No Services dependency on ViewModels, Composition or Avalonia is permitted.

## 2.2 Ownership matrix to implement

| Capability | Current application caller/owner | Proposed authority and Desktop role |
| --- | --- | --- |
| Settings bootstrap/data root | Desktop synchronous precomposition load; Local API separate startup | Common bootstrap policy loads/stages migration before resolving stores. Host mode explicitly chooses permitted side effects. Desktop renders restart decisions. |
| Store startup/recovery | App and individual lazy store initialization | Lifecycle coordinator invokes existing owners in documented order, reports partial readiness and retry. Keep idempotent lazy safety checks. |
| Managed runtime | Services server-row managers, existing resource coordinator | Services runtime registry owns manager lifetime by stable server id, admission and operation identities. Row VM projects state and submits commands. |
| Lab/RAG suspension | Lab VM and `RagIngestServiceSuspension` | Application operation owns suspend, experiment/ingest, cancellation, restore, and restore-failed receipt. UI cannot lose restoration by navigating away. |
| Agent | AgentService plus VM direct task/patch writes | One per-task command owner covering every task mutation, plus workspace-target conflict validation. VM becomes a client. |
| Benchmarks | Service runs cases; VM prepares model | One operation starts before preparation and owns terminal evidence and cleanup. |
| Chat/RAG/voice | Services perform work, VMs assemble/cancel workflows | Extract only production workflow orchestration required by driver/shutdown, retaining presentation state in VMs. Reuse provider, retrieval, memory and voice implementations. |
| Automatic refresh/Recall | MainWindow background task list | Owned scheduling with host cancellation and bounded drain; no provider/network/auto-start during capability queries. |
| Notifications/navigation | Toast/voice bridges and page-string callbacks | Capability events contain identities/outcomes; Desktop maps to toast, voice or navigation. UI response never becomes authority. |

A task's Project association and workspace are frozen at creation. A current
sidebar selection is not an authority to retarget existing work. Cross-task
operations addressing the same file must compare expected pre-image and acquire
a short target write lock; per-task serialization alone cannot protect a shared
workspace. Do not serialize whole inference runs globally.

## 2.3 Operation contract

**Problem:** A1-A4, G1-G6, R1 and B1 in doc 01 share lifecycle/authority gaps.
**Outcome:** every long operation has an identity, an owner and a truthful
terminal observation before its host reports completion.

**P:** define a minimal immutable operation snapshot carrying operation id,
capability, task/workspace/server identity as applicable, owner session/epoch,
phase, monotonic revision, start/end timestamps, progress, cancellation reason,
terminal result, and evidence references. Phases distinguish preparation,
waiting for owner, running, verifying, restoring, completed, cancelled, failed,
and interrupted. Capability-specific detail stays typed in its subsystem.

- A command returns accepted/rejected plus an operation id, or an already-known
  result for the same idempotency key. Acknowledgement is not completion.
- Events are bounded, ordered per operation, and projectable from a current
  snapshot. A UI subscription can reconnect without replaying mutations.
- Cancellation is scoped to the requesting operation. An owner stopping a task
  cancels its child chain according to documented parent/child rules, not an
  unrelated task or shared runtime. Distinguish timeout from user cancellation.
- Persist only recovery-relevant transitions and bounded evidence, through
  existing stores/journal conventions. Do not persist every progress tick.
- Crash recovery never re-executes an approved write just because its outcome
  was not recorded. Reconcile its operation/pre-image/post-image receipt; mark
  ambiguous results Interrupted/Unknown and require renewed review.
- Shutdown refuses new work, requests cancellation, awaits owned work and
  restoration, stops owned resources, flushes evidence, then records clean exit.
  A deadline produces an incomplete-shutdown receipt, not a false clean marker.
- Preserve configured, planned, rendered, effective and observed runtime values;
  these are not aliases for the operation's status.

Normal MainWindow close already awaits `ShutdownAsync`. Preserve that mechanism;
the repair is complete operation coverage and truthful failure handling, not
inventing async close. Include App's detached embedding warmup/backfill in the
owned inventory. Restart currently starts a replacement before the parent exits
and releases its single-instance lock. Define a bounded handoff/ready protocol
or release-and-transfer sequence that prevents the replacement losing that race
without allowing two authoritative writers. Test failure on either side of the
handoff; do not solve it with an arbitrary delay or another real data migration.

**Boundary:** existing stores remain authoritative. `task_state.json` remains
task truth, task_index.db remains rebuildable. Add versioned/additive fields;
old tasks without verification receipts are historical Unknown, not retroactively
Verified. Resource reservations remain in the existing coordinator.

**Dependencies:** baseline source inventory and isolated scenario seams.
**Verification:** paired clients, rejected stale revision, cancel at every await
boundary, delayed event after view switch, dispose/drain failure, restart from
each durable transition, settings bootstrap before store constructors.
**Non-goals:** distributed transactions, arbitrary workflow DAGs, telemetry SaaS,
a resident background service merely to satisfy the theme.

## 2.4 Headless whole-product application driver

**P, mandatory:** a small console/test driver composes the same application
contracts and production services. Default mode requires explicit scratch
settings, data and workspace roots and uses no owner secrets. It must fail
closed if isolation is missing. It is a client, not a forked implementation.

The first useful slice runs bootstrap -> create task -> provider response ->
validated proposal -> decision -> filesystem result -> verification -> reopen.
Subsequent slices cover benchmark preparation/cancel, runtime recovery,
Lab Apply/reconcile/reopen, RAG ingest/query generations and shutdown. Driver
commands and structured result receipts can remain test/developer-facing in R33;
a general end-user CLI design is not required.

Two evidence modes:

1. **Deterministic production workflow:** script the external LLM/provider and
   OS timing boundaries only; retain production composition, safety gate, task
   store, tool executor, workspace tools, application owners and configuration
   projections. Mark runtime/hardware execution simulated.
2. **Real dependency scenario:** use explicitly selected installed runtime and
   isolated model/workspace assets. Preserve binary/config identity and resource
   observations. This mode earns runtime evidence, not automatic GUI proof.

Replace only interfaces necessary to control external nondeterminism. Each test
lists substitutions. Do not reuse `AgentScenarioRunner` as whole-product proof:
it intentionally constructs a narrower graph with null retrieval/MCP/traces and
seeded workspace memory (`AgentScenarioRunner.cs:105-127`). Keep that suite for
its existing purpose. Ensure host-level service resolution is validated and no
optional null constructor parameter silently omits a production dependency.

## 2.5 Conditional Agent Local API decision

**Decision:** keep routes unmapped until a single actual process owner exists.
An in-process SemaphoreSlim in each independent host does not close the gate.
An exclusive headless driver and Desktop may initially use the same contracts
while refusing concurrent authoritative host operation.

If R33 retains execution endpoints after the mandatory slices pass, prefer an
Agent/application host with Desktop and the existing Local API acting as clients
of the same owner. A local authenticated coordinator is also viable, but select
one topology explicitly before implementation. Do not allow both a proxy path
and direct file/service mutation fallback. A lost owner connection means
unavailable/interrupted, never local fallback execution.

The topology decision must cover host bootstrap and lock identity, local IPC
transport/authentication, process death and reattachment, task state revisions,
readiness, token revocation, concurrent Desktop decisions, event subscriptions,
resource coordination, and shutdown ownership. An existing Local API process
may already share read/query stores and initialize resource consumers; do not
accidentally start recovery or duplicate allocation owners in every client.

Endpoint exit gate: all direct Desktop/queue/patch/rewind/subtask mutations use
the owner; two-process tests prove serialization, ownership recovery and
revocation; a token cannot approve; per-token task/workspace/model/Project scope
is enforced against resolved saved identities; the scope editor uses normal
Settings/secret flow. Existing `AgentApiPolicy` and DTOs remain the starting
point, and capabilities must truthfully explain unavailable execution.

No Internet-facing binding, multi-user tenancy, API approval endpoint, broad
filesystem API, automatic execution on reconnect, or new MCP transport.
