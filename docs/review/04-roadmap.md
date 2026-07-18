# 04: Roadmap

## Ship shape

Ships as **0.21.0-alpha** (feature-and-fix round, minor bump per the
r5 precedent). Version bump lives solely in `Directory.Build.props`
(VersionPrefix/AssemblyVersion/FileVersion).

## Sequencing

Doc 01 lands before doc 03: the recent-tasks list makes direct child
access a first-class flow, so the orchestration reconcile and status
truth fixes must already be in place when it ships.

1. 1.7 (index date parse - tiny, isolated).
2. 1.4 + 1.5 (persisted WorkspaceRoot, approval execution against it;
   the null-options hole closes as a consequence).
3. 1.1 (orchestration reconcile + review-queue ParentTaskId mapping).
4. 1.2 + 1.3 (manual-step routing; plan_subtasks re-proposal guard).
5. 1.6 (parent pause status truth + VM resume adjustment).
6. 2.1 (archived-rows filter) then 2.3 (expiry) then 2.5 (decay in
   injection scoring) - small, independent store/selection fixes.
7. 2.2 (wire the [MEMORY: ...] save path) - the largest memory item,
   built on the now-correct recall semantics.
8. 2.4 (embedding dim column, mismatch surfacing, re-embed action).
9. 3.2, 3.3, 3.4, 3.6 (small UI truth fixes, each independently
   shippable).
10. 3.1 (recent-tasks list + review-queue Open) - the capstone, after
    the agent-side truths it displays are fixed.
11. 3.5 (converter/wheel hygiene) last; pure mechanical, easiest to
    rebase over everything else.
12. Docs (`docs/features.md`, `docs/agent.md`, `CHANGELOG.md`,
    `docs/security-review.md` r16 subsection), full build and test per
    AGENTS.md.

## Test expectations

Roughly 35 to 45 new tests from the current **861** (0.20.0-alpha,
verified green before this pack was written). Scripted-LLM fakes
throughout; zero live model, zero live embedding endpoint (fake
`IEmbeddingService` with switchable dimensionality for 2.4). Runner:
`dotnet test src/Aether.Tests/Aether.Tests.csproj`. Zero-warning build
enforced; tests stay sequential.

Security-review touch: 1.4 (workspace identity on approval execution)
and 2.2 (a new model-authored write path into the memory store, gated
by Memory.Enabled and dedupe-capped) get a short r16 subsection.
Neither loosens a gate; 1.4 narrows where an approved action can land.

## Explicit rejections (do not implement, do not re-propose)

- **No refusal of cross-workspace approvals.** Execute against the
  task's stored root (1.4); do not add a refusal dialog for an action
  the user already previewed and approved.
- **No parallel children, no depth beyond 1, no approval inheritance.**
  r15's rejections all stand unchanged.
- **No memory vector index and no recall-cap tuning.** Store scale is
  hundreds of rows; the in-process scan stays.
- **No LLM-judged memory quality or dedupe.** Dedupe stays the exact
  normalized-content match in `MergeAndSaveAsync`.
- **No automatic re-embed on model switch.** Stale-vector clearing is
  user-clicked only (2.4); a settings change must never trigger a bulk
  destructive write on its own.
- **No Doctor checks for memory embedding state.** The Memories page is
  the surface; Doctor does not grow subsystem knowledge (standing rule).
- **No full theming pass.** 3.5 consolidates the palette values that
  exist; auditing every hardcoded color against both theme variants is
  its own future round if the owner wants it.
- **No virtualization of the recent-tasks list.** Capped at 25 rows.
- **No undo system for conversation delete.** Confirm dialog only.
- **No new nav panel for agent tasks.** The list lives inside
  AgentView's existing layout.
