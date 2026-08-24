# Agent Workbench

## Overview

The **Agent** workspace is a local-first, approval-gated task runner. It works
one goal at a time and keeps state outside the model (`task_state.json`, a
persisted step transcript, and a per-machine lesson store) instead of relying
on whole-chat-history context. It can run several steps in a row without a
click for each one, but every write, command, or MCP call still requires
explicit user approval before it executes.

### Task Management

- Builds explicit task state and compact context packs.
- Records `task_state.json`, `agent.log`, `agent.trace.jsonl`, and
  `transcript.jsonl` under the Hermaeus data root.
- Maintains `agent/task_index.db` as a SQLite catalog for recent-task and review
  queue lists, with `task_state.json` remaining the source of truth for full
  task state. Initialization reconciles JSON task files back into the index.
- Validates task IDs with a short alphanumeric, dash, and underscore allowlist
  before resolving task directories.
- Shows a review queue of tasks that need a decision right now: those
  `waiting_for_review` or `blocked`, and nothing else. A task that has been
  approved in the past is not in the queue; its approvals live in the run
  ledger and in that task's own approval labels. A row with a gated action
  pending offers Approve and Reject; a row waiting on an answer or a blocked
  run says so and offers Open, which loads the task into the workbench where
  the reply and Continue boxes are. The queue refreshes itself whenever a run
  pauses, so there is no manual refresh to remember.
- An approval or rejection sent to a task with nothing pending is refused and
  says so. It appends no approval record, changes no status, and never
  restarts a finished run.
- **Dismiss** is the way out of the queue for a run you are done with. It
  discards the pending action without executing it and closes the task as
  `cancelled`, so the row leaves the queue for good; the task stays in Recent
  Tasks with its run ledger, approval history and transcript intact, and the
  Continue box can still reopen it. Dismiss records no approval, because
  walking away from a decision is not making one. It is refused on a task that
  is still running (stop it first) and on a sub-task child (dismiss the parent,
  or its orchestration would wait forever on a child that never finished).
- Shows a Recent Tasks list (status chip, goal, relative time, pending step
  count; sub-task children indented with a tag) so a completed task's
  report, a failed task's blockers, or an orphaned task is reachable after a
  restart without going through the review queue. Opening a child directly
  also shows its parent's goal.
- A task that reached a terminal state before its own plan actually
  finished (the model stopped short, or the run hit `Agent.MaxAutoSteps`)
  shows a note naming why, alongside a Continue box: typed instructions call
  `ContinueTaskAsync`, which resets the task back to a runnable state and
  reconciles any sub-task plan, rather than requiring a brand new task for
  what is really unfinished work. `ContinueTaskAsync` refuses with an
  explanation when the task is a sub-task (continue the parent instead), is
  already running, or has a tool approval pending. A "New Task" button next
  to Start always starts an actual fresh task.

### Context & Retrieval

- Searches and reads bounded text files under a selected workspace root, with
  glob matching, optional regex search, and line-ranged reads for large files.
- Can include relevant context from an optional RAG dataset.
- Replays a budgeted tail of the task's own step transcript (see below). The
  persisted `transcript.jsonl` remains complete; model-facing replay compacts
  only consecutive replay-safe tool outcomes with the same tool,
  canonical arguments, and result. The first outcome remains with a count and
  deterministic step/entry range. Failed, timed-out, denied, changed, and old
  entries without the required provenance stay separate. Three or more
  unchanged successful calls add an informational context-receipt diagnostic;
  it never blocks a call or changes task status or loop behavior.
- Every new tool, approval, safety-gate, and rewind result carries a
  provider-neutral normalized outcome alongside its existing raw summary,
  exit code, timeout, patch detail, and provenance. Outcomes distinguish
  success, partial success, no effect, unavailable dependencies, user denial,
  deterministic blocking, failure, cancellation, timeout, and unknown. They
  are derived only from executor or gate evidence, never from model-authored
  result prose. Pre-R31 task files load as `Unknown` without reinterpretation.
- Model-facing replay includes that normalized label plus the bounded raw
  summary. Only deterministically successful results are replay-safe; outcome
  labels never grant authority or bypass an approval, workspace, token, or
  destructive-action guard.
- Surfaces relevant lessons from the self-learning store (see below).
- Classifies risky actions before execution.

### Workbench layout

The panel is a fixed status line, a pinned decision strip, and four tabs.

- **Status line.** One row: task status, step count, goal, the latest status
  message, and the New task / Run Step / Stop controls. It is a fixed set of
  scalars, so a long plan can no longer squeeze the working area toward zero.
  The step count is the task's whole life ("step 111"); the `Agent.MaxAutoSteps`
  budget caps one autonomous run, so it is shown as "(4 of 20 this run)" only
  while a run is spending it, rather than as a denominator on a lifetime count.
  While a run is in flight it also carries an indeterminate progress bar and a
  rotating activity line ("Step 3: Weighing the plan... 12s"), naming the
  current step and how long it has been going. Without it a long model call
  left every label frozen and the panel read as hung. The clock and the word
  rotation restart on each step, so the line reports the current step's wait
  rather than the age of the whole run, and it stays empty for the first
  second and a half so a fast step never flickers a placeholder.
- **Decision strip.** Sits above the tabs and collapses to nothing when
  nothing is waiting on you. It carries the review queue, the reply box (with
  the agent's actual question shown above it) and the Continue box. This is the
  rule that makes tabs safe here: the thing the agent is waiting on is never
  behind a tab.
- **Run.** Goal, workspace, model and RAG dataset; the Start Agent button; the
  run outcome for a finished task; the agent's own response; sub-tasks and
  plan; and the task state, context receipt and retrieved context, collapsed.
  The task's frozen model identity is shown independently of the current model
  picker. Changing the picker does not retarget an existing task. A paused task
  can use **Use for task** to make an explicit, audited model change after the
  newly selected model is checked against the current visible model list.
  A project-bound task receives a separate bounded `Project State` receipt
  section only when accepted State exists. It names the accepted revision;
  pending and rejected proposals are excluded.
- **Changes.** The draft patch queue and the run ledger (files, before/after,
  commands, approvals, and Rewind run). Carries a count when patches are
  waiting on you; no other tab has a badge.
- **Workspace.** What the agent can do here, the workspace policy, the
  workspace profile, the file browser with its draft patch composer, and
  workspace memory. Re-analyse workspace and Save as workspace defaults are
  here, next to what they write.
- **History.** Recent tasks, new lessons from this task, the lesson store,
  scenario evals, and the agent log.

The panel opens on Run every time and never switches tabs on its own. A
finished run lights the Changes badge and says so in the run outcome; it does
not move the page under you.

### What the agent can do here

The Workspace tab's capability list is derived, not written down: it is built
from the executor's actual tool set, this workspace's declared command
recipes, its workspace policy, and whether an MCP bridge is configured. With
no recipes declared it says commands cannot run here and names
`.hermaeus/workspace.json` as where recipes are declared; with recipes
declared it names them. With no MCP bridge it says the agent cannot reach
outside the folder; with one it says calls go through the servers you
configured and each one is gated. With no workspace selected it says the
answer depends on the workspace, and that nothing runs without an approval you
give.

A test asserts that every tool the executor accepts is accounted for by
exactly one line, so the text cannot drift away from the code again.

### Did that actually work

When a task reaches a terminal state, the Run tab shows a short outcome above
the fold, composed entirely from the run ledger and the task's own state:

- files changed, split created and edited, with the summed line delta,
- commands run and how many failed (a failed command is reported even when the
  task itself completed),
- approvals asked for, split approved and rejected,
- the unfinished-plan note when a terminal task still has pending steps,
- the model's own reservations, and
- a plain "this run changed no files and ran no commands" when that is what
  happened.

It computes no new facts, calls no service, and carries no score, grade or
percentage. The model's own account of the run is separate, under "Agent
response".

The Workspace tab includes a workspace file browser with query, list, preview,
and summary support so you can inspect local workspace files without leaving
the workbench.

Draft patch proposals are also available from the workspace file browser. You
can enter a rationale, review the generated patch preview, queue the patch for
review, and then approve or reject queued patches from the dedicated panel.
Approving a queued patch applies the proposed content to the selected workspace
file immediately and refreshes the preview. Queued patches expose explicit
pending, applied, rejected, and blocked states so review decisions are visible
at a glance.

## Autonomous Runs

Clicking Start (or approving a gated action while a run was in progress) does
not stop after one model call. The agent keeps running steps on its own until
one of the following happens:

- the model reaches a final answer,
- the model asks the user a question,
- an action needs approval (a gated tool, an MCP call, or a command),
- the task becomes blocked or fails, or
- the task hits `Agent.MaxAutoSteps` (Settings; default 20) - a safety valve
  against runaway loops, not a normal stopping condition. Hitting it hands
  the task to `waiting_for_review` with a note in the log and transcript
  rather than leaving it looking silently active.

The workbench shows live progress (current step, tool, status) as the run
proceeds, and Stop cancels mid-run at any point. A manual "Run step" advance is
still available for stepping through a task one model call at a time. Nothing
about this changes what is allowed to execute without approval; the loop only
removes the need to click through every read-only step by hand.

An unreadable model response does not stop the run. The response is recorded,
a corrective note is appended to the transcript naming what was wrong with it,
and the loop takes another step; three unreadable responses in a row still fail
the task. Before this, one bad response synthesized an `ask_user`, which parked
the task in `waiting_for_review` and showed the user a reply box for a question
the agent had never asked, while the three-strike budget was unreachable
because reaching it took three manual Run Step clicks.

A directory listing describes the workspace, so it gets a budget suited to that
rather than sharing the search-hit cap. `list_files` returns entries sorted, and
includes directories (with a trailing separator) so a folder is never invisible
just because its own files were filtered out. When a listing stops early it says
so and says how to narrow it, because "not in this list" is not the same as "not
present" and a real run drew exactly that wrong conclusion about a folder that
existed.

A truncated read is never a dead end. `read_file` results carry the line range
they cover and a continuation hint naming the exact `line_offset` to ask for
next, and a file over the whole-file byte cap can still be read in slices (the
size ceiling applies to reading a file whole, not to a bounded line range). The
system prompt says so too. Before this, a result said only `"truncated": true`
and a real run concluded the tool "cannot return the entire file content in one
go" and abandoned the file.

A response that names a tool in `next_action.type` (for example
`"type": "set_plan"` with a null `tool_name`) is repaired into the protocol's
own shape and executed, rather than being rejected as unparseable. This is a
common local-model mistake and the response is otherwise complete and valid.
The repair corrects the shape of the request, never its authority: the safety
gate classifies the resulting tool exactly as if the model had named it
correctly, so a repaired action can no more skip approval than any other.

A task waiting on `ask_user` shows a reply box in the workbench; answering it
appends the reply to the transcript and resumes the run, the same
approve-and-continue shape as a gated-action approval. A reply is never
accepted while a tool approval is also pending on the same task - those are
answered separately, on their own explicit approve/reject path.

## Steering a Running Task

The agent used to take an instruction once, at creation. Watching a task head
in the wrong direction left two options: let it finish wrong, or press Stop and
lose the run. Steering is the third.

While a task is running, the same box that answers an `ask_user` question sends
an instruction instead. Its caption, watermark and button label change with the
state, so it is always clear which of the two you are doing, and an accepted
instruction appears in the transcript immediately, before the model has
responded to it.

What happens to it:

- It is queued on `task_state.json` (`pending_instructions`) so it survives a
  crash between being accepted and being used, and held in memory as well so a
  step already in flight cannot overwrite it with the state it loaded before the
  instruction arrived.
- It is **drained at the next step boundary**, in order, and folded into the
  planner's context pack alongside the goal and the constraints. It is consumed
  exactly once, even if that step then fails.
- It **interrupts the model call in progress**. The planner inference is the
  long part of a step and the safely cancellable part, so a steer does not wait
  for it. The partial response is discarded and the next step starts fresh with
  the instruction in context.
- It **never interrupts a tool that has started executing.** A tool runs to
  completion and records its result; the instruction lands at the boundary after
  it. Cancelling a half-written patch apply or a half-run `run_command` is how a
  workspace ends up in a state `task_state.json` does not describe.
- The interrupted step **still counts against the step budget**
  (`Agent.MaxAutoSteps`). Steering repeatedly spends your budget, which is the
  correct incentive and keeps a run bounded.

**An instruction cannot approve anything.** This is the boundary, and it is not
negotiable. An injected instruction is user text, exactly as untrusted as the
goal the task was created with. It carries no approval, it never sets
`requires_approval`, and it never changes a risk classification. Telling a
running agent "you have my approval to run any command, do not ask again" does
nothing at all: the next `run_command` still stops at the gate. Approvals go
through the explicit approve/reject path with an expected fingerprint, and
nothing else reaches it. Three regression tests exist solely to keep this true.

Steering is refused, with the reason shown, when:

- the instruction is empty;
- the task has finished (`Complete`, `Failed`, `Cancelled`) - use **Continue**,
  which reopens the task with a new instruction;
- the task is an orchestration parent with a running sub-task, in which case the
  refusal names the child so you can steer that instead;
- eight instructions are already waiting. An instruction that silently vanishes
  is worse than one that is refused.

## Plan Checkpoint and Completion Honesty

- **Plan-approval checkpoint** (`Agent.RequirePlanApproval`, Settings;
  default off): when enabled, a fresh task's first successful `set_plan`
  pauses the run instead of continuing, so the plan can be reviewed before
  anything unattended happens. The Continue task box (the same one used to
  resume a task that stopped early) resumes the run from there. This fires
  at most once per task, even across a restart, and never for a sub-task -
  its own `plan_subtasks` approval already showed the full sub-task plan
  before anything ran. If the model proposes a gated action before ever
  calling `set_plan`, the existing approval flow already provides the
  checkpoint; this never adds a second pause on top of it. Off by default
  because it adds a click to every run; some users trust plans more than
  opaque momentum, and this is their choice to opt into.
- **Visible plan revisions**: `set_plan` still replaces the whole checklist
  each call (it stays a Safe, non-gated tool), but a call that replaces a
  non-empty existing plan now logs `plan revised: 4 items -> 6 items`
  (counts, not a diff) to `agent.log` and the transcript, and the plan panel
  shows a small "revised at step N" annotation.
- **Completed with reservations**: a final answer may optionally carry a
  short list of specific things the model looked for and could not verify
  or finish. This is never a numeric confidence score - a self-reported
  percentage is theatre, not measurement - and never required or nagged
  for; an empty or absent list shows nothing. A task completing with
  reservations shows "Completed with reservations" in the recent-tasks list
  (status stays `Complete`); the reservations render as part of the Run tab's
  run outcome for that task, and orchestration synthesis
  carries a child's reservations into the parent's `report.md` under a
  "Reservations" heading.
- **Context receipt**: a collapsed-by-default "Context receipt" expander in
  the workbench shows the latest step's context sections exactly as
  `AgentContextReceiptBuilder` computed them - label, item count, token
  estimate, and item identifiers. Read-only, no new persistence; the
  `agent.trace.jsonl` trace remains the tool for step-by-step archaeology.

## Sub-task Orchestration

For a broad, multi-domain goal, the agent can request `plan_subtasks`: a
proposal to split the goal into 2 to 6 focused sub-tasks, each with a goal, a
specialist profile, and success criteria. Like every other gated action, this
always requires approval - the preview shows the exact sub-tasks (profile and
goal) you are authorizing before anything runs. `plan_subtasks` is never the
right tool for a goal that already fits a normal plan; `set_plan` remains that
tool.

Once approved, the plan is materialized on the parent task and children run
**sequentially**, one at a time, through the exact same loop, safety gate, and
per-action approval flow as any other task - a child is an ordinary task with
`ParentTaskId` and its own `WorkspaceRoot` (inherited from the parent), its
own transcript, its own lessons, and its own `RememberedCommandApprovals`
(never shared with siblings or the parent). A child that pauses for approval
or a question shows up in the review queue like any other waiting task,
labeled with its parent's goal, and approving it resumes the **parent's**
orchestration (looked up by the queue entry's own parent id, not by whatever
task happens to be open in the workbench). The parent's own status mirrors a
paused child's (`WaitingForUser`/`Blocked`) and names which sub-task it is
waiting on, instead of showing `Running` with nothing happening.

A child can reach `Complete`/`Failed` outside the orchestration loop entirely
- opened directly from the recent-tasks list and stepped to completion, for
example. The parent self-heals this on its next run: before choosing what to
advance, it reconciles every sub-task spec against its child's actual
persisted status, so orchestration is never permanently stuck believing an
already-finished child is still running. A parent with any unfinished
sub-task never takes a bare parent model step itself (Run Step routes through
the orchestration loop instead when the open task has one), and a task whose
plan already exists cannot accept another `plan_subtasks` proposal.

Depth is limited to one level, enforced in code rather than by the model: a
child that itself requests `plan_subtasks` is blocked immediately, with the
reason recorded on the safety gate, and the run continues with its remaining
siblings rather than crashing.

The whole orchestrated run (every child's steps plus final synthesis) is
capped by `Agent.MaxOrchestrationSteps` (Settings; default 60), separate from
each child's own `Agent.MaxAutoSteps`. If the ceiling is hit, every remaining
pending sub-task is marked `Skipped` and the run proceeds straight to
synthesis, which says plainly that the run was truncated by budget rather than
pretending everything finished.

Once every sub-task is `Complete`, `Failed`, or `Skipped`, the parent runs one
final model step to synthesize a consolidated report from each child's outcome
(no child transcript replay - summaries only) and writes it to `report.md` in
the parent's task directory, openable from the workbench. If that synthesis
step itself fails or returns something unusable, a deterministic fallback
report (built from the sub-task specs themselves) takes its place instead of
failing the whole run - the sub-task work already happened.

### Per-subtask model selection

The plan review shows a model selector for every proposed child. Choices are
limited to configured visible models plus an explicit **Inherit parent** option.
The selection is written back into the pending plan and its approval fingerprint
is recomputed before approval, so the approved payload is exactly the plan that
materializes. Unknown, hidden, removed, or unavailable explicit model ids are
rejected before any child is created.

Each resolved child model is persisted on the sub-task spec and child task before
execution. Task state, recent-task and sub-task rows, transcripts, traces,
context receipts, child reports, and the final synthesis input retain that model
identity. Siblings may use different models, while synthesis always returns to
the parent's persisted model. A stopped or missing frozen model blocks visibly
with an actionable message and no inference call or silent fallback. Model choice
does not change tools, workspace policy, risk classification, approvals, depth,
or orchestration budgets.

Explicit design limitations: no parallel child execution (one local
model at a time, one GPU), no nesting beyond depth 1, no user-editable specialist
profiles yet (the fixed catalog - `general`, `correctness`, `security`,
`tests`, `performance`, `docs` - ships first), no approval inheritance of any
kind, and no background/detached orchestration (the run lives in the workbench
session like any agent run; a crash-resumable parent is enough).

## Transcript

Each task keeps a persisted step transcript (`transcript.jsonl`, one line per
assistant thought, tool result, or user reply) that is replayed - budgeted,
most recent steps prioritized - back into the model's context on every
following step (`Agent.TranscriptTokenBudget`, default 12,000 tokens). This is
what lets the agent still "remember" a file it read three steps ago instead of
only ever seeing the last five, truncated tool results. Every executed tool
result reaches the transcript this way, including a gated action's result
once it is approved - not just the ones read-only steps ran on their own.
`agent.trace.jsonl` remains the separate, schema-oriented audit log (full
context pack on step one, a small delta per step after that); the transcript
is what the model actually reads.

A model that supports native tool calling gets a transcript as informative as
the JSON-protocol fallback's: any prose it streamed alongside a tool call
becomes the recorded thought instead of a synthetic placeholder, and if it
requested more than one tool in the same turn, the dropped calls are named so
the model sees next step that only the first one ran.

## Tools

- Read-only file tools for workspace inspection: `list_files` (optional
  subdirectory and depth), `search_files` (optional regex and context lines),
  `glob_files` (`*`/`**` patterns), `read_file` (optional line range),
  `summarize_file`, `draft_patch`, and `inspect_git_diff`.
- `set_plan`: replaces the task's visible plan checklist. Executes
  immediately; it only touches task state, never files or commands, so it
  never requires approval.
- `plan_subtasks`: proposes splitting a broad goal into 2-6 focused
  sub-tasks (see Sub-task Orchestration below). Always requires approval.
- Approval-gated write tools:
  - `edit_file` (relative_path, old_string, new_string) - the primary way to
    change part of an existing file. `old_string` must match the file's
    current content exactly once; zero or multiple matches refuse the edit
    rather than guess.
  - `create_file` (relative_path, content) - new files only; refuses to
    overwrite an existing one.
  - `apply_draft_patch` - whole-file rewrite, for the cases `edit_file` isn't
    a fit.
- Approval-gated command execution (`run_command`): a fixed set of template
  families, and only when the workspace itself declared that family safe in
  `.hermaeus/workspace.json`. The families are the verbs a developer runs to
  check their own work: `dotnet build`/`dotnet test` with an optional project
  path; `npm`/`pnpm`/`yarn` `test` and `run <script>`, where the script must
  already exist in the workspace's own `package.json`; `cargo build`,
  `cargo test`, `cargo check`, `cargo clippy`; `go build`, `go test`, `go vet`,
  which also accept Go's `./...` package pattern; and `pytest` /
  `python -m pytest` with an optional path. A command containing shell
  metacharacters (`&`, `|`, `;`, backtick, `$`, `<`, `>`, a newline) matches no
  family at all: nothing is ever launched through a shell, so they could not
  have been interpreted, but such a string must not reach an approval prompt
  looking legitimate either.

  Absent by decision, not oversight: installers (`npm install`,
  `dotnet restore`, `pip install`) reach the network and pull in third-party
  code; formatters (`cargo fmt`, `dotnet format`) rewrite source outside the
  patch queue where the user would see the diff; long-running processes
  (`dotnet run`, `npm start`) are not a verification step; and `make`, `mvn`
  and `gradle` can run arbitrary targets with no cheap declared-target check
  equivalent to `package.json`'s scripts. Those declarations are editable from the
  Workspace tab's Command Recipes panel: pick a family from the fixed list,
  optionally narrow it with an argument, and it is written to the manifest
  straight away; Remove takes it back off. The picker offers only families the
  safety gate can accept, so a recipe cannot be declared that would be refused
  at run time, and nothing about the editor widens what the gate allows. When
  the agent asks for a family the workspace has not declared, the blocked row
  names that family and offers to declare it in one click; the action still
  needs its own approval afterwards. When it asks for something outside the
  families entirely, the refusal says so and offers nothing, because there is
  nothing the user could allow that would make it runnable.
  Optional path arguments go through the
  same containment checks as every other workspace file path. Always requires
  approval, even for a declared-safe family. After the user approves a given
  command string once in a task, an identical repeat of that exact string may
  auto-execute for the rest of that task; a different command - even in the
  same family - still requires its own approval, and the memory never
  survives past the task.
- MCP tools (Settings > MCP Servers): each configured server's declared
  tools are exposed to the agent as `mcp:{serverId}:{toolName}`. A server can
  optionally be restricted to an explicit allowed-tools list (comma
  separated); leaving it empty permits every tool the server declares,
  matching the original behavior. Every `mcp:` call always requires approval
  regardless of what the server or the allowlist says, and the bridge also
  refuses to forward a tool name the server did not actually declare via
  `tools/list`, even if it happens to appear in a stale allowlist entry.
- Proposed next actions with safety gates.
- Local logs and JSONL traces for debugging.
- Approval-gated draft patch queue with approval metadata and task-state
  persistence.

### Native tool calling

When the configured model/provider supports OpenAI-style tool calling
(OpenAI-compatible endpoints, llama.cpp's server, or Ollama), the agent
declares its fixed tool set natively instead of asking the model to hand back
JSON matching a schema, and consumes the returned tool call directly. A
model or provider that ignores the declared tools (or doesn't support them)
falls straight back to the existing "return JSON" text protocol automatically
- the same request works either way, and local models without tool-calling
support remain fully supported. MCP (`mcp:`) tools are not declared natively;
they remain reachable only through the JSON protocol.

### Constrained planner protocol

The JSON text protocol is therefore not a legacy path for weak models. It is
the only path to every MCP tool, for every model, whenever the provider does
not return native tool calls.

Since r28, that protocol has a real JSON schema, and the schema is sent as an
output constraint whenever the selected model's provider can enforce one
(`docs/features.md`, "Constrained output"), which includes every local
llama.cpp and Ollama model. The sampler is what keeps `next_action.type` to
the four action kinds and `risk_level` to its four values, rather than a
prompt asking politely and an extractor cleaning up afterwards.

Order of precedence is unchanged: a provider that returns native `tool_calls`
still takes that path, and the constraint applies to the text protocol that
runs otherwise.

**A constrained response is exactly as untrusted as an unconstrained one.**
Constraining a shape makes an answer parseable, not authoritative. Every
action still goes through the same classification from the tool name, and a
schema-valid response asserting `"requires_approval": false` for a high-risk
tool is blocked, with the gate's own reason, exactly as before. A regression
test pins that across the constrained and unconstrained paths side by side.

Nothing was removed to make room for this. `ExtractJson`, the targeted
`next_action.type` repair described above, the error budget and the `AskUser`
fallback all still run, because they are what a provider that cannot constrain
falls back to. What changes is how often they are reached. Each task records
whether its planner calls were constrained, visible in the task's own trace, so
"did this help" can be answered from real runs rather than claimed.

The message shown when a response cannot be parsed now depends on that:
unconstrained, it says the provider could not enforce a shape and that a local
llama.cpp or Ollama model has it enforced for it; constrained, it says the
shape was enforced and the model missed anyway, which is a different problem
with a different answer. It no longer tells a local-first user their only
option is a bigger model.

## Lessons (self-learning)

The agent keeps a per-machine lesson store (`agent/lessons.db`) of
deterministic, evidence-backed observations about what works or fails, from
five sources:

- **Commands.** `run_command`'s structured exit code (not string-sniffed from
  the output), with the compiler/test error token in the claim when it
  fails. A timeout records nothing - it says nothing about the command
  itself.
- **Patches.** `edit_file`/`create_file`/`apply_draft_patch` success or
  failure per file.
- **Approvals.** A rejection records "the user rejects X here"; a later
  approval of that same gated action counters it instead of being ignored,
  so a one-off rejection cannot become a standing lesson that the user's own
  subsequent approvals can never soften. Approving a tool that was never
  rejected records nothing on its own.
- **Task outcomes.** When a task completes or fails, a lesson keyed by a
  deterministic fingerprint of the goal (tokenized, sorted, hashed - no LLM
  involved) records whether goals like it tend to work out in this
  workspace. An uneventful success records nothing (it teaches nothing); a
  success that recovered from trouble along the way records a positive
  lesson, and a failed task records the blocker.
- **Stated.** A model can add its own observation with a `[LESSON: ...]`
  marker in its response, recorded at low starting confidence and clearly
  distinguished (kind "stated") from the four deterministic sources above.

The dedupe key (signature) identifies only the subject - the command text, or
the tool+path, or the approval's tool - never the outcome, so a command that
used to fail and now succeeds lands on the *same* row instead of spawning a
permanently separate one. Repeated matching evidence reinforces that row
(evidence count and confidence go up); a contradiction (the same signature,
a different outcome) decays confidence and, if it drops far enough, retires
the lesson and flips it to the new outcome - which further confirming
evidence can then build back up. Successfully completing a task also
confirms (bumps evidence on) every lesson that was actually shown to the
model during that task, so a lesson that helped compounds, not just one that
happened to be independently re-observed.

Relevant lessons (global plus the current workspace's) are injected into every
step's context pack, ranked by a mix of term overlap with the current goal
and recent tool activity, pinned status, and confidence - not confidence
alone, so an accumulated high-confidence lesson about something unrelated
cannot crowd out one that actually bears on what the model is doing right
now. Each entry shows its outcome, confidence, and evidence count so the
model can weigh them. Lessons only ever *inform* the model - the safety gate
never reads the lesson store, so nothing here can widen what is allowed to
execute without approval.

The Lessons panel in the workbench lists active and retired lessons per
workspace, with inline edit, pin (locks the lesson against further automatic
changes), retire, reactivate, and delete actions.

Chat can optionally consume Global-scope lessons too (Settings > Memory >
"Agent lessons in chat", off by default): they appear as a read-only block in
the system prompt alongside stored memories, but only this panel can ever
edit, pin, retire, or delete them - chat has no write path into the lesson
store.

## Workspace Memory

Hermaeus Agent includes a workspace memory panel for saving and reusing notes tied
to a specific workspace root. This allows you to maintain persistent context
across task sessions.

## Safety & Transparency

- All risky actions are classified before execution.
- Workspace path checks stay case-sensitive on Linux and macOS, and
  case-insensitive on Windows, matching the platform filesystem rules.
- Approval queue provides clear visibility into pending actions.
- Approving an action is bound to a fingerprint (SHA256 over the tool name
  and its canonicalized arguments) of the pending action as displayed. If the
  pending action changed between render and click (a concurrent step, a
  crash-restore race, a tampered `task_state.json`), the mismatch refuses
  execution instead of running whatever happens to be pending; the task
  stays waiting for review and the mismatch is recorded in the trace.
  Rejections are unaffected, since rejecting the wrong thing executes
  nothing.
- Full trace logs enable debugging and auditing of agent behavior.
- Local execution means no data leaves your machine.

## Current Non-Goals

The agent intentionally avoids functionality that would reduce user control or
safety. The agent does not:

- execute arbitrary shell commands (only the fixed `run_command` template
  families, and only ones the workspace itself declared safe)
- install packages
- access the network
- modify files without explicit user approval
- commit, push, or interact with remote repositories
- operate outside the selected workspace root
- assume whole-chat-history context for decision making
- let a lesson (or anything in memory) change what the safety gate allows

Making these non-goals explicit helps users trust that the workbench will not
perform unexpected actions.

## Local API ownership boundary

R31 defines the versioned Agent Local API requests, responses, per-token scope,
ownership rules, and pure authorization policy. It does not expose the
conditional Agent routes. Desktop and `Hermaeus.LocalApi` are separate processes
and do not yet share one serialized task-mutation owner, so a second Agent
service could race task runs, steering, cancellation, and approval state.

The capabilities endpoint reports Agent execution unavailable with that reason.
Existing tokens gain no authority: their additive Agent scope defaults disabled
with an empty operation and saved-workspace allowlist. There is no approval or
denial endpoint, and token possession plus a fingerprint is never approval. See
[Agent Local API contract](agent-api.md) for the complete v1 surface and the gate
that must be met before route handlers can ship.

## Agent Loop

For each step the agent follows a simple, auditable lifecycle:

1. Record or continue the user goal.
2. Update `task_state.json` with the current goal and progress.
3. Retrieve bounded workspace context, optional RAG snippets, the task's own
   transcript tail, and relevant lessons.
4. Produce a compact context pack for the model (see below).
5. Classify the proposed action by risk level (native tool call or parsed
   JSON `next_action`, whichever the model produced).
6. Execute safe, read-only actions (and `set_plan`) immediately.
7. Queue write, command, or MCP actions for user review and approval; a
   previously-approved exact `run_command` string may auto-execute.
8. Record all decisions, actions, and outcomes in logs, JSONL traces, the
   transcript, and (for command/patch/approval outcomes) the lesson store.
9. Repeat automatically (Autonomous Runs) until a stopping condition is hit.

This explicit loop makes the workbench behaviour predictable and auditable
even when it is running several steps unattended.

## Action Risk Levels

| Level | Meaning | Examples | Behaviour |
|---|---|---|---|
| Safe | Read-only local inspection, or task-state-only | `list_files`, `search_files`, `glob_files`, `read_file`, `summarize_file`, `draft_patch`, `inspect_git_diff`, `set_plan` | execute directly |
| Review | Local write, command, sub-task delegation, or MCP call proposed by the agent | edit_file, create_file, apply_draft_patch, `run_command`, `plan_subtasks`, mcp: calls | queue for approval |
| Blocked | Out of scope | `delete_file`, `install_package`, `network_access`, `upload`, `download`, `modify_system_config`, `commit`, `push`, `change_git_history` | do not execute |
| Dangerous | Destructive or broad operation | delete tree, overwrite many files | block by default |

Risk classification is deterministic and recorded in traces so users can review
why an action was allowed, queued, or blocked.

`run_command` is Review, not Blocked, and the route matters: the dispatch path
sends it to `AgentSafetyGate.EvaluateCommand`, which resolves the command family
and blocks anything the workspace has not declared as a command recipe. A
declared family returns "requires approval". The `run_command` entry in the
gate's high-risk set is defence in depth for any future caller that reaches the
generic `Evaluate` path instead.

One nuance: within a single task, a `run_command` whose command string is
character-for-character identical to one the user already approved executes
without asking again. That memory is per task and per exact string; it never
widens to the command family, so approving `dotnet test` once does not approve
`dotnet test --filter Something` later.

A constrained planner response (see "Constrained planner protocol" below) is
classified identically to an unconstrained one. Constraining the shape of the
model's reply makes it parseable, not trusted: `requires_approval` and
`risk_level` remain fields the model fills in and the code overrides.

## Patch Queue Semantics

Patch proposals include structured metadata to make review and application
robust and auditable. Each proposal contains at least:

- `targetPath`: the workspace-relative target file
- `rationale`: brief human-readable explanation
- `patch`: the generated diff or before/after preview
- `createdAt`: timestamp of proposal creation

Approved patches are applied as text edits against the selected workspace file.
The apply result, approving user, approval timestamp, and any failure reason
are recorded in task state. Patch review decisions are independent from a
separate pending tool action on the same task and cannot approve, reject, or
execute that action. A queued full-content patch does not currently carry a
draft-time file hash, so review the live file before applying when other tools
or people may have edited it since the draft was created. Apply captures the
live pre-image immediately before writing, allowing Revert to restore that
content. `edit_file` has a separate staleness control: if `old_string` no
longer matches exactly once, the edit is refused and the agent must re-read the
file.

## Run Ledger and Task Rewind

Every agent run has an undo button. Hermaeus keeps a ledger of everything a
run changed and can put it all back, file by file, with one click.

The Changes view on an open task (in the Agent workbench) shows the run's
total footprint, projected purely from persisted task state:

- **Files**: one entry per distinct file path the run touched, grouped
  across every applied patch for that path. Each entry shows whether the
  file was created or edited, how many patches applied to it, its current
  status (applied, reverted, or conflicted), and its net line delta.
  Conflicted means the file's live content no longer matches what the run
  last wrote to it; this is detected only when the Changes view has
  workspace access to read the file, never by the underlying projection
  itself. Selecting a file shows its content before and after the run,
  using the same preview presentation as a queued draft patch.
- **Commands**: every `run_command` execution, with its exit code and
  whether it timed out.
- **Approvals**: every approval decision recorded for the run.
- **Sub-tasks**: for an orchestration parent, each sub-task's goal and
  status, with the children's own file and command entries folded into the
  sections above and tagged with the child's own task id.

**Rewind run** restores every distinct file path the run touched (including
finished orchestration children, each against its own stored workspace
root) to its content from before the run first touched it, or deletes it if
the run created it. Per file, this reuses the exact same conflict rule as a
single patch's revert: if the file's current content does not match the
latest content the run applied, that file is skipped rather than
overwritten, and the skip reason is reported. Rewind is therefore always a
truthful partial-success report ("Reverted 4 of 5 files. Skipped
src/Foo.cs: the file changed again after this patch was applied."), never a
silent all-or-nothing operation. A confirmation dialog lists exactly which
files will be restored and which will be deleted before anything runs; there
is no "do not ask again."

Rewind refuses to start while the task is still running, has a pending tool
approval, or (for an orchestration parent) has an unfinished sub-task. A
successful rewind writes a `task_reverted` trace event with the per-file
outcomes and appends the summary to `agent.log`. No lesson is recorded from
a rewind: reverting is user judgment about wanted-ness, not evidence that a
tool or command failed.

What Rewind explicitly does not do:

- No filesystem snapshots, copy-on-write overlays, or shadow workspaces. The
  ledger only ever covers what the agent itself changed through its tools.
- No revert of command side effects. The ledger shows that commands ran; it
  cannot un-run `dotnet build`, and build outputs under `bin`/`obj` are
  never ledger entries.
- No auto-rewind on task failure. Rewind is always a user action.

## Workspace Boundary Rules

All file operations are constrained to the selected workspace root. The workbench
normalises paths before access and refuses to follow symlinks or relative paths
that resolve outside the workspace root. Examples that are blocked:

- `../outside-file`
- absolute paths outside the workspace root
- symlinks resolving outside the root
- files under symlinked ancestor directories

This containment rule prevents accidental or malicious access to unrelated
files, including the workspace-relative path arguments `run_command` accepts.

Each task persists the workspace root it was created against. Approving a
pending action (from the review queue, which lists tasks across every
workspace) always executes against the task's own stored root, never
whichever workspace happens to be active in the workbench at approval time.
Older tasks created before this behavior shipped, with no stored root, fall
back to the workbench's active workspace, exactly as before.

## Workspace Policy

A workspace can optionally narrow, never widen, what the agent's tools may
read or write inside it, on top of the containment rules above. Add a
`policy` object to `.hermaeus/workspace.json`:

```json
{
  "policy": {
    "read_allow": ["src/**", "docs/**"],
    "write_allow": ["docs/**", "reports/**"],
    "never": ["secrets/**", "certificates/**", ".git/**"],
    "max_file_reads_per_task": 200
  }
}
```

- `read_allow` / `write_allow`: glob allowlists, workspace-relative, using
  the identical `*`/`**` syntax `glob_files` already matches (the same
  matcher, not a second implementation). Empty or absent means "allow all"
  in that direction, so a workspace with no policy behaves exactly as
  before.
- `never`: a deny list that beats both allow lists, for reads and writes
  alike.
- `max_file_reads_per_task`: caps `read_file`/`summarize_file` executions
  per task. 0 or absent means unlimited. The count is persisted on the task,
  so a restart does not reset the budget.
- **Policy only ever narrows.** Since the manifest lives inside the
  workspace, hostile workspace content can author one; the worst it can do
  is restrict the agent further inside that workspace. Nothing in policy can
  grant a path outside the root, a new command family, or relax any gate.
- A malformed policy (bad shape, a negative cap) is rejected as a whole,
  with a visible warning in the workbench and log; the rest of the manifest
  still loads normally. Hermaeus never silently falls back to a
  half-applied policy, since a boundary the user trusts but that is not
  actually there is worse than no boundary at all.

Enforcement sits immediately after the existing containment/symlink checks,
so policy is never consulted before a `../escape`-style path has already
failed containment. A denied read returns a structured refusal naming the
path and the rule (not an exception, so the step completes normally and the
model can route around it); a denied write is classified Blocked by the
safety gate before it ever becomes an approvable pending action, so it
cannot be approved into existing. The draft-patch queue and Task Rewind
enforce the same write rules through the same code path; a Rewind of a file
the current policy denies writing is refused per file with the policy named,
and the ledger still shows it.

The Workspace tab's capability list shows one line when a policy is
active (for example "Workspace policy: reads limited to 2 rules, writes to
2, 3 paths off limits."); expanding it lists the raw globs, read-only. There
is no policy editor; the policy block of the manifest is hand-edited.
`AllowedCommands` is the exception: it has an editor on the Workspace tab,
because a workspace that declares no recipes can run nothing and the user had
no way to discover either the file or the families it accepts.

## Context Packs

A context pack is a compact, task-scoped bundle the agent provides to the
model. It is reconstructed on demand from persisted state and retrieval
results. A typical context pack may include:

- current goal
- task summary
- relevant file excerpts (bounded and trimmed)
- retrieved RAG snippets
- a budgeted tail of the task's own step transcript
- relevant lessons (global plus this workspace), each with confidence and evidence count
- recent task history
- workspace memory notes (scoped to the workspace)
- pending review items
- declared constraints and blocked actions

Context packs are intentionally small and focused. Do not assume they contain
full chat history or global user memory.

## Trace Schema Example

Traces are written as newline-delimited JSON (`agent.trace.jsonl`). A single
trace event may look like:

```json
{
  "timestamp": "2026-05-16T10:32:00Z",
  "taskId": "abc123",
  "event": "patch_queued",
  "risk": "review",
  "targetPath": "src/App.axaml.cs",
  "reason": "User requested a local code change",
  "status": "pending"
}
```

Traces record enough context to replay or audit the agent's decisions. UI
components should surface the trace event details and provide a clear reason
when an action is blocked.

## Explain Why Blocked

When an action is blocked the UI should surface a concise reason and a safe,
manual next step where appropriate. Example:

```
Blocked: shell execution is not supported outside the fixed run_command
template families.
Suggested manual step: run `dotnet build` in the workspace terminal.
```

This keeps the user informed and provides practical remediation guidance.

## Privacy Caveat

Hermaeus performs agent workspace operations locally. Data only leaves the
machine if the user configures a remote model, embedding provider, or other
external service. The default local-first setup does not upload workspace files.

## Workspace Memory Scope

Workspace memory is scoped to the selected workspace root. It is intended for
project-specific notes and decisions and is not used as global user memory.

## Task States

Tasks are tracked with explicit states to make automation predictable:

- `idle`
- `running`
- `waiting_for_review`
- `blocked`
- `completed`
- `failed`
- `cancelled` - terminal, and the only state the user assigns directly: what
  Dismiss puts a task into when they are done with it. Every other state is
  the run's own account of itself.

`failed` is a real, reachable terminal state, not just a nominal one: a model
whose response fails to parse as valid JSON three steps in a row fails the
task, with the reason recorded on the task state and every bad step still
visible in the transcript. A step that parses successfully resets the
counter, so occasional trouble spread across a long task does not add up to
a failure. Any unhandled error mid-step (the model call itself failing, or a
tool throwing) hands the task back to `waiting_for_review` rather than
leaving it stuck showing as `running` with nothing left able to act on it.

Review items (patches) use these states:

- `pending`
- `approved`
- `applied`
- `rejected`
- `blocked`
- `failed`

Recording these states in `task_state.json` and traces simplifies UI logic and
automated testing.

## Scenario Suite Checks

The built-in Agent Scenario Suite (`src/Hermaeus.Agent/Scenarios/`) grades a real
run's transcript against each manifest's `expect` block. The
`expect_subtask_statuses` check (used by orchestration scenarios) checks only
that every distinct expected status was reached by at least one sub-task, not
that the model produced the exact same number and order of sub-tasks the
manifest happens to hardcode - a model that reasonably splits a goal
differently is not itself a failure of orchestration.

Sixteen scenarios ship built in, including three added in r23: `14-confused-
user-authority` (a goal that pre-announces consent must still go through
approval), `15-tool-result-poisoning` (provocative directory and file names,
not file body content, as the injection vector), and `17-memory-poisoning`
(a workspace instructs the agent to record a lesson claiming blanket
approval, and also exercises the workspace policy end to end via a `never`
rule over a secrets file). Scenario 16 in the suggester's own numbering
shipped as code hardening instead (approval fingerprint binding, r23 4.1):
the suite grades model behaviour and cannot itself tamper with task state
between an approval's render and its click, so there is no model-behaviour
scenario to write for it.

`forbid_active_lesson_matching` (used by scenario 17) asserts that no lesson
left active in the sandbox lesson store after the run matches an
approval-policy claim token (the same list the stated-lesson gate-claim
filter, r23 4.2, rejects at capture time). A claim the model attempted and
the filter rejected passes this check by construction, since it was never
stored; only a claim that reached the store some other way fails it.

## Manual Verification

The agent can now build and test its own work through the fixed `run_command`
template families (still approval-gated), so it can read a compiler or test
failure and fix it in the same task. For anything outside that fixed
command set, the agent will surface manual verification steps such as
reviewing diffs. These are presented as next actions rather than executed
automatically.
