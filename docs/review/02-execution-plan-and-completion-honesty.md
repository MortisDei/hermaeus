# 02. Execution plan checkpoint and completion honesty

Adopts, in scoped form, two of the external feature suggestions ("Execution
Plan" and "Confidence-Based Completion") plus the defensible slice of a
third ("Evidence Explorer"). What was rejected and why is consolidated in
doc 06.

## 2.1 Plan-approval checkpoint (opt-in)

New setting `AgentSettings.RequirePlanApproval` (bool, default `false`),
exposed with the other Agent settings. Off by default because it adds a
click to every run; the reviewer who asked for it is right that some users
trust plans more than opaque momentum, so it is their choice.

Behaviour when enabled, for a fresh task's first autonomous run only:

- The run proceeds normally until the model's first successful `set_plan`.
  Instead of continuing to the next step, the task pauses in
  `waiting_for_review` with a note that the plan is ready for review, and
  the workbench shows the plan checklist with Continue and Stop available.
  Continuing resumes the autonomous run via the existing continue path
  (`ContinueTaskAsync` semantics); no new task state is added.
- If the model proposes a gated action before ever calling `set_plan`, the
  existing approval flow already provides the checkpoint; do not add a
  second pause on top of it.
- The checkpoint fires at most once per task (track a bool on
  `AgentTaskState`, persisted, so a restart does not re-arm it).
- Sub-tasks never re-check: `plan_subtasks` approval already shows the
  user the full sub-task plan before anything runs
  (`AgentApprovalPreview.DescribePlanSubtasks`, AgentApprovalPreview.cs:51).

Implementation note: this is a pause, not a new approval object. Nothing
about the safety gate changes; a plan is `set_plan` output the user reads
before letting the loop continue.

## 2.2 Visible plan revisions

`set_plan` currently replaces the checklist silently (it is a Safe,
non-gated tool and stays that way). Make revisions visible after the fact:

- When `set_plan` replaces a non-empty existing plan, append a log and
  transcript line: `plan revised: 4 items -> 6 items` (counts, not a diff).
- The workbench plan panel shows a small "revised at step N" annotation on
  the current plan when at least one revision happened.

This is the "plan revision becomes visible" half of the reviewer's request
without turning plan management into a gated ceremony.

## 2.3 Completed with reservations

Adopt the honest-completion idea; reject numeric confidence scores (a
model-invented "78%" is theatre, and doc 06 records that rejection).

- The final-answer protocol gains an optional `reservations` field: a list
  of short strings, each one thing the model could not verify or finish
  ("Could not find deployment documentation anywhere in the workspace").
  Both the JSON protocol and the native tool-calling path must carry it.
- A task completing with a non-empty list displays status text "Completed
  with reservations" in the workbench summary strip and recent-tasks list
  (status stays `completed`; this is presentation plus persisted metadata
  on `AgentTaskState`, not a new state machine value).
- The reservations render as their own list under the final answer, and
  orchestration synthesis carries child reservations into `report.md`
  under a "Reservations" heading.
- An empty or absent field means nothing is shown; do not nag the model
  into inventing reservations, and do not reward it for boilerplate ones.
  The system prompt mentions the field once, neutrally: reservations are
  for things it looked for and could not verify, not for hedging.

## 2.4 Surface the context receipt ("what the model saw")

`AgentContextReceiptBuilder` already computes, per step, exactly which
memory, RAG, file, instruction, lesson, and sub-task items were injected
and their token costs (AgentContextReceiptBuilder.cs:17). Nothing shows it.

- Add a collapsed-by-default "Context receipt" expander to the workbench
  showing the latest step's sections: label, item count, token estimate,
  and item identifiers, exactly as built. Read-only, no new persistence.
- This is deliberately the whole Evidence Explorer this round: evidence of
  what went in, not a model-authored narrative of what mattered. The trace
  (`agent.trace.jsonl`) remains the audit tool for step-by-step archaeology.

## 2.5 Docs

`docs/agent.md`: document 2.1 (setting, exact pause semantics), 2.2, 2.3
(field is optional, model-provided, never required), and 2.4.
`docs/features.md` gets one line each for the plan checkpoint and
reservations. Settings docs mention `RequirePlanApproval` under Agent.
