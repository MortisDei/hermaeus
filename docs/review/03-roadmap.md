# 03 - Roadmap

## Version

This round ships as **`0.12.0-alpha`**: one user-facing capability (the
Agent Scenario Suite) justifies the minor bump, matching r5/r6 precedent.
Bump the version in the Desktop csproj assembly info
(`SystemInfoService.cs:29` reads
`AssemblyInformationalVersionAttribute`). Update `CHANGELOG.md`.

## Sequencing

Every phase leaves main shippable.

- **Phase 1 - models + checks.** Doc 01 sections 1.1 (models) and 1.3
  (check evaluator) plus their pure unit tests. No filesystem, no
  runner; this is the bulk of the new test coverage and can merge alone.
- **Phase 2 - loader + library.** 1.2 (store) and 2.1/2.2 (the ten
  scenario folders + csproj copy + validator + library sanity test).
  Verify the output-directory copy lands for Desktop AND Tests before
  moving on; everything later depends on it.
- **Phase 3 - runner.** 1.4-1.7 (sandbox, isolated composition, loop,
  export, EvalMode.AgentScenario, DI registration) with the scripted
  end-to-end tests from doc 01's acceptance list. This is the risky
  phase; the isolation acceptance criteria are non-negotiable and must
  be tested, not eyeballed.
- **Phase 4 - UI.** 2.3 (suite ViewModel + AgentView expander + VM
  tests). Independently droppable if time runs short; the suite is
  already usable from tests without it, but the round's demo value
  ("click Run Scenario Suite") lives here, so drop it only as a last
  resort.

## Test requirements

Existing conventions in `src/Aether.Tests` (see the `build-and-verify`
skill): xunit via `dotnet test`, most cases registered in the
`XunitHarnessTests.cs` harness lists, no test may require a running
llama-server or network. All scenario end-to-end tests drive the runner
with the existing scripted fakes (`FakeSequencedAgentLlm`,
`FakeToolCallingAgentLlm` in `Helpers.cs`) injected as the runner's
`ILlmService`.

Required, mapped to acceptance criteria already stated in docs 01/02:

- Manifest defaults, clamping, schema_version rejection, unknown-key
  tolerance (1.1).
- Loader warnings, user-over-built-in override, duplicate-id skip (1.2).
- Every check type: one pass + one fail case; JsonElement round-trip
  equivalence test (1.3).
- Runner: isolation (user data root untouched, source workspace
  untouched), auto-approve path incl. revertible-patch record, gated
  run_command stops WaitingForUser, blocked delete_file, traversal
  run-error path with allow_run_error both ways, suite export +
  EvalMode.AgentScenario round-trip through SqliteEvalStore,
  cancellation (1.4-1.6).
- Library sanity: 10 ids, validator-clean, no auto_approve of
  run_command (2.2).
- Suite ViewModel: row mapping, headline, CanExecute transitions (2.3).

Expect roughly 25-30 new tests (408 at round start). Coverage floor:
raise 48 to **49** (narrative target in CHANGELOG, as before); the
models/checks/validator are pure logic and should carry it.

Zero-warning build policy applies (`TreatWarningsAsErrors`
solution-wide). The `ScenarioSettings` stand-in must not trip CS0067
(doc 01 shows the empty-accessor event pattern).

## Security review touch

Add a short subsection to `docs/security-review.md` (currently at
0.11.0-alpha) covering the suite:

- Scenario runs execute the real `AgentService` with the real
  `AgentSafetyGate`; the suite adds no bypass, no new tool, and no new
  disposition.
- Isolation guarantees: sandbox workspace copy, isolated data root,
  denial-by-default approvals (auto_approve is per-scenario, explicit,
  and validated to never include run_command in built-ins), so a
  malicious or buggy scenario folder cannot touch user data or execute
  commands. A user-authored scenario CAN set auto_approve for file
  tools, but those writes land in the sandbox copy only.
- `outside_files` exist to detect traversal; they are written under the
  sandbox root, never elsewhere.

## Explicit rejections

Recorded so future rounds do not re-propose them.

- **No LLM judge for scenario scoring.** Checks are deterministic
  predicates over recorded artifacts, full stop. A "explanation quality"
  score is out of scope until someone shows a deterministic proxy.
- **No auto-approval mode for run_command in built-ins**, and no
  "approve everything" suite switch. The gate holding IS the product.
- **No parallel scenario execution.** Sequential keeps LLM contention
  and timing sane; the whole suite is minutes, not hours.
- **No giant composite scenario / "evil repository".** Combining
  behaviours is a future option once single behaviours are covered;
  it is explicitly not this round.
- **No network fixtures, no malicious-package simulation that installs
  anything.** Scenarios are static files; nothing fetches, installs, or
  spawns beyond the (never-approved) recipe machinery.
- **No CI gate on suite pass rate.** The suite needs a real local model;
  CI runs the scripted-fake tests only. A release checklist line
  ("run the suite, record N/M in CHANGELOG") is enough for now.

## Done means

All acceptance criteria in docs 01-02 pass; ~25-30 new tests green via
`dotnet test`; coverage floor 49; zero warnings; the 10 built-in
scenarios load in the app and the suite runs end to end against a local
model from the Agent workbench with live progress and an exported
report; a scenario run demonstrably leaves the user's agent data root
and the scenario source folders untouched; `docs/security-review.md`
updated; CHANGELOG + version bumped to 0.12.0-alpha. Then archive this
pack to `docs/review/archived/r7/`.
