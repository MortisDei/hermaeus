---
name: add-a-feature
description: The Hermaeus workflow for adding a bounded feature across Core, Services, Agent, Rag, ViewModels, Desktop, DI, settings, docs, and tests.
---

# Add a feature to Hermaeus

This skill supplements `AGENTS.md`. Read the current source and authoritative
docs before editing. Keep the change narrow and follow the dependency flow:
Desktop -> ViewModels -> Services, Agent, or Rag -> Core. Never add a reference
against that flow.

## Bound the design first

- Put immutable models and service contracts in `src/Hermaeus.Core`. Core has
  no UI types and no new package references.
- Put runtime, storage, settings, secrets, voice, and Doctor implementations
  in `src/Hermaeus.Services`; use `src/Hermaeus.Agent` or
  `src/Hermaeus.Rag` when the behaviour belongs to those domains.
- Add an interface only when multiple implementations, a provider boundary,
  or a test seam genuinely warrants it. Otherwise prefer a sealed concrete
  service.
- Register services in `src/Hermaeus.Composition/HermaeusServiceRegistration.cs`
  and register desktop ViewModels in `src/Hermaeus.Desktop/App.axaml.cs`.
  Follow the existing singleton lifetime and constructor-injection patterns.
- Keep ViewModels free of `Avalonia.*`. Keep code-behind limited to control
  events that cannot be expressed by bindings or commands. Business logic
  belongs in services or ViewModels, not AXAML code-behind.

## Settings and persistence

- Add settings to the matching `AppSettings` domain section: Llm, Tts, Rag,
  Ui, DataManagement, Memory, Mcp, LocalApi, Agent, Stt, or trust models.
  The settings-section guard must remain consistent with `AppSettings`.
- Process, server, runtime, executable, and asset configuration belongs on
  Services. Preference-only knobs belong on Settings. Use the existing single
  `SettingsService` save flow and never write `settings.json` directly.
- Store secrets through `ISecretStore`, persist only references, and pass log
  text through `RedactionService` before persistence.
- Persistent state files use atomic replacement. SQLite uses
  `Microsoft.Data.Sqlite` and additive `SqliteMigrationRunner` migrations only.
  Derived indexes must be rebuildable from their source of truth. Long writes
  remain batched and cancellable.

## UI, safety, and documentation

- Reuse existing styles and `MossEmptyState` for empty states. Icon-only
  controls need tooltips. Do not add a new provider-specific bypass, weaken a
  risk gate, or expose a managed server beyond localhost.
- User-visible behaviour requires truthful updates to `docs/features.md`,
  `docs/user-guide.md` when the user workflow changes, the relevant workflow
  doc, and `CHANGELOG.md`. Do not describe planned work as shipped.
- For user-controlled paths, processes, downloads, network, or Agent tools,
  apply the security-posture skill as well. Use `ArgumentList`, path and
  symlink containment checks, SHA256 verification, and deterministic Agent
  risk classification already present in the repository.

## Tests and handoff

- Read `docs/testing.md` before adding or changing tests. Add representative
  regression coverage at the highest practical seam, including lifecycle,
  cancellation, failure, and sibling paths where the defect class requires it.
- New harness-style methods must be added to
  `src/Hermaeus.Tests/XunitHarnessTests.HarnessCases`; the reflection guard
  rejects unregistered methods. Platform-only tests use `WindowsOnlyFact`.
- Run focused tests during implementation, then use build-and-verify for the
  sequential full suite, Debug and Release builds, final coverage, and process
  audit. Leave desktop interaction and owner live validation explicitly gated
  when source tests cannot prove them.
