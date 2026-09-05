# AGENTS.md

Hermaeus is a native, local-first AI workstation: Avalonia UI + .NET 10, Windows and Linux. Chat, local RAG, an approval-gated agent workbench, managed llama.cpp/Ollama/OpenAI-compatible runtimes, local memory, and voice.

## Non-negotiable principles

- Native desktop. Local-first. Provider-agnostic. No vendor lock-in.
- Minimise dependencies: adding a NuGet package requires written justification in the PR. Prefer small internal components.
- Security-conscious by default: no shell-string process launches (use `ProcessStartInfo.ArgumentList`), localhost binding for managed servers, redact before persisting logs, atomic writes for state files, SHA256-pinned downloads, path-traversal and symlink rejection for anything under user control.
- Never use em dashes in code, docs, or UI text.
- Be concise. Avoid unnecessary narration, repeated analysis and repeated file reads. Minimise token usage where practical, but never at the expense of correctness or code quality.

## Solution map

| Project | Role | Rules |
| --- | --- | --- |
| `src/Hermaeus.Core` | Models + service contracts | No new package refs. No UI types. |
| `src/Hermaeus.Services` | Runtime, storage, settings, secrets, voice, doctor | SQLite via `Microsoft.Data.Sqlite`; schema changes go through `SqliteMigrationRunner` (additive only). |
| `src/Hermaeus.Rag` | Ingest, retrieval, rerank, citations, traces, evals | ONNX Runtime types must not leak out of this project. |
| `src/Hermaeus.Agent` | Task state, context packs, risk gates, patch queue | `task_state.json` is source of truth; `agent/task_index.db` is a rebuildable index. Risk classification is deterministic; never bypass it. |
| `src/Hermaeus.ViewModels` | MVVM state/commands (CommunityToolkit.Mvvm) | Must never reference `Avalonia.*`. |
| `src/Hermaeus.Desktop` | Avalonia views, styles, entry point, DI root | Views bind to ViewModels; no business logic in code-behind. |
| `src/Hermaeus.Tests` | xunit regression suite (`dotnet test`) | Tests run sequentially (shared temp data roots and SQLite pools); do not re-enable parallelization. |

Dependency direction: Desktop → ViewModels → (Services, Agent, Rag) → Core.
Never add a reference against that flow.

## Repository skills

`AGENTS.md` is authoritative for repository-wide rules. Task-specific
procedures live in `.agents/skills/` and supplement these instructions; they do
not override them. Read and apply the relevant skill before work in that area.
If a skill conflicts with `AGENTS.md`, current authoritative documentation, or
source behaviour, treat the skill as drift and correct it.

- `add-a-feature`: bounded feature workflow across architecture, DI, settings,
  UI, documentation, security, and tests.
- `build-and-verify`: approved-host-aware build, sequential test, coverage,
  packaging, process audit, and owner GUI verification.
- `review-round`: numbered review-pack sequencing, evidence, closure, and
  publication ownership.
- `security-posture`: process, secret, download, path, network, and Agent gate
  invariants.
- `storage-and-data-root`: SQLite, backup, atomic writes, and staged Data Root
  migration lifecycle.

## Build, test, run

```bash
dotnet build Hermaeus.sln                                  # zero warnings enforced (TreatWarningsAsErrors)
dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj         # standard xunit; all tests must pass
dotnet run --project src/Hermaeus.Desktop                  # launch the app; see warning below
./build.sh --skip-restore    # or: pwsh ./build.ps1 -SkipRestore   # packaging
./scripts/coverage.sh        # or: pwsh ./scripts/coverage.ps1     # line-coverage ratchet (floor: 60%)
```

Run coverage once as the final automated verification immediately before
creating a commit, after the solution build and test suite pass. Keep coverage
results outside the repository.

Any compiler warning fails the build. Fix the warning; do not suppress it without a comment explaining the constraint.

- `docs/testing.md` is the reference for the suite: what it is, the platform-skip attribute, the injectable-timeout rule, the coverage floor, the guard tests, and why Windows CI is slower than Linux CI. Read it before adding or changing tests.
- Run the documented suite from a normal VS Code or PowerShell terminal before diagnosing failures. Restricted runners may not be able to access the real `%LOCALAPPDATA%\Hermaeus` app-data root or NuGet user configuration, producing misleading temp-root, SQLite, or restore errors. Never modify `src/Hermaeus.Tests/Helpers.cs`, production root selection, or SQLite setup just to compensate for that environment; reproduce the same command with results outside the repo first.
- New harness-style test methods must be registered in `XunitHarnessTests.HarnessCases`; a reflection guard (`HarnessRegistrationGuardTests`) fails the suite otherwise.
- Platform-specific tests use `[WindowsOnlyFact]` so they report Skipped, never an early `return` that reports Passed; a guard test enforces this.
- Never let test output land in the working tree: pass `--results-directory` outside the repo, and never `git add -A`.
- `dotnet run` on the Desktop project reads and writes the SAME `%LOCALAPPDATA%\Hermaeus\settings.json` (Linux: `~/.local/share/Hermaeus`) as the owner's installed app. Look, do not resave settings casually, and never force-kill the process (`taskkill /F`); close it cleanly.
- Releases are tag-driven (`.github/workflows/release.yml`, see `docs/packaging.md`). Never push a version tag; that is an owner action.

## Conventions

- C#: file-scoped namespaces, nullable enabled, `sealed` by default for new classes, records for immutable models/DTOs.
- Settings live in domain sections on `AppSettings` (Llm, Tts, Rag, Ui, DataManagement, Memory, Mcp, LocalApi, Agent, Stt, plus trust models); one save flow via `SettingsService`. A guard test fails if this list and `AppSettings` disagree in either direction. Add fields to the matching section; never write `settings.json` directly. Placement rule: process/server/runtime configuration belongs on the Services page; preference-only knobs belong on the Settings page.
- Secrets: never store raw keys in settings or logs. Use `ISecretStore`
  references. Runtime log output goes through `RedactionService`.
- User-facing state files: write via atomic replacement (temp + move), like existing code in `SettingsService`/`BackupService`.
- UI copy: icon-only controls need tooltips (a guard test scans axaml and fails without one); empty states use the shared `MossEmptyState` control; any text attributed to Moss follows `docs/mascot.md` "Voice in UI copy". When in doubt, drop the personality and state the fact.
- Docs: all authoritative repository documentation must stay synchronized with behaviour, architecture, security and privacy claims, setup and build instructions, configuration, workflows, APIs, UI semantics, licensing, and current feature descriptions. User-visible behaviour changes must update `docs/features.md`, `docs/user-guide.md` when the release-user workflow changes, the relevant workflow doc (`docs/rag.md`, `docs/agent.md`, `docs/voice.md`, `docs/benchmarks.md`), and `CHANGELOG.md`. Stale documentation is a defect. Do not document planned behaviour as existing behaviour. If no documentation update is required, state that explicitly in the implementation or review.

## Known hot spots (read before large edits)

`DoctorService`, `LocalAiSetupService`, `BenchmarkService`, `ChatViewModel`,`AgentViewModel`, `RagViewModel` are large and cross-cutting. Make minimal, focused changes there; do not add new subsystem knowledge to Doctor/Setup if it can live with the subsystem instead. The long-term direction for these files is documented in `docs/review/` (2026-07 architecture review); align new work with it.

## Working agreement

- Inspect the existing architecture and its authoritative docs before adding an abstraction.
- Never weaken approval gates, deterministic risk classification, integrity checks, or other security boundaries to make a workflow easier.
- Do not change the PolyForm Noncommercial/source-available licensing model unless the repository owner explicitly requests that legal change.
- Smallest complete change that solves the task; no stubs, TODO placeholders, or partial implementations unless explicitly requested.
- Do not invent APIs, files, or architecture; follow existing patterns unless improving them is the point of the task.
- If you find a bug or unsafe pattern: fix it if in scope, otherwise record it and mention it in your final response.
- Run focused tests while implementing behavioural changes, then the complete solution build and full repository suite before completion.
- Behavioural and runtime changes follow this sequence: investigate; prove the root cause; complete the bounded implementation; audit the proven defect class and sibling paths; add representative regression coverage; complete automated verification; only then stop for owner live validation; repair and repeat after owner feedback as necessary; commit only when explicitly instructed. Owner live validation is not a reason to stop while assigned investigation, sibling audits, implementation, tests, or automated verification remain incomplete.
- When fixing a defect, investigate the affected defect class before declaring the repair complete. After proving the root cause, audit sibling call sites, construction paths, lifecycle paths, persistence paths, and shared abstractions for the same failure mode, and add regression coverage for the original defect plus representative affected siblings. Keep the audit bounded to the proven failure class and do not expand it into unrelated refactoring.
- Keep documentation synchronized with behaviour, configuration, workflows, APIs, and UI semantics. Stale documentation is a defect. Planned behaviour must never read as implemented. If no documentation update is required, state that explicitly in the implementation or review rather than silently omitting it.

Execution environment selection: Do not knowingly run a required command in an environment that cannot satisfy its dependencies. Commands requiring NuGet restore, NuGet vulnerability/audit metadata access, or other required external network access must run on the approved host when the restricted runner is known to block that access. Likewise, commands requiring MSBuild/VSTest IPC or socket capabilities known to be blocked by the restricted runner must run on the approved host first. Do not perform a known-doomed restricted-runner attempt merely to demonstrate the existing limitation. Use the restricted runner when the command is expected to succeed there or when capability is genuinely unknown.

Before finishing any task:
1. `dotnet build Hermaeus.sln` and run the test harness.
2. Update docs if behaviour, commands, setup, or features changed.
3. Commit only after the owner explicitly authorizes it; push only after build/tests pass and docs are truthful.
4. If any step was impossible, say exactly what and why.

Git rules:
- Code changes land via pull request on a branch, per `docs/pull-requests.md`: one open PR per maintainer at any one time. Documentation-only changes may be committed straight to `main` when the owner says so.
- Never add AI co-author trailers (e.g. `Co-Authored-By: Claude`) to commits.
- Never push version tags or create releases; never change repository settings or visibility. Owner-only.

Commit-message contract for non-trivial work:
- Use a Conventional Commit-style subject such as `fix: preserve chat scroll anchoring`.
- Include a meaningful body describing what changed and why. Subject-only commits are acceptable only for genuinely trivial changes.
- The body must record important correctness, security, privacy, or evidence semantics affected by the change.
- The body must identify relevant verification performed, including focused tests or known verification limits.
- Record deliberate limitations and remaining live gates when they exist. Do not omit this detail for concision.
- Commit messages describe repository changes and durable engineering rationale only. Never include temporary agent workflow or control state such as push instructions, branch or worktree status, model or session information, quota information, or similar execution commentary. Put that status in the final report instead.

Final response format: what changed; build/test result; risks or follow-ups.
