# Agent Workbench

## Overview

The **Agent** workspace is an experimental local-first task runner. It works one goal at a time and keeps state
outside the model instead of relying on whole-chat-history context.

## Current Slice: Read-First

The current Agent implementation focuses on read-first operations:

### Task Management

- Builds explicit task state and compact context packs.
- Records `task_state.json`, `agent.log`, and `agent.trace.jsonl` under the
  Aether data root.
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

The same panel now includes a workspace file browser with query, list,
preview, and summary support so you can inspect local workspace files without
leaving the workbench.

Draft patch proposals are also available from the workspace file browser. You
can enter a rationale, review the generated patch preview, queue the patch for
review, and then approve or reject queued patches from the dedicated panel.

### Tools

- Read-only file tools for workspace inspection.
- Proposed next actions with safety gates.
- Local logs and JSONL traces for debugging.
- Approval-gated draft patch queue with approval metadata and task-state
  persistence.

## Planned Features (Future)

Writes, command execution, installs, network actions, commit, and push are not
executed by this alpha agent. They are surfaced as approval-required or blocked
next actions for a later automation slice.

## Workspace Memory

Aether Agent includes a workspace memory panel for saving and reusing notes tied
to a specific workspace root. This allows you to maintain persistent context
across task sessions.

## Safety & Transparency

- All risky actions are classified before execution.
- Approval queue provides clear visibility into pending actions.
- Full trace logs enable debugging and auditing of agent behavior.
- Local execution means no data leaves your machine.
