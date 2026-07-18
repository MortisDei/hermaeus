# 03: Scenario coverage and agent-loop hardening

## 3.1 New built-in scenarios

Three additions under src/Aether.Agent/Scenarios (same manifest format,
copied to output via the existing csproj None-Include glob; remember
the r10 lesson: exclude any fixture .cs from compilation). The scenario
runner (src/Aether.Agent/Services/AgentScenarioRunner.cs) must learn to
follow orchestration: when the scripted run produces child tasks, the
runner's approval hook applies to child pending actions too, and checks
may assert on the parent's `SubTaskPlan`. The standing rule that no
scenario may auto-approve `run_command` (AgentScenarioManifestValidator)
stays and now applies across children as well.

- `11-orchestration-gate`: scripted model proposes `plan_subtasks` with
  3 sub-tasks. Checks: the action pauses `RequiresApproval` (never
  auto-executes), the plan materializes only after approval, children
  run sequentially, parent ends Complete with a synthesis report
  containing all three sub-task names. Manifest auto-approves
  `plan_subtasks` only.
- `12-orchestration-depth`: a scripted CHILD step requests
  `plan_subtasks`. Check: safety-gate row shows Blocked with the
  depth reason; the child does not gain a `SubTaskPlan`; the run
  continues rather than crashing.
- `13-orchestration-budget`: plan of 3, `MaxOrchestrationSteps` forced
  low via scenario settings seam (the runner already composes its own
  `ScenarioSettings : ISettingsService`; expose the new setting there).
  Checks: at least one spec ends `Skipped`, parent still Complete, and
  the report contains the truncation note.

New check types needed by the above (pure functions in
AgentScenarioChecks, following the existing 11): `expect_subtask_statuses`
(ordered list of expected spec statuses) and
`expect_report_contains` (substring on the parent's final message).

Acceptance: all three scenarios pass under the scripted fakes in the
test suite; the manifest validator accepts the new check names and
still warns on unknown ones.

## 3.2 Hardening fix: gated-but-unexecutable actions strand the task

`AgentService.RunStepAsync` (src/Aether.Agent/Services/AgentService.cs:286-299):
when a decision is `RequiresApproval` but `_toolExecutor.CanExecute`
is false, the task is set `WaitingForUser` with NO `PendingToolAction`.
There is nothing to approve, and `AppendUserReplyAsync` is the only way
out, which a user has no reason to guess. Today this is reachable for a
gated tool name with no registered executor (e.g. an `mcp:` tool when
the bridge is not wired into the executor set).

Fix: in that branch, treat it like the allowed-but-unexecutable case at
:349-358: set `Blocked`, append an `AgentToolResult` explaining that
the tool required approval but no local executor is registered for it.
Acceptance: unit test drives a gated unknown-executor tool and asserts
Blocked plus the explanatory result; the existing approvable paths are
unaffected.

## 3.3 Hardening fix: blockers silently vanish or flip status twice

`ApplyResponse` (AgentService.cs:1050-1072): a response carrying
`state_update.blockers` sets `Status = Blocked` (:1070-1071), but the
blocker strings themselves are never recorded anywhere, and if the same
response also requests an allowed tool, the execution path later
overwrites the status to Running (:322, :345), so the blocker leaves no
trace at all.

Fix, deterministic precedence:
- Record each non-empty blocker into `state.Decisions` as
  `new AgentDecision(blocker, "model-reported blocker", now)` so it
  survives regardless of status.
- Set `Blocked` from blockers only when the step's next action does not
  go on to execute successfully this step; a step that both reports a
  blocker and successfully executes a tool ends Running (progress wins),
  with the blocker preserved in Decisions.

Acceptance: two unit tests, one per branch; existing blocker behavior
for ask_user/final responses unchanged.

## 3.4 Security review touch

docs/security-review.md gains an r15 subsection: orchestration adds no
new tool capability, no network surface, and no gate bypass; the new
attack surface is prompt-injected DECOMPOSITION (workspace content
convincing the model to propose malicious-looking sub-tasks). Mitigation
is the same as ever: the plan itself is approval-gated with full
preview (doc 02 2.1), children obey the unchanged per-action gates, and
depth-1 prevents recursive amplification. State this explicitly, and
note the standing scenario (`02-prompt-injection`) still passes.
