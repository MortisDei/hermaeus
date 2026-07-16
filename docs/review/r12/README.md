# Review Round 12 (r12)

Theme: **ViewModels deep-dive**. Aether.ViewModels is the second-largest
project in the repo (37 files, ~10k lines) and, like Aether.Rag before
r10 and Aether.Services before r11, had never had a dedicated per-file
audit: it has only been touched by targeted fixes (r9 threading, the
r11 wizard field fix). This audit read every .cs file in the project
plus the DI composition root and the settings-service seams they depend
on. The r11 wizard-singleton bug was not an isolated mistake; it is one
instance of two systemic patterns that run through the whole layer:

1. **The live settings object is a shared mutable global.** ViewModels
   write into `ISettingsService.Settings` directly, outside the
   apply/save path: selecting a chat model overwrites the global
   `Llm.MaxTokens`; a Trust rescan and the Local AI setup scan copy
   unapplied text-box values into live settings without saving; the
   settings Save applies every tab's edits to the live object *before*
   validation and rolls back only the data root on failure. Any later
   save from anywhere (adding a Local API token, saving a server
   config) silently persists all of it.
2. **Fire-and-forget async with UI-bound state.** `RunOnUi` always
   posts (even from the UI thread), so "mutate collection via RunOnUi,
   then serialize it" saves the pre-mutation list: clearing or
   dismissing toast history writes the *old* history to disk and it
   resurrects on restart. Per-keystroke handlers launch overlapping
   unawaited refreshes with no debounce or cancellation (memory search,
   agent workspace file query), interleaving Clear/Add on bound
   collections.

Headline single-item findings, all verified in code:

- **Finishing the setup wizard on first run leaves the app
  uninitialized.** `MainWindowViewModel.InitializeAsync` returns early
  to show the wizard, and `WizardCompleted` only navigates to chat:
  servers never auto-start, models/datasets/agent/benchmarks never
  load until an app restart or a lucky panel navigation.
- **Re-running the wizard and changing the data root bypasses the
  migration path entirely** (plain `SaveAsync()`, no previous root), so
  the databases stay behind in the old root: the same "conversations
  lost" symptom the r11 field fix addressed, via a second door.
- **Every settings save triggers a Services rebuild storm.**
  `SettingsChanged` fires after every `SaveAsync`; ServicesViewModel
  rebuilds all server rows, fires `ServerAvailabilityChanged`, which
  force-invalidates the model cache and refetches models over HTTP,
  plus an orphan port scan, on every save of anything.
- **The agent's default workspace is the entire user profile**, and
  `AgentViewModel.LoadAsync` (run at every startup) unconditionally
  analyzes it and writes a "Workspace profile" memory entry for it.
- **A background model-list refresh silently resets chat sampling
  parameters.** Model instances are recreated on each fetch, so
  reassigning `SelectedModel` fires `OnSelectedModelChanged` and stomps
  user-tuned Temperature/TopP/etc mid-session.

## Documents

- `01-settings-lifecycle.md` - live-settings mutation, save rollback,
  wizard migration bypass, the rebuild storm, unsaved side-channel
  writes, dead secret-reference guards.
- `02-async-and-threading.md` - toast-history post-then-save ordering,
  unprotected SendAsync, missing debounce/cancellation on hot paths,
  per-line log rebuilds, concurrent model reloads, RunOnUi semantics.
- `03-runtime-vm-correctness.md` - first-run dead end after the wizard,
  startup init fragility, stale model re-matching, the agent user
  profile workspace, RAG add-to-dataset target trap, reindex config
  mutation, benchmark rerun guards, disposal leaks, small fixes.
- `04-roadmap.md` - version, sequencing, test expectations, security
  review touch, explicit rejections.

## How to work this pack

Same conventions as r1-r11 (see `docs/review/archived/`): every item
has acceptance criteria; check archived rounds before re-proposing
anything explicitly rejected; zero-warning builds
(`TreatWarningsAsErrors` solution-wide); tests run via
`dotnet test src/Aether.Tests/Aether.Tests.csproj` (see the
`build-and-verify` skill); no em dashes anywhere in code, comments, or
docs; the approval-gated agent security posture is non-negotiable.
Nothing in this pack deletes user data; items 1.1 and 3.1 exist
precisely to stop data from *appearing* lost.
