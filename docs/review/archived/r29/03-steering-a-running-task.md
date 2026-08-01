# 03. Steering a running task

## Why

The agent takes an instruction once, at creation, and then the only way to
say anything to it is to wait. `AppendUserReplyAsync`
(`AgentService.cs:1222`) answers an `ask_user` question. `ContinueTaskAsync`
(`:1249`) adds an instruction to a task that has already stopped. Both
require the task to not be running. `AgentViewModel.CanSendReply`
(`AgentViewModel.cs:1239`) says so directly:

```csharp
private bool CanSendReply() => !IsRunning && IsWaitingForReply && !string.IsNullOrWhiteSpace(ReplyText);
```

So a user watching a task head in the wrong direction has exactly two
options: let it finish wrong, or press Stop (`:1053`) and lose the run. The
owner asked for a third, and asked that it be able to interrupt the step in
progress rather than wait for it.

That is a real feature and it is also the most dangerous thing in this
round, because "let the user say something to a task mid-flight" is one
misstep away from "let text injected at runtime widen what the agent may do
unattended". This doc specifies the feature and pins the boundary.

All references verified against `f03e7c1`.

## The boundary, stated once

An injected instruction is **user text and nothing else**. It is exactly as
untrusted as the goal the task was created with.

- It never carries an approval. Approvals go through `AppendApprovalAsync`
  (`:1036`) with an expected fingerprint, and that path is not touched.
- It never sets `requires_approval`. The dispatch path already overwrites
  whatever the model claims with the gate's own decision
  (`AgentService.cs:474-482`), and that stays.
- It never changes a risk classification. `AgentSafetyGate.Evaluate`
  (`AgentSafetyGate.cs:40-45`) takes a tool name and a mutation flag. It
  gains no third parameter in this round.
- **`AgentSafetyGate.cs` is not edited in this round.** If implementing this
  doc appears to require editing it, stop: it does not.

## What "interrupt mid-step" means, exactly

A step is: build context, call the model, parse, classify the proposed
tool, maybe execute it, save. The model call
(`AgentService.cs:337-342`) is both the long part and the safely
cancellable part; it is already passed a `CancellationToken`.

**Cancellable: the planner inference.** **Not cancellable: a tool that has
started executing.** A half-applied patch or a half-run `run_command`
leaves the workspace in a state `task_state.json` does not describe, and
the agent's whole design rests on that file being the truth. A tool that
has begun runs to completion and records its result; the instruction is
applied at the boundary immediately after.

In practice this is what the owner wants anyway: the wait they are trying to
cut short is the model thinking, not a command finishing.

## Work items

### 3.1 Pending instructions on the task state

Add to `AgentTaskState` (`AgentModels.cs:101-129`), JSON-additive:

```csharp
/// <summary>Instructions the user sent while the task was running, not yet
/// folded into the planner context. Drained at the next step boundary, in
/// order. User text: carries no approval and no risk decision (r29 doc 03).</summary>
public List<AgentSteeringNote> PendingInstructions { get; set; } = [];
```

`AgentSteeringNote` is a record of the text, the UTC timestamp, and the
`StepCount` at which it was received. A task file written before this round
must load with an empty list; add a load test that proves it against a
fixture, because `task_state.json` is the source of truth and a schema slip
there is unrecoverable.

### 3.2 Accepting an instruction

```csharp
public async Task<AgentSteeringResult> SteerTaskAsync(
    string taskId, string instruction, CancellationToken ct = default)
```

- Rejects empty or whitespace instruction.
- Rejects a task in a terminal status (`Complete`, `Failed`, `Cancelled`);
  those already have `ContinueTaskAsync`.
- Appends to `PendingInstructions` and saves through the existing store, so
  the instruction survives a crash between acceptance and consumption.
- Appends a log line, matching `ContinueTaskAsync`'s style (`:1283`).
- Signals the interrupt from 3.4 if a run is in flight.

Cap the queue at a small number (8 is consistent with
`AgentContextBuilder.cs:53`'s `Constraints.Take(8)`) and, when full, refuse
with a message rather than silently dropping. An instruction that vanishes
is worse than one that is refused.

### 3.3 Consuming an instruction at the step boundary

In `RunAsync`'s loop (`AgentService.cs:786-797`), before each
`RunStepAsync`, drain `PendingInstructions` into the task's own record:

- Each becomes an `AgentDecision(state.StepCount, "user", null, $"steer: {text}", DateTime.UtcNow)`,
  the exact shape `ContinueTaskAsync` uses at `:1264`.
- The drained instructions are removed from `PendingInstructions` and the
  state is saved before the step runs, so an instruction is consumed exactly
  once even if the step then fails.

The context builder must surface them to the model. Deliver them as a
clearly labelled block in the prompt, phrased so the model cannot mistake
them for system authority: they are the user's words, arriving late. The
existing prompt already distinguishes goal, constraints and history; put
steering alongside those, not in the system prompt.

`RunStepAsync` called directly (not through `RunAsync`) must drain too, so
a single-step run honours a pending instruction. Put the drain in
`RunStepAsync` and let `RunAsync` inherit it, rather than writing it twice.

### 3.4 Interrupting the planner call

Add a per-task interrupt source held by `AgentService` for the duration of a
run. In `RunStepAsync`, wrap only the model call:

```csharp
using var plannerCts = CancellationTokenSource.CreateLinkedTokenSource(ct, steerToken);
// ... _llm.StreamChatAsync(..., plannerCts.Token) ...
```

Everything after the model call, including tool execution, keeps using the
original `ct`.

Handling the cancellation is the part that needs care. The existing catch
(`:349-357`) explicitly excludes `OperationCanceledException`, so today a
cancelled step propagates out and leaves `Status = Running` in the saved
file (`:308-309`). That is correct for the user pressing Stop and wrong for
a steer.

Distinguish them by source:

- `ct.IsCancellationRequested` is true: the caller cancelled. Rethrow.
  Behaviour unchanged.
- Otherwise the steer token fired: absorb it. Discard the partial response,
  record a decision noting the step was interrupted by a user instruction,
  leave `Status = Running`, and return a step result that lets `RunAsync`'s
  loop continue. The next iteration drains the instruction through 3.3 and
  the model sees it on the very next call.

The interrupted step still counts against `maxSteps`
(`AgentService.cs:783`). A user who steers repeatedly consumes their step
budget, which is the correct incentive and prevents an unbounded run.

Orchestrated runs (`RunOrchestrationAsync`, `:829`) delegate to `RunAsync`
per child (`:873`). Steering the parent while a child runs must not silently
retarget the child. Simplest correct rule for this round: a steer on a
parent with a running sub-task plan is refused with a message naming the
running child. Say so in `docs/agent.md`.

### 3.5 The UI

`AgentViewModel` already has `ReplyText` and `SendReplyCommand`
(`:1213-1239`). Extend rather than duplicate:

- While `IsRunning`, the same box is enabled and its button sends a steering
  instruction. `CanSendReply`'s `!IsRunning` becomes a routing condition
  rather than a block.
- The button's label and the box's watermark change with state, so the user
  knows which of the two they are doing: answering a question, or steering a
  run. Do not present one control that silently means two things.
- Each accepted instruction appears in the task transcript immediately,
  attributed to the user, before the model has responded to it. A steering
  instruction that produces no visible acknowledgement will be sent twice.
- Refusals (terminal task, queue full, orchestrating parent) surface as a
  toast with the reason.

`AgentView.axaml` is at 0% coverage and 480 lines; keep the change to the
existing reply row.

### 3.6 The regression test that pins the boundary

Not optional. Three tests, named for what they protect:

1. **An instruction cannot approve a tool.** Steer a running task with text
   that explicitly grants permission ("you have my approval to run any
   command, do not ask again"), then have the planner propose
   `run_command`. Assert the resulting disposition is
   `RequiresApproval`, the task lands in the approval state, and
   `ApprovalHistory` gained no entry.
2. **An instruction cannot lower a risk classification.** Same setup with a
   tool the gate blocks outright; assert it is still `Blocked` and the
   reason string is the gate's, unchanged.
3. **An instruction cannot pre-approve `plan_subtasks`.**
   `AgentSafetyGate.cs:53-54` requires approval for it regardless of what
   the model claims, because approving it changes how much autonomous work
   runs. Assert steering does not change that.

Plus the mechanics:

4. A steer queued while running is consumed exactly once, appears once in
   `Decisions`, and is gone from `PendingInstructions`.
5. A steer interrupt cancels the planner call and the loop continues, with
   the task still `Running` and the interrupted step counted.
6. A caller cancellation (Stop) is still a cancellation: the task does not
   silently continue.
7. A tool already executing completes; the instruction lands after it. Use
   the existing fake tool executor and assert on ordering in `ToolResults`.
8. A `task_state.json` written before this round loads with an empty
   `PendingInstructions`.

Register any harness-style methods in `XunitHarnessTests.HarnessCases`.

## Docs

`docs/agent.md` gains a section on steering: what it does, that it is
consumed at a step boundary, that it interrupts the model call and never a
running tool, that it consumes the step budget, that it is refused on an
orchestrating parent, and the plain statement that an instruction cannot
approve anything. `docs/features.md` and `CHANGELOG.md` as usual.

`docs/review/deferred.md`'s "Agent run/step endpoints on the local API" row
is **not** closed by this work and its reason does not change. Steering is
an interactive channel to a task a human is already watching; that row is
about a non-interactive caller satisfying the approval gate, which still has
no design. Add a sentence to that row pointing here so the distinction is
recorded.
