# Review round 15: Sub-task orchestration

Audience: the implementing agent. Read every doc before writing code.
Prior rounds live in `docs/review/archived/`; check their roadmaps for
explicit rejections before re-proposing anything.

## Theme

Give the Agent a way to take one large, vague goal ("get this project
ready for review") and execute it as a sequence of small, focused
sub-tasks, each run through the exact same loop, safety gate, and
approval flow that exists today, then synthesize one consolidated
report at the end.

## Why this shape and not "multi-agent"

The idea started as a multi-agent "Specialist Agent Router" proposal.
The kernel is right; the framing is wrong for a local-first app:

- There is one local model on one GPU. "Multiple agents" cannot run in
  parallel and would not be different minds anyway; a 8B model
  role-playing four specialists is the same model with a different hat.
- What actually fails today on a broad goal is context, not expertise:
  a vague goal burns the transcript budget (`AgentSettings.TranscriptTokenBudget`,
  default 12000) long before it is done, and the model loses the thread.
  Decomposition into sub-tasks gives each one a fresh, small, focused
  context pack. That is the real win, and it is model-size-agnostic.
- Aether already has every primitive: durable `AgentTaskState`, a
  deterministic safety gate, per-action approvals, lessons, transcript
  budgeting, and a scenario suite. This round composes them; it does
  not build a new agent runtime.

So: sequential execution, depth limited to one level, every child task
is an ordinary agent task with an ordinary approval queue, and the
decomposition itself is a gated action the user approves before any
child runs. No new autonomy is introduced anywhere.

## Documents

| Doc | Contents |
| --- | --- |
| `01-subtask-orchestration.md` | Data model, `plan_subtasks` tool, gating, sequential child execution, budgets, synthesis |
| `02-orchestration-ui.md` | Workbench presentation: sub-task strip, child approvals, consolidated report |
| `03-scenarios-and-hardening.md` | New built-in scenarios covering orchestration, plus two agent-loop fixes found while auditing for this round |
| `04-roadmap.md` | Ship shape, sequencing, test expectations, explicit rejections |

## Non-negotiables (repeated from AGENTS.md because they bind everything here)

- Risk classification stays deterministic. Nothing in this round may
  widen what a child task is allowed to do, and no approval ever
  propagates from parent to child or between children.
- `task_state.json` remains the source of truth; every new field is
  JSON-additive so pre-r15 tasks load unchanged.
- Zero-warning build, no em dashes anywhere, docs updated truthfully.
