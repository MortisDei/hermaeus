# 01. The review queue is a queue

## The defect

The owner reported that the Review Queue entries never leave: Approve can be
clicked forever on the same row. That is real, and it is the symptom of three
separate faults that compound.

### Fault 1: the query returns history, not a queue

`FileAgentTaskStateStore.ListReviewQueueAsync`
(`src/Hermaeus.Agent/Services/FileAgentTaskStateStore.cs:102-136`) selects:

```sql
WHERE t.status IN ('WaitingForUser', 'Blocked') OR t.approval_count > 0
```

The first clause is the queue. The second clause is an archive: any task that
has ever had a single approval recorded stays in the list permanently,
regardless of status. A `Complete` task with one approval in its history is
indistinguishable, in this list, from a task sitting paused waiting for you
right now.

Approving increments `approval_count`, so approving never removes a row. It
guarantees the row stays.

### Fault 2: the row offers decisions it does not have

`AgentView.axaml:388-401` renders Approve, Reject and Open on every row
unconditionally. `HasPendingAction` (`AgentViewModel.cs:112`) exists and is
already used at `AgentView.axaml:370`, but only to show or hide the *detail*
block that describes the pending action. The buttons that act on it are
outside that `IsVisible`.

So a row with nothing pending shows an Approve button in exactly the same
visual state as a row with a gated `run_command` waiting on it.

### Fault 3: the approval path mutates a task that has nothing pending

This is the one that does damage. `AgentService.AppendApprovalAsync`
(`src/Hermaeus.Agent/Services/AgentService.cs:932-1066`):

- Line 972 appends an `AgentApprovalRecord` unconditionally, before any check
  that there is something to approve.
- Lines 1054-1062, the `else` branch taken when `PendingToolAction is null`,
  sets `state.Status = approved ? AgentTaskStatus.Running : AgentTaskStatus.WaitingForUser`
  and saves.
- Line 1065 returns `new AgentApprovalResult(true, string.Empty)`, so the
  caller believes the approval applied.

Then `AgentViewModel.ApproveReviewAsync` (`AgentViewModel.cs:979-1010`) sees
`result.Applied == true` and calls `ResumeAgentLoopIfRunnableAsync`, whose
only guard (`AgentViewModel.cs:1085`) is that the target's status is
`Running`. Which `AppendApprovalAsync` just set it to.

**Net effect: clicking Approve on a finished task in the review queue
un-completes it and starts the agent loop running on it again.** Clicking
Reject on a finished task sets it to `WaitingForUser`, which is a status a
`Complete` task should never hold. Both write to `task_state.json`, which is
the source of truth.

The fingerprint guard added in r23 4.1 does not catch this. It only fires
when `pendingAtStart is not null` (`AgentService.cs:946`); with nothing
pending there is no fingerprint to mismatch, so the guard passes by being
inapplicable.

### Fault 4 (minor, same family): the queue does not refresh when it changes

`StartAsync` (`AgentViewModel.cs:854`) and `RunStepAsync`
(`AgentViewModel.cs:880`) call `RefreshRecentAsync` after a run. Neither
calls `RefreshReviewQueueAsync`. A run that pauses for approval therefore
does not appear in the queue until the user finds and clicks the "Refresh
Queue" button at `AgentView.axaml:278`. Combined with faults 1 to 3, the
queue shows stale history and hides the live item. That is the whole of the
"borked" feeling.

## Work items

### 1.1 The query lists what needs a decision

Drop the `OR t.approval_count > 0` clause from `ListReviewQueueAsync`. The
`WHERE` becomes:

```sql
WHERE t.status IN ('WaitingForUser', 'Blocked')
```

Everything else about the method (the parent join, the `PendingToolAction`
hydration pass at lines 141-149, the ordering, the limit) is correct and
stays.

Approval history does not need a new home. It already has two: the run
ledger's Approvals section, rendered at `AgentView.axaml:952-962` from
`LedgerApprovals`, and each row's own `ApprovalLabel` /
`LatestApprovalLabel`. Do not build a third.

Tests:
- A `Complete` task with `approval_count > 0` is absent from the queue.
- A `Failed` task with approvals is absent.
- A `WaitingForUser` task with zero approvals is present (the fresh case the
  old first clause already handled, asserted so 1.1 cannot regress it).
- A `Blocked` task is present.
- Approving the pending action on a `WaitingForUser` task removes it from the
  next `ListReviewQueueAsync` result. This is the owner's bug, stated as an
  assertion.

### 1.2 An approval with nothing pending is refused

In `AppendApprovalAsync`, before line 972's `ApprovalHistory.Add`, return
early when there is nothing to decide:

```csharp
if (state.PendingToolAction is null)
    return new AgentApprovalResult(false, "This task has no action waiting for a decision.");
```

No history record, no status change, no save. The task is left exactly as it
was found.

This is the item's core. Every other change in this doc makes the wrong
button harder to reach; this one makes the wrong button harmless.

Note the ordering constraint: this check must sit **after** the `LoadAsync`
that throws on a missing task, and **before** the fingerprint block, because
the fingerprint block's own null-handling (`AgentService.cs:946`) is written
on the assumption that a null pending action is a legitimate no-op rather
than a caller error. It is now a caller error.

`AgentApprovalResult.Applied == false` is already the "nothing executed"
contract from r23 4.1, and `ApproveReviewAsync` already handles it correctly
at `AgentViewModel.cs:986-993`: it surfaces `result.Message` in
`StatusMessage` and returns without resuming. `RejectReviewAsync`
(`AgentViewModel.cs:1110-1117`) currently discards the result; make it
surface the message the same way.

Tests:
- Approving a `Complete` task with no pending action: returns
  `Applied == false`, the task's status is still `Complete` afterwards, and
  `ApprovalHistory.Count` is unchanged.
- Rejecting a `Complete` task with no pending action: same three assertions.
- The reloaded-from-disk assertion for both, since the bug's damage is what
  reached `task_state.json`.
- Approving a task that *does* have a pending action still executes it and
  returns `Applied == true`. The existing coverage in
  `AgentOrchestrationViewModelTests.cs:117-158` covers the orchestration
  shape of this; add the plain single-task case if it is not already
  asserted directly.

### 1.3 A row shows only the decisions it actually has

In `AgentView.axaml`, the Approve and Reject buttons move inside a container
bound to `HasPendingAction`. Open stays unconditional.

A queue row without a pending action is not an error state, it is a different
one. Two cases, and each gets its own honest line where the buttons were:

- `WaitingForUser` with no pending action: the agent asked you a question.
  Say so, and offer Open, which loads the task into the workbench where the
  reply box (`AgentView.axaml:414-437`) already exists.
- `Blocked`: the run stopped and needs an instruction. Say so, and offer
  Open, which reaches the Continue box (`AgentView.axaml:443-471`).

Add `NeedsReply` and `NeedsInstruction` to `AgentReviewQueueItemViewModel`
(`AgentViewModel.cs:41-116`) rather than putting status comparisons in the
axaml. They are pure derived properties over `Status` and `HasPendingAction`
and can be tested without a view.

Tests: the three states of one queue row (pending action, awaiting reply,
blocked) each expose exactly one true action flag.

### 1.4 The queue refreshes when a run pauses

Call `RefreshReviewQueueAsync` alongside the existing `RefreshRecentAsync` in
`StartAsync` (`AgentViewModel.cs:854`) and `RunStepAsync`
(`AgentViewModel.cs:880`), and in the `finally`-adjacent refresh inside
`ResumeAgentLoopIfRunnableAsync` (`AgentViewModel.cs:1095`).

Then delete the "Refresh Queue" button at `AgentView.axaml:278-281`. A
manual refresh button for a list the app can keep current itself is an
admission, not a feature. `RefreshReviewQueueCommand` itself stays: it is
still called from `LoadCoreAsync` (`AgentViewModel.cs:793`) and by the three
call sites above.

Test: after a run step that leaves the task `WaitingForUser` with a pending
action, `vm.ReviewQueue` contains that task without any explicit refresh
call.

### 1.5 The header tile stops calling history a queue

`AgentView.axaml:71` renders `{0} queued reviews` from `ReviewQueueCount`
(`AgentViewModel.cs:636`). After 1.1 that count is finally what the label
says, so the label is now true rather than misleading. Keep the binding and
change the wording to name the decision rather than the object: "N waiting on
you", and zero states as "Nothing waiting on you".

This tile is currently inside the "Task history" card
(`AgentView.axaml:67-73`), which is why it read as history in the first
place. Doc 02 moves it to the pinned decision strip, where it belongs. If doc
02 is descoped, the wording change still lands here on its own.

## What this doc does not do

- It does not change risk classification, the safety gate, or what counts as
  a gated action. `AgentSafetyGate` is untouched.
- It does not add a bulk "approve all". Every approval is one decision about
  one named action, and that is the product.
- It does not add an approval-history panel. Two already exist (1.1).
- It does not change `AgentApprovalResult`'s shape. r23 4.1 already defined
  `Applied == false` to mean "nothing executed, here is why"; 1.2 is a second
  caller of an existing contract, not a new one.
