# 04: Roadmap

## Ship shape

Ships as **0.20.0-alpha** (feature round, minor bump per the r5
precedent). Version bump lives solely in `Directory.Build.props`.

## Sequencing

1. Doc 01 1.1 data model + store round-trip tests (everything else
   depends on the fields existing).
2. Doc 03 3.2 and 3.3 loop hardening (small, independent, and the
   orchestration loop should be built on the fixed semantics, not
   before them).
3. Doc 01 1.2 gate + tool declaration, 1.3 profiles.
4. Doc 01 1.4 sequential execution, 1.5 budgets, 1.6 synthesis.
5. Doc 02 UI (preview, strip, child approvals, report).
6. Doc 03 3.1 scenarios (they exercise the finished flow end to end).
7. Docs + CHANGELOG + security-review subsection, then full build and
   test per AGENTS.md.

## Test expectations

Roughly 30 to 40 new tests from the current 834 (0.19.0-alpha),
scripted-LLM fakes throughout, zero live model. Runner:
`dotnet test src/Aether.Tests/Aether.Tests.csproj`. Zero-warning build
is enforced; tests stay sequential.

## Explicit rejections (do not implement, do not re-propose)

- **No parallel child execution.** One local model, one GPU; parallel
  children would fight for the same server slots (r14 just set
  `--parallel 1` deliberately). Sequential is the design, not a
  limitation to fix later.
- **No nesting beyond depth 1.** A child proposing sub-tasks is
  blocked in code. Recursive delegation multiplies token cost and
  approval fatigue with no added capability.
- **No user-editable specialist prompts this round.** Free-text
  profiles are a prompt-injection surface persisted outside the
  transcript; the fixed catalog ships first, editability can be argued
  in a future round with its own gating story.
- **No per-child model selection.** Children inherit the parent's
  `AgentWorkspaceOptions` including ModelId. Mixing models mid-run
  invites the r12 class of selection/refresh bugs for marginal gain.
- **No approval inheritance of any kind.** Not parent-to-child, not
  child-to-sibling, not `RememberedCommandApprovals` sharing. Every
  gated action is approved where it happens.
- **No background or detached orchestration.** The run lives in the
  workbench session like every agent run today; a crash-resumable
  parent (specs persist in task_state.json) is enough.
- **No LLM-judged synthesis quality scoring.** The r7 no-LLM-judge
  precedent stands; scenario checks stay deterministic.
