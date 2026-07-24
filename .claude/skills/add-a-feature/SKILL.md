---
name: add-a-feature
description: The end-to-end pattern for adding a feature to Hermaeus across Core, Services, ViewModels, and Desktop, including DI registration and settings. Use when creating a new service, panel, or user-facing capability.
---

# Adding a feature to Hermaeus

Follow the dependency flow: Desktop → ViewModels → (Services/Agent/Rag) → Core.

## Steps

1. **Contract and models** in `src/Hermaeus.Core`: records/POCOs under
   `Models/`, and a service interface under `Services/` **only if** more than
   one implementation is plausible (providers, backends, checks). Otherwise a
   concrete class in `Hermaeus.Services` is preferred; do not add ceremony
   interfaces.
2. **Implementation** in `src/Hermaeus.Services` (or `Hermaeus.Rag`/`Hermaeus.Agent`
   if it belongs to those domains). Follow existing patterns: constructor
   injection, `sealed` class, async with `CancellationToken` parameters,
   atomic writes for files, `RedactionService` before persisting any log
   text. No em dashes in any code, docs, or UI strings.
3. **DI registration** in the Desktop composition root (`src/Hermaeus.Desktop`,
   `App.axaml.cs` / service collection setup). Register alongside similar
   services; singletons are the norm here.
4. **ViewModel** in `src/Hermaeus.ViewModels` using CommunityToolkit.Mvvm
   (`[ObservableProperty]`, `[RelayCommand]`). ViewModels must not reference
   `Avalonia.*`; marshal to the UI thread via the patterns already used
   (dispatcher abstractions), not `Avalonia.Threading` directly.
5. **View** in `src/Hermaeus.Desktop/Views` as `.axaml` + minimal code-behind.
   Reuse styles from `Styles/`; match existing spacing and toast/notification
   patterns. Wire navigation via `MainWindowViewModel`.
6. **Settings**, if needed: add a property to the matching domain section on
   `AppSettings` (`LlmSettings`, `TtsSettings`, `RagSettings`, `UiSettings`,
   `DataManagementSettings`, `MemorySettings`, `McpSettings`,
   `LocalApiSettings`, `AgentSettings`, trust models); never a loose
   top-level key. Expose it via the corresponding section ViewModel in
   `SettingsSectionViewModels.cs`. Placement rule (v0.28.0 precedent):
   process/server/runtime configuration belongs on the Services page;
   preference-only knobs belong on the Settings page.
7. **Storage**, if needed: SQLite via `Microsoft.Data.Sqlite`; new tables and
   columns go through `SqliteMigrationRunner` as additive versioned
   migrations. Never edit an existing migration.
8. **Docs and changelog**: update `docs/features.md`, the relevant workflow
   doc, and `CHANGELOG.md`. Describe only what is actually implemented.
9. **Tests**: add cases to the harness in `src/Hermaeus.Tests`; harness-style
   methods must be registered in `XunitHarnessTests.HarnessCases` or the
   registration guard fails (see build-and-verify skill).

## Anti-patterns to avoid

- Adding new checks inside `DoctorService` / `LocalAiSetupService` when the
  logic belongs to the feature's own service.
- New NuGet packages (needs explicit justification per AGENTS.md).
- Business logic in AXAML code-behind or ViewModels growing multi-service
  orchestration (extract a service instead).
- Provider-specific `if` chains in shared services; route through
  profile/provider tags as existing code does.
- Icon-only buttons without tooltips: the icon-only tooltip guard test in
  `ServiceTests` scans axaml and fails without one (or an allowlist entry).
- Empty states as bare "Nothing here" text: use the shared `MossEmptyState`
  control and follow `docs/mascot.md` "Voice in UI copy".
