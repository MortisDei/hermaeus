# AGENTS.md

Aether is a native, local-first AI workstation: Avalonia UI + .NET 10,
Windows and Linux. Chat, local RAG, an approval-gated agent workbench, managed
llama.cpp/Ollama/OpenAI-compatible runtimes, local memory, and voice.

## Non-negotiable principles

- Native desktop. Local-first. Provider-agnostic. No vendor lock-in.
- Minimise dependencies: adding a NuGet package requires written justification
  in the PR. Prefer small internal components.
- Security-conscious by default: no shell-string process launches (use
  `ProcessStartInfo.ArgumentList`), localhost binding for managed servers,
  redact before persisting logs, atomic writes for state files, SHA256-pinned
  downloads, path-traversal and symlink rejection for anything under user
  control.
- Never use em dashes in code, docs, or UI text.

## Solution map

| Project | Role | Rules |
| --- | --- | --- |
| `src/Aether.Core` | Models + service contracts | No new package refs. No UI types. |
| `src/Aether.Services` | Runtime, storage, settings, secrets, voice, doctor | SQLite via `Microsoft.Data.Sqlite`; schema changes go through `SqliteMigrationRunner` (additive only). |
| `src/Aether.Rag` | Ingest, retrieval, rerank, citations, traces, evals | ONNX Runtime types must not leak out of this project. |
| `src/Aether.Agent` | Task state, context packs, risk gates, patch queue | `task_state.json` is source of truth; `agent/task_index.db` is a rebuildable index. Risk classification is deterministic; never bypass it. |
| `src/Aether.ViewModels` | MVVM state/commands (CommunityToolkit.Mvvm) | Must never reference `Avalonia.*`. |
| `src/Aether.Desktop` | Avalonia views, styles, entry point, DI root | Views bind to ViewModels; no business logic in code-behind. |
| `tests/Aether.Tests` | Regression harness (custom runner, see below) | |

Dependency direction: Desktop → ViewModels → (Services, Agent, Rag) → Core.
Never add a reference against that flow.

## Build, test, run

```bash
dotnet build Aether.sln                                  # zero warnings enforced (TreatWarningsAsErrors)
dotnet run --project tests/Aether.Tests/Aether.Tests.csproj   # custom runner, NOT `dotnet test`
dotnet run --project src/Aether.Desktop                  # launch the app
./build.sh --skip-restore    # or: pwsh ./build.ps1 -SkipRestore   # packaging
```

Any compiler warning fails the build. Fix the warning; do not suppress it
without a comment explaining the constraint.

## Conventions

- C#: file-scoped namespaces, nullable enabled, `sealed` by default for new
  classes, records for immutable models/DTOs.
- Settings live in domain sections on `AppSettings` (Llm, Rag, Data, Memory,
  Voice, Ui, Trust); one save flow via `SettingsService`. Add fields to the
  matching section; never write `settings.json` directly.
- Secrets: never store raw keys in settings or logs. Use `ISecretStore`
  references. Runtime log output goes through `RedactionService`.
- User-facing state files: write via atomic replacement (temp + move), like
  existing code in `SettingsService`/`BackupService`.
- Docs: user-visible behaviour changes must update `docs/features.md` and the
  relevant workflow doc (`docs/rag.md`, `docs/agent.md`, `docs/voice.md`,
  `docs/benchmarks.md`) plus `CHANGELOG.md`. Do not document planned behaviour
  as existing behaviour.

## Known hot spots (read before large edits)

`DoctorService`, `LocalAiSetupService`, `BenchmarkService`, `ChatViewModel`,
`AgentViewModel`, `RagViewModel` are large and cross-cutting. Make minimal,
focused changes there; do not add new subsystem knowledge to Doctor/Setup if
it can live with the subsystem instead. The long-term direction for these
files is documented in `docs/review/` (2026-07 architecture review) — align
new work with it.

## Working agreement

- Smallest complete change that solves the task; no stubs, TODO placeholders,
  or partial implementations unless explicitly requested.
- Do not invent APIs, files, or architecture; follow existing patterns unless
  improving them is the point of the task.
- If you find a bug or unsafe pattern: fix it if in scope, otherwise record it
  and mention it in your final response.

Before finishing any task:
1. `dotnet build Aether.sln` and run the test harness.
2. Update docs if behaviour, commands, setup, or features changed.
3. Commit; push only after build/tests pass and docs are truthful.
4. If any step was impossible, say exactly what and why.

Final response format: what changed; build/test result; risks or follow-ups.
