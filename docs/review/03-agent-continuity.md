# 03. Agent continuity

The owner's report: an agent session "stopped halfway through its proposed steps, with
no way to continue", and after loading a session from recent tasks there is "no way to
start a new agent session except closing the app".

Verified against the owner's live data root. Task `b5c0d0a1...` in
`C:\AI\Aether\agent\tasks\` is `status: complete` with 7 completed steps and a
still-populated `pending_steps` list of 16 entries (the model declared victory while
its own plan had open items). The runtime log has the matching
`[Error] [Agent] Agent task is already finished.` from the owner clicking the only
affordances available, which all funnel into `AgentService.RunAsync` /
`ExecuteStepAsync`, and those throw for any terminal status
(`src/Aether.Agent/Services/AgentService.cs:223` and `:635`).

## 3.1 Continue a finished or stalled task

New capability: reopening a terminal task with a user instruction.

- `IAgentService.ContinueTaskAsync(string taskId, string instruction, AgentRunOptions
  options, CancellationToken ct)`: loads the task, refuses only when the task is
  actively `Running` with a live loop or has a `PendingToolAction` (that is what the
  review queue is for), otherwise:
  - appends the instruction as a decision entry ("user: continue - {instruction}") so
    the transcript records why the task woke up,
  - resets `Status` to `Running`, clears `ConsecutiveStepErrors`,
  - if the task is an orchestration parent, reconciles child statuses first (the r16
    reconcile path) rather than re-running finished children,
  - resumes the normal loop (same code path `SendReplyAsync`'s resume uses,
    `AgentViewModel` already has the shared resume helper around :795).
- Gate posture unchanged: continuing NEVER auto-approves anything; a reopened task that
  proposes a gated action still goes through the review queue. Depth rule unchanged
  (a child task with `ParentTaskId` set is not continuable directly; continue the
  parent).
- UI: when `CurrentTask` is terminal (`Complete`, `Failed`, or `Blocked`), show a
  "Continue task" row: a single-line instruction TextBox (placeholder "What should it
  do next? Leave empty for: finish the remaining planned steps") plus a Continue
  button. Default instruction when empty: "Continue with the remaining pending steps."

Acceptance: scenario-style test with a scripted LLM: run a task to `Complete`,
`ContinueTaskAsync` with an instruction returns it to `Running`, the next model call's
context contains the instruction, and a second completion is reached; a task with a
pending gated action refuses to continue with a clear message; a child task refuses
with "continue the parent".

## 3.2 New task without restarting the app

`AgentView.axaml` always shows the goal composer, and `CanStart`
(`AgentViewModel.cs:1500-1504`) does not depend on `CurrentTask`, so a new task is
technically startable - but once a task is loaded the workbench is visually and
mentally owned by it, `GoalText` still holds the old goal, and nothing says "you can
just type a new goal". The owner read it as impossible. Treat that as the bug: the
affordance must be explicit.

- Add a "New task" button next to the task header (visible whenever `CurrentTask` is
  not null and `IsRunning` is false). Command clears: `CurrentTask`, `_openedTaskId`,
  `_currentTaskParentGoal`, `GoalText`, `ReplyText`, `StatusMessage`, error state, and
  refreshes the preview panels to their empty state. It must NOT touch the persisted
  task (loading it again from Recent tasks resumes exactly as before) and must NOT
  clear the review queue.
- While a task is running, the button is hidden (Stop first, then New task).
- After `StartAsync` completes a run, keep current behaviour otherwise.

Acceptance: VM test: load a recent task, invoke NewTask, assert composer state is
pristine and the store still contains the untouched task; Start with a fresh goal then
creates a task with a new id.

## 3.3 Premature "complete" must be visible

The stall in 3.1 was the model declaring `complete` with 16 pending steps. The loop
cannot stop models from being lazy, but the UI must not present that as a clean finish.

- When a task reaches a terminal status with a non-empty `pending_steps` (or an
  orchestration plan with unfinished children, which r16 already surfaces), compute a
  short honesty note on the task summary: "Finished with N planned steps not run." Show
  it beside the status chip in the task header and in the Recent tasks list item
  (`AgentTaskListItemViewModel`), so a half-done task is recognizable at a glance.
- This is presentation only: no status rewriting, no automatic reopening. Pair it with
  3.1's Continue affordance (the note plus the Continue box together answer "it stopped
  halfway, now what").

Acceptance: VM test: a terminal state with pending steps produces the note text with
the right count; empty pending steps produces no note.
