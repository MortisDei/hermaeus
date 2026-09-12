# 03. Agent workspace and mutation lifecycle

## 3.1 Preserve the product that exists

**RF:** `AgentService.CreateChildTaskAsync` already inherits task workspace and
Project; sequential child orchestration, model selection, approvals, steering,
transcripts, bounded steps, run ledger and task rewind exist. The workbench has
Run, Changes, Workspace and History. R33 matures these surfaces and guarantees
that their state corresponds to the same underlying task and filesystem.

**OO:** approved malformed writes, stale content after rewrite, misleading
Changes, repeated recovery loops, unreliable child mutation and stale New Task
presentation are symptoms supplied for this round. Source supports several
mechanisms (G1-G4); the precise child failure and rewrite example remain
unreproduced. Do not claim all symptoms have one root cause.

## 3.2 One prepared mutation contract

**Problem:** a known tool name and deterministic risk disposition are not a
complete, valid proposal. `AgentService.RunStepAsync` can queue write approval
before the executor/workspace layer discovers missing args, stale match, missing
file or denied resolved target. `plan_subtasks` also performs schema/model
validation at approval time. Current fingerprints bind tool/args only.

An additional policy seam exists: `AgentViewModel.BuildOptions` supplies no
Policy; direct `AppendApprovalAsync` fixes the workspace root but does not
reload its manifest, whereas queued `AgentPatchReviewService` does. Execution's
`EnforceWritePolicy` reads only the passed policy, and null means unrestricted.
Initial proposal-time denial still works. The missing second check matters if
policy changes while a proposal is awaiting review or a restored pending action
outlives its policy. Add a real propose -> change policy -> approve regression
through both paths. Do not describe current direct approval as always rechecking
the persisted policy merely because the low-level helper has a check.

**P outcome:**

```text
intent -> prepared mutation -> resolved workspace/target -> owner review
       -> approved decision -> execution attempt -> observed filesystem result
       -> artifact verification -> completion decision
```

`Proposed != Approved != Applied != Verified`.

**Boundary:** prepare an immutable typed proposal in Agent before it becomes
approvable. Use the existing workspace containment/policy code and deterministic
safety gate. Do not write from validation. Validate:

- exact tool argument schema/types and required presence; missing content is
  different from an intentional empty string; reject unknown/ambiguous aliases;
- operation kind: create, unique replacement, full replacement, patch, command,
  subtask plan; do not treat a full rewrite as a successful unique substring edit;
- persisted task/workspace identity, resolved relative target, policy, supported
  encoding/text size, existence and pre-image hash/version;
- full proposed output/diff, including deletions, and any truncation/unsupported
  preview. A truncated source preview must not seed a destructive full-file
  replacement as if it were complete;
- child plan schema, depth/budget and resolved model availability before review.

Bind approval to proposal id/revision, task id, workspace identity, operation
kind, target, expected pre-image and payload digest plus the applicable policy
revision. Store approval provenance separately from executor outcome. Changing
any material reviewed fact invalidates approval. Revalidate containment, policy,
pre-image and capability immediately before applying; legitimate filesystem
changes after review may still cause a refusal. Promise an honest refusal,
not that approval guarantees execution under a changed environment.

Route direct approved tools, queued patches, manual editor proposals, child
mutations and rewind through the same owner/policies. A VM must not rewrite
pending subtask arguments or SaveAsync a task behind it. Preserve existing
exact-command approval policy; preflight its optional path and actual recipe
before review too, as the current security-roadmap confusion trigger is met.

**Dependencies:** doc 02 owner/revision contract.
**Verification:** malformed and wrong-type arguments never produce an approvable
pending action; changed policy/workspace/pre-image refuses; empty content is
explicitly reviewed; path casing/symlink/traversal remain denied as appropriate;
manual/child/queue paths cannot bypass preparation.
**Non-goals:** trusting model risk fields, approving a family of commands,
arbitrary shell access, autoapproval through API or editor.

## 3.3 Execution receipts and completion

**RF:** `AgentToolExecutor` normalizes exceptions into Blocked/Unavailable/Failed.
`AgentService.AppendApprovalAsync` can nevertheless append an Applied patch
record whenever pre-image tracking was possible (`AgentService.cs:1395`). A
successful atomic write proves bytes were submitted, not semantic task success.
`AgentPatchReviewService.ApplyAsync` also needs to distinguish a no-effect result
from a change and to record durable verification separately.

**P:** persist the prepared mutation and decision before execution. Record attempt
id, execution outcome, observed existence/hash, changed/no-change/conflict,
verification result, evidence ids and completion reason afterward. Recover a
crash between filesystem write and task save using this receipt, without blind
replay. Apply a short workspace-target lock and pre-image comparison across tasks.
Keep external-editor races visible; do not claim atomic replacement alone is
compare-and-swap or an adversarial filesystem sandbox.

Read back the written artifact after mutation through safe workspace resolution.
For a full replacement, compare expected complete content/hash and check declared
acceptance conditions (for example a replaced section is absent and the new
section occurs once). Exact-byte verification may need an explicit normalization
contract for line endings/encoding. It cannot prove arbitrary program correctness,
factual accuracy or design quality. Those remain declared tests, owner inspection
or Unknown. A model's Final text never upgrades Unknown to Verified.

Applied requires evidence of the intended filesystem change. NoEffect can be
AlreadySatisfied only when expected output is already present and verified.
Blocked/Unavailable/Failed/Cancelled never produce an Applied record. An approval
counter records owner decisions, not successful mutations. Historic patch rows
without receipts remain legacy evidence; do not fabricate post-images.

Task completion must account for pending proposals, failed required mutations,
unverified artifacts and outstanding declared success criteria. Permit an honest
partial completion or owner-ended run with named limitations; do not make every
read-only conversation wait for a filesystem verifier. The model's narrative is
shown as a claim alongside the authoritative outcome, not the outcome itself.

**Verification:** bytes before/after/reopen; no-effect versus actual change;
execution refusal after approval; cancellation after write but before receipt;
restart ambiguity; external edit conflict; multiple tasks writing the same file;
queued patch and rewind siblings; final response after failed write cannot claim
verified success.

## 3.4 Bounded recovery and task isolation

Existing step/error budgets and successful transcript compaction are retained.
Compaction is not failure recovery. Use a bounded failure key of normalized
operation, target, argument/precondition digest and observed failure. Return a
specific correctable error to the model, allow a bounded corrected proposal, and
stop repeated unchanged attempts with an owner-visible blocker. Count nonprogress
read/reason loops without suppressing valid reads needed to repair a proposal.
Never infer approval from repetition. Preserve failed attempts and the reason
for stopping; do not put full file contents in general diagnostic logs.

New Task resets task-scoped response/context/receipt/log/continuation/patch/
outcome/editor selection and cancels or invalidates pending projections. Preserve
chosen model/workspace preferences only where explicitly labeled as reusable
setup. Use task id plus view-generation checks on async returns so an older task
cannot repopulate the new view. Workspace listing preferences are distinct from
a selected artifact belonging to the previous task.

A child inherits canonical workspace identity at creation, not a mutable UI
folder path. Verify create/edit on a real isolated filesystem through parent
plan approval, child review and execution, parent synthesis and reopen. Changing
the active workspace while reviewing a child must not redirect its write. Parent
and child receipts retain separate ids and combine only for presentation.

Artifacts become addressable task outputs. Completion opens the selected primary
artifact in Workspace at useful width, with Changes for review and History for
provenance. No hidden execution, browser network authority or auto-run preview.

**Dependencies:** owner and receipt contracts, then doc 08 UI projection.
**Verification:** slow task-A callbacks after New Task/task-B; approval routed to
child while parent is active; cancellation/restart at child boundaries; repeated
malformed call budget; artifact absent/unreadable/deleted after completion.
**Non-goals:** autonomous swarm, arbitrary parallel subtask writes, universal
semantic verifier, full IDE.
