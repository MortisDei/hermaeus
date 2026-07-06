---
name: add-a-feature
description: The end-to-end pattern for adding a feature to Aether across Core, Services, ViewModels, and Desktop, including DI registration and settings. Use when creating a new service, panel, or user-facing capability.
---

# Adding a feature to Aether

Follow the dependency flow: Desktop → ViewModels → (Services/Agent/Rag) → Core.

## Steps

1. **Contract and models** in `src/Aether.Core`: records/POCOs under
   `Models/`, and a service interface under `Services/` **only if** more than
   one implementation is plausible (providers, backends, checks). Otherwise a
   concrete class in `Aether.Services` is preferred; do not add ceremony
   interfaces.
2. **Implementation** in `src/Aether.Services` (or `Aether.Rag`/`Aether.Agent`
   if it belongs to those domains). Follow existing patterns: constructor
   injection, `sealed` class, async with `CancellationToken` parameters,
   atomic writes for files, `RedactionService` before persisting any log text.
3. **DI registration** in the Desktop composition root (`src/Aether.Desktop`,
   `App.axaml.cs` / service collection setup). Register alongside similar
   services; singletons are the norm here.
4. **ViewModel** in `src/Aether.ViewModels` using CommunityToolkit.Mvvm
   (`[ObservableProperty]`, `[RelayCommand]`). ViewModels must not reference
   `Avalonia.*` — marshal to the UI thread via the patterns already used
   (dispatcher abstractions), not `Avalonia.Threading` directly.
5. **View** in `src/Aether.Desktop/Views` as `.axaml` + minimal code-behind.
   Reuse styles from `Styles/`; match existing spacing and toast/notification
   patterns. Wire navigation via `MainWindowViewModel`.
6. **Settings**, if needed: add a property to the matching domain section
   (`LlmSettings`, `RagSettings`, `MemorySettings`, `TtsSettings`,
   `UiSettings`, `DataManagementSettings`, trust models) — never a loose
   top-level key. Expose it via the corresponding section ViewModel in
   `SettingsSectionViewModels.cs`.
7. **Storage**, if needed: SQLite via `Microsoft.Data.Sqlite`; new tables and
   columns go through `SqliteMigrationRunner` as additive versioned
   migrations. Never edit an existing migration.
8. **Docs and changelog**: update `docs/features.md`, the relevant workflow
   doc, and `CHANGELOG.md`. Describe only what is actually implemented.
9. **Tests**: add cases to the harness in `tests/Aether.Tests` (see
   build-and-verify skill for how to run it).

## Anti-patterns to avoid

- Adding new checks inside `DoctorService` / `LocalAiSetupService` when the
  logic belongs to the feature's own service.
- New NuGet packages (needs explicit justification per AGENTS.md).
- Business logic in AXAML code-behind or ViewModels growing multi-service
  orchestration (extract a service instead).
- Provider-specific `if` chains in shared services — route through
  profile/provider tags as existing code does.
