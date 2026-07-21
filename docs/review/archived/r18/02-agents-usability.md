# 02 - Agent Scenarios correctness and the Agents view layout

## 2.1 Agent Scenarios: 3 failures in a live run, "logic related"

The Scenarios suite is not a fixed-input unit test - it's a
model-graded behavioral check. `AgentScenarioRunner` (`src/Aether.Agent/Services/AgentScenarioRunner.cs`)
drives a real `AgentService` against the user's selected model for
each of the 13 built-in manifests loaded by
`AgentScenarioStore.LoadAllAsync` (`AgentScenarioStore.cs:35-110`,
reading `src/Aether.Agent/Scenarios/{01..13}-*/scenario.json`), and
`AgentScenarioChecks.Evaluate` (`AgentScenarioChecks.cs:16-70`) grades
the transcript. A model genuinely behaving differently than expected
is a legitimate failure mode here, not necessarily a bug - but two
things in the runner/checks can produce a failure that has nothing to
do with whether the agent did something reasonable, and both should be
tightened before trusting the pass/fail count:

- `CheckExpectSubtaskStatuses` (`AgentScenarioChecks.cs:72-83`) requires
  an exact-length, exact-order match between the model's
  `SubTaskPlan` and the manifest's `expect_subtask_statuses`. The three
  orchestration scenarios (`11-orchestration-gate`,
  `12-orchestration-depth`, `13-orchestration-budget`) all depend on
  this. If the model splits work into a different number of subtasks
  than the manifest hardcodes - a reasonable thing for a model to do -
  the check fails even though orchestration itself worked. Loosen this
  to check the *relevant* statuses (e.g. "at least one subtask reached
  `Failed`/`Blocked` for the budget-exhaustion case") rather than a
  positional array diff, or treat an unexpected-but-valid plan shape as
  inconclusive rather than fail.
- The approval loop (`AgentScenarioRunner.cs:114-151`) only
  auto-approves tools the manifest explicitly lists in `auto_approve`;
  only `08-approval-flow/scenario.json` sets that. Any other scenario
  where the model calls a tool the harness didn't anticipate leaves the
  task stuck in `WaitingForUser` and the scenario times out or reports
  a status mismatch that has nothing to do with the behavior under
  test. Before assuming a scenario is "logic related" broken, check
  whether it actually reached a graded end state or stalled waiting for
  an approval the harness never grants.
- Two checks use exact-substring matching on model wording
  (`CheckAnswerMustMentionAny`, `AgentScenarioChecks.cs:202-212`):
  `09-missing-docs` requires the literal phrase "not documented in this
  workspace"; `13-orchestration-budget` requires an honest
  "budget exhausted"-style phrase. A model that correctly identifies
  the gap but phrases it differently fails on wording, not logic.

Action: re-run the suite once, capture which 3 scenarios failed and
whether the transcript shows the model behaving correctly-but-differently
vs. actually wrong, then fix the specific check (loosen the subtask
assertion, add missing `auto_approve` entries, or broaden the
mention-any phrase list) rather than guessing. Do not touch scenario
manifests 01-07/10 unless the same failure shows up there too.

## 2.2 Agents view: the response is a truncated 12px label, not a panel

`AgentView.axaml` is one `Grid RowDefinitions="Auto,*"`
(`AgentView.axaml:14`): row 0 is a pinned header `Border`
(`:15-168`) with a 4-column stats grid, row 1 is a `ScrollViewer`
(`:170`) holding the goal/workspace form, workspace profile, files,
retrieved context, task state, patches, recent tasks, review queue,
scenario evals, memory, lessons, and log - in that order, all in one
scroll region.

What the user is calling "a little window at top left of screen,
hard to read" is almost certainly the `CurrentStep` `TextBlock`
(`AgentView.axaml:25-30`): it lives in the pinned header's top row next
to the Run Step/Stop buttons, `FontSize="12"`, `Opacity="0.5"`,
`TextTrimming="CharacterEllipsis"`, no wrapping - a single truncated,
low-contrast line set from `AgentViewModel.cs:1408`
(`CurrentStep = label + result.State.ActiveStep;`). That is the live
"what is the agent doing right now" indicator, and it is the most
prominent thing in the fixed header, but it's built to show a status
phrase, not a response. The actual planned action
(`NextActionPreview`, JSON-serialized at `AgentViewModel.cs:1410`) only
appears in the "Next Action" `Expander` (`AgentView.axaml:562-569`,
`MinHeight="150"`) far down the scroll region, collapsed by default.

Fix, scoped to layout (no new agent behavior):
- Give the agent's current response/output its own panel with real
  width and height - not squeezed into the header's status label - most
  naturally by moving it to the top of the scrollable content (row 1)
  as its own always-expanded card, sized to actually be readable
  (e.g. a `MinHeight` around 200-300px with wrapping and its own
  scrollbar for long output), rather than living inside a collapsed
  `Expander`.
- Keep `CurrentStep` in the header as the short status phrase it's
  suited for, but that's supplementary to the response panel, not a
  replacement for it.
- Re-order the scroll region so the response/next-action panel is
  first (or pinned near the top), ahead of workspace profile/files/task
  state, which are reference panels the user checks less often once a
  run is going.

This is UX layout only - do not change `AgentViewModel`'s state model,
risk gates, or `task_state.json` semantics to do it.

## Acceptance

- Re-running Scenarios after 2.1's fixes: no failures attributable to
  positional-array mismatch, unmet `auto_approve`, or wording-only
  mismatch: remaining failures (if any) are genuine model behavior
  differences, documented as such.
- The Agents view shows the agent's current response/output in a panel
  sized to be read without scrolling to a collapsed expander first.
