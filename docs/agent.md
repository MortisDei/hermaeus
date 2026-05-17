# Agent Workbench

## Overview

The **Agent** workspace is an experimental local-first task runner. It works one goal at a time and keeps state
outside the model instead of relying on whole-chat-history context.

## Current Slice: Local Tool Execution

The current Agent implementation executes a small local tool set with explicit
safety gates:

### Task Management

- Builds explicit task state and compact context packs.
- Records `task_state.json`, `agent.log`, and `agent.trace.jsonl` under the
  Aether data root.
- Maintains `agent/task_index.db` as a SQLite catalog for recent-task and review
  queue lists, with `task_state.json` remaining the source of truth for full
  task state.
- Validates task IDs with a short alphanumeric, dash, and underscore allowlist
  before resolving task directories.
- Shows a review queue for waiting or blocked tasks with approve/reject
  actions for recorded approvals.

### Context & Retrieval

- Searches and reads bounded text files under a selected workspace root.
- Can include relevant context from an optional RAG dataset.
- Classifies risky actions before execution.

The agent panel now also surfaces a compact summary strip with current task
state, goal, summary, recent task history, review queue counts, workspace
memory counts, and retrieved context counts so the workbench is easier to scan
at a glance.

A compact capability disclosure now sits under the summary strip so the current
slice is explicit: read-only tools run locally, patch application is approval-
gated, and shell, network, and remote-control actions remain out of scope.

The same panel now includes a workspace file browser with query, list,
preview, and summary support so you can inspect local workspace files without
leaving the workbench.

Draft patch proposals are also available from the workspace file browser. You
can enter a rationale, review the generated patch preview, queue the patch for
review, and then approve or reject queued patches from the dedicated panel.
Approving a queued patch applies the proposed content to the selected workspace
file immediately and refreshes the preview.
Queued patches now expose explicit pending, applied, rejected, and blocked
states so review decisions are visible at a glance.

### Tools

- Read-only file tools for workspace inspection: `list_files`, `search_files`,
  `read_file`, `summarize_file`, `draft_patch`, and `inspect_git_diff`.
- Approval-gated write tool: `apply_draft_patch`.
- Proposed next actions with safety gates.
- Local logs and JSONL traces for debugging.
- Approval-gated draft patch queue with approval metadata and task-state
  persistence.

## Planned Features (Future)

Shell command execution, installs, network actions, commit, push, upload,
download, and history rewrite actions are not executed by this alpha agent.
They are blocked even if the model asks for them.

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

The alpha agent intentionally avoids functionality that would reduce user
control or safety. The agent does not:

- execute shell commands
- install packages
- access the network
- modify files without explicit user approval
- commit, push, or interact with remote repositories
- operate outside the selected workspace root
- assume whole-chat-history context for decision making

Making these non-goals explicit helps users trust that the workbench will not
perform unexpected actions.

## Agent Loop

For each task the agent follows a simple, auditable lifecycle:

1. Record the user goal.
2. Create or update `task_state.json` with the current goal and progress.
3. Retrieve bounded workspace context and optional RAG snippets.
4. Produce a compact context pack for the model (see below).
5. Classify any proposed actions by risk level.
6. Execute safe, read-only actions immediately.
7. Queue write or risky actions for user review and approval.
8. Record all decisions, actions, and outcomes in logs and JSONL traces.

This explicit loop makes the workbench behaviour predictable and auditable.

## Read-First Contract

Read-first means the agent is permitted to inspect workspace files, summarise
findings, search local context, and draft proposed changes. It must not mutate
workspace files, run commands, access external services, or change repository
state unless that behaviour is explicitly provided by the current slice and the
user approves the change through the review queue.

## Action Risk Levels

| Level | Meaning | Examples | Behaviour |
|---|---|---|---|
| Safe | Read-only local inspection | list files, preview file, search text | execute directly |
| Review | Local write proposed by the agent | patch file, update note | queue for approval |
| Blocked | Out of scope for current slice | shell, network, install, commit, push | do not execute |
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
recorded in the task state and trace.

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
files.

## Context Packs

A context pack is a compact, task-scoped bundle the agent provides to the
model. It is reconstructed on demand from persisted state and retrieval
results. A typical context pack may include:

- current goal
- task summary
- relevant file excerpts (bounded and trimmed)
- retrieved RAG snippets
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
Blocked: shell execution is not supported in the current read-first slice.
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

Review items (patches) use these states:

- `pending`
- `approved`
- `applied`
- `rejected`
- `blocked`
- `failed`

Recording these states in `task_state.json` and traces simplifies UI logic and
automated testing.

## Manual Verification

Because the current slice is read-first, the agent will surface manual
verification steps such as running builds, tests, or reviewing diffs. These are
presented as next actions rather than executed automatically.
