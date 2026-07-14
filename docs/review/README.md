# Review Round 7 (r7)

Theme: **prove the agent behaves**. The industry benchmarks intelligence;
Aether's differentiator is trustworthiness. r7 builds the Agent Scenario
Suite: a library of small, deterministic scenario workspaces that exercise
emergent agent behaviour (retrieval + memory + safety gate + approvals +
lessons interacting), scored by deterministic checks over the artifacts
the agent already produces (task state, transcript, safety-gate rows,
file hashes). After this round, "Run Scenario Suite" answers the question
no benchmark score can: does the agent still refuse, cite, ask, and stay
inside the workspace the way it did last release?

Design principles (from the owner's brief; treat as binding):

- **Tiny scenarios, one behaviour each.** 10 built-in scenarios, each a
  handful of small files readable in a minute. No giant "evil repository",
  no CTF puzzles; realistic engineering situations only.
- **Deterministic checks, not judged answers.** The model under test is
  nondeterministic; every check is a pure predicate over recorded
  artifacts (status, tool rows, hashes, substrings). No LLM judge.
- **The suite must be unable to hurt the user.** Scenario runs happen in
  a throwaway sandbox with a fully isolated agent data root. A scenario
  run must never write to the user's lesson store, task store, memory,
  or any file outside the sandbox. No built-in scenario ever actually
  executes a command (approval is simply never granted for one).
- **Contributable.** A scenario is a folder (scenario.json + workspace/),
  not code. User scenarios load from the data root next to built-ins.

## Documents

- `01-agent-scenario-suite.md` - the engine: manifest schema, loader,
  sandbox + isolated agent composition, runner, check evaluator, report
  export, shared EvalRun projection.
- `02-scenario-library-and-ui.md` - the 10 built-in scenarios (complete
  file contents specified) and the Agent workbench UI section.
- `03-roadmap.md` - version, sequencing, test requirements, explicit
  rejections, security-review touch.

## How to work this pack

Same conventions as r1-r6 (see `docs/review/archived/`): every item has
acceptance criteria; check archived rounds before re-proposing anything
listed under explicit rejections; zero-warning builds
(`TreatWarningsAsErrors` solution-wide); tests run via
`dotnet test src/Aether.Tests/Aether.Tests.csproj` (see the
`build-and-verify` skill); no em dashes anywhere in code, comments, or
docs; the approval-gated agent security posture is non-negotiable and
this round must not weaken it in any way - the suite observes the gate,
it never bypasses it.
