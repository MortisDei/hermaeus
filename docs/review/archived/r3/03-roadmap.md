# r3-03: Roadmap

Sequenced so each phase makes the next cheaper, same discipline as
r1/r2. Phases 1-2 are doc 01, phases 3-4 are doc 02; phase 0 is the
housekeeping the owner asked for plus small verified optimisations.
Every phase lands with tests in the matching test file (create new
`*Tests.cs` files as needed) and doc updates per AGENTS.md.

## Phase 0 - Housekeeping and small optimisations (do first)

### 0.1 Move the test project into src/

`git mv tests/Aether.Tests src/Aether.Tests`, then:

- `Aether.sln`: update the project path (`tests\Aether.Tests\...` ->
  `src\Aether.Tests\...`).
- `src/Aether.Tests/Aether.Tests.csproj`: every
  `../../src/X/X.csproj` ProjectReference becomes `../X/X.csproj`.
- Update every live reference to the old path. Known from grep:
  `AGENTS.md` (solution map row + test command), `README.md`,
  `CONTRIBUTING.md`, `docs/avalonia-upgrade.md`,
  `.claude/skills/build-and-verify/SKILL.md`,
  `.claude/skills/add-a-feature/SKILL.md`. Leave
  `docs/review/archived/` untouched (historical record).
- Remove the now-empty `tests/` directory.

Acceptance: `dotnet build Aether.sln` zero warnings;
`dotnet test src/Aether.Tests/Aether.Tests.csproj` fully green; a
repo-wide grep for `tests/Aether.Tests` matches only archived docs.

### 0.2 CI baseline

There is no `.github/workflows/`. Add one workflow: restore, build
(warnings as errors already enforced), `dotnet test` on ubuntu-latest
and windows-latest. This is the cheapest insurance for everything the
later phases touch. Keep it to build+test; no packaging.

### 0.3 Verified small optimisations

- Drop the `Task.Run` wrappers around pure synchronous work in
  `MemoryInjectionService.SelectMemoriesForInjectionAsync` and
  `MemoryExtractionService.ExtractMemoriesAsync`; make them sync (or
  return `Task.FromResult` at the interface seam if a signature must
  stay async).
- `AgentService.RunStepAsync` serialises the **entire context pack**
  into the per-step trace JSONL every step ([AgentService.cs:210-217](../../src/Aether.Agent/Services/AgentService.cs#L210-L217)).
  With transcripts (Phase 1) this becomes quadratic-ish disk bloat.
  Trace the pack once per task plus per-step deltas (step number,
  action, tool, result summary), keeping the schema documented in
  docs/agent.md.
- `AgentToolExecutor.Summarize` truncates serialized JSON at 4000
  chars mid-structure; replace per Phase 1's per-tool budgets (if
  Phase 1 is imminent, fold it in there instead of doing it twice).

### 0.4 Coverage floor

Coverlet is already referenced. Add a documented coverage run
(`dotnet test /p:CollectCoverage=true /p:Threshold=...` or a script
target in build.ps1/build.sh), record the current line-coverage number
in this doc when first measured, and set the threshold just under it
so it can only ratchet up. Named gaps to close opportunistically while
touching these areas (not a big-bang test-writing pass):
`AgentService.ExtractJson` malformed-response cases, the
`run_command` timeout/kill path, `MemoryInjectionService` budget and
ordering, `ConversationMemoryService` merge/dedupe behaviour.

## Phase 1 - The loop (doc 01: A, B, D, E, G)

Order inside the phase: A (transcript) -> B (auto-run) -> D (edit
tools) -> E (navigation tools) -> G (plan tool). A and B are the
capability unlock; D and E are what make the unlocked loop productive.
Definition of done is the per-item acceptance criteria in doc 01, plus
one end-to-end regression: on a fixture workspace, a multi-step goal
runs unattended from Start to a queued `edit_file` approval, resumes
on approval, and completes, with transcript, logs, traces, and task
state all consistent after an app restart.

## Phase 2 - Native tools and verification (doc 01: C, F)

C (tool-calling API support in Core + the three providers, JSON
fallback) then F (run_command template families + transcript-visible
output + per-task remembered approvals). F lands after C so command
results arrive as proper tool-result messages. Definition of done per
doc 01 acceptance criteria; additionally the JSON-fallback path is
exercised in tests permanently (fake LLM without tool support), since
local models without tool calling remain first-class citizens.

## Phase 3 - Memory upgrades (doc 02 part 1: M1-M5)

M2 and M5 are independent and small; M1 (hybrid recall) is the big
one; M3 (lifecycle + update/forget markers) builds on M2's injection
changes; M4 rides the existing auto-summary LLM call. All schema
changes are additive migrations through `SqliteMigrationRunner` (v4 on
the memories scope).

## Phase 4 - The lesson store (doc 02 part 2)

Deliberately last: its two richest signal sources (structured command
outcomes, transcript-visible results) exist only after Phases 1-2.
Slices, in order:

1. Store + migrations + deterministic capture from command outcomes,
   patch outcomes, approvals, task terminal states.
2. Injection into `AgentContextPack` + relevance/budget + tests.
3. Lessons UI panel (view, edit, pin, retire, delete) + `[LESSON:]`
   stated lessons.
4. Optional: chat-side consumption of global lessons (settings toggle,
   default off) and the idle-time LLM guidance refinement pass.

## Rejected this round

- **Auto-approve / "yolo" mode.** Claude Code has one; Aether should
  not. The per-task remembered approval (01-F) is the ceiling. The
  product's identity is the approval gate.
- **Terminal/PTY embedding or arbitrary shell.** Template families
  only. Arbitrary shell is where the security story dies.
- **Adopting an external agent framework.** The loop is a few hundred
  lines on top of infrastructure that already exists and is tested;
  a framework would import someone else's safety assumptions.
- **Multi-agent orchestration.** One good loop first. Revisit only
  after Phase 2 has real usage.
- **Vector database dependency for memory.** Hundreds of rows;
  in-process cosine is enough (doc 02 M1). Reassess only if memory
  counts grow by orders of magnitude.
- **Letting lessons touch the safety gate.** Restated from doc 02
  because it will be tempting: lessons inform the model, never the
  policy.

## Definition of done for r3

Phase 0 complete (tests relocated, CI running, optimisations landed);
Phases 1 and 2 complete per their acceptance criteria; Phase 3
complete; Phase 4 slices 1-3 complete (slice 4 optional). At that
point the agent loops, edits surgically, verifies its own builds,
remembers per-workspace, and demonstrably learns from its own
outcomes - which is the concrete, local-first version of "chasing the
two behemoths" this codebase can actually own.
