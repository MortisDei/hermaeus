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
  `transcript.jsonl` under the Aether data root.
- Maintains `agent/task_index.db` as a SQLite catalog for recent-task and review
  queue lists, with `task_state.json` remaining the source of truth for full
  task state. Initialization reconciles JSON task files back into the index.
- Validates task IDs with a short alphanumeric, dash, and underscore allowlist
  before resolving task directories.
- Shows a review queue for waiting or blocked tasks with approve/reject
  actions for recorded approvals, plus an Open button that loads the queued
  task into the workbench.
- Shows a Recent Tasks list (status chip, goal, relative time; sub-task
  children indented with a tag) so a completed task's report, a failed
  task's blockers, or an orphaned task is reachable after a restart without
  going through the review queue. Opening a child directly also shows its
  parent's goal.

### Context & Retrieval

- Searches and reads bounded text files under a selected workspace root, with
  glob matching, optional regex search, and line-ranged reads for large files.
- Can include relevant context from an optional RAG dataset.
- Replays a budgeted tail of the task's own step transcript (see below).
- Surfaces relevant lessons from the self-learning store (see below).
- Classifies risky actions before execution.

The agent panel surfaces a compact summary strip with current task state, step
count, goal, summary, recent task history, review queue counts, workspace
memory counts, and retrieved context counts so the workbench is easy to scan
at a glance.

A compact capability disclosure sits under the summary strip so the current
scope is explicit: read-only tools run locally, writes and commands are
approval-gated, and shell, network, and remote-control actions remain out of
scope.

The same panel includes a workspace file browser with query, list, preview,
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

A task waiting on `ask_user` shows a reply box in the workbench; answering it
appends the reply to the transcript and resumes the run, the same
approve-and-continue shape as a gated-action approval. A reply is never
accepted while a tool approval is also pending on the same task - those are
answered separately, on their own explicit approve/reject path.

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

Explicit rejections for this round: no parallel child execution (one local
model, one GPU), no nesting beyond depth 1, no user-editable specialist
profiles yet (the fixed catalog - `general`, `correctness`, `security`,
`tests`, `performance`, `docs` - ships first), no per-child model selection
(children inherit the parent's model and workspace), no approval inheritance
of any kind, and no background/detached orchestration (the run lives in the
workbench session like any agent run; a crash-resumable parent is enough).

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
  families (`dotnet build`/`dotnet test` with an optional project path,
  `npm test`, `npm run <script>` where the script must already exist in the
  workspace's own `package.json`, `cargo build`, `cargo test`, `pytest` with
  an optional path), and only when the workspace itself declared that family
  safe in `.aether/workspace.json`. Optional path arguments go through the
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

Aether Agent includes a workspace memory panel for saving and reusing notes tied
to a specific workspace root. This allows you to maintain persistent context
across task sessions.

## Safety & Transparency

- All risky actions are classified before execution.
- Workspace path checks stay case-sensitive on Linux and macOS, and
  case-insensitive on Windows, matching the platform filesystem rules.
- Approval queue provides clear visibility into pending actions.
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
| Safe | Read-only local inspection, or task-state-only | list files, search, glob, read, `set_plan` | execute directly |
| Review | Local write, command, or MCP call proposed by the agent | edit_file, create_file, apply_draft_patch, run_command, mcp: calls | queue for approval |
| Blocked | Out of scope | shell, network, install, commit, push | do not execute |
| Dangerous | Destructive or broad operation | delete tree, overwrite many files | block by default |

Risk classification is deterministic and recorded in traces so users can review
why an action was allowed, queued, or blocked.

## Patch Queue Semantics

Patch proposals include structured metadata to make review and application
robust and auditable. Each proposal contains at least:

- `targetPath`: the workspace-relative target file
- `rationale`: brief human-readable explanation
- `patch`: the generated diff or before/after preview
- `risk`: the classified risk level
- `sourceContext`: snippets used to generate the patch
- `createdAt`: timestamp of proposal creation
- `baseHash`: hash of the target file at proposal time (for stale-file protection)

Approved patches are applied as text edits against the selected workspace file.
If the file's current hash does not match `baseHash` the patch is blocked and
must be refreshed or regenerated by the agent before it can be applied. The
apply result, approving user, approval timestamp, and any failure reason are
recorded in the task state and trace. `edit_file`'s own staleness protection is
its unique-match requirement: if `old_string` no longer matches (0 or more than
1 occurrence), the edit is refused and the agent must re-read the file.

### Stale-file protection (recommended)

When a patch is drafted the agent stores the file hash (for example, SHA-256):

```
{
  "targetPath": "src/Foo.cs",
  "baseHash": "sha256:...",
  "createdAt": "2026-05-16T10:00:00Z",
  "patch": "..."
}
```

Before applying a patch the agent compares the stored `baseHash` with the
current file hash. If they differ the patch is blocked and the UI prompts the
user to review or regenerate the patch.

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
Pre-r16 tasks with no stored root fall back to the workbench's active
workspace, exactly as before.

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

Aether performs agent workspace operations locally. Data only leaves the
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
- `cancelled`

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

The built-in Agent Scenario Suite (`src/Aether.Agent/Scenarios/`) grades a real
run's transcript against each manifest's `expect` block. The
`expect_subtask_statuses` check (used by orchestration scenarios) checks only
that every distinct expected status was reached by at least one sub-task, not
that the model produced the exact same number and order of sub-tasks the
manifest happens to hardcode - a model that reasonably splits a goal
differently is not itself a failure of orchestration.

## Manual Verification

The agent can now build and test its own work through the fixed `run_command`
template families (still approval-gated), so it can read a compiler or test
failure and fix it in the same task. For anything outside that fixed
command set, the agent will surface manual verification steps such as
reviewing diffs. These are presented as next actions rather than executed
automatically.
