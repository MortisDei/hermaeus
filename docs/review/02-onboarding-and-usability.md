# 02 - Onboarding and usability

## Problem statement

The setup wizard (src/Aether.ViewModels/SetupWizardViewModel.cs,
shown on first launch via MainWindowViewModel.cs:141) collects paths
and choices but performs no setup: step "Model folder" asks the user
to point at a folder of GGUF files a novice does not have, and step
"Voice" describes the install plan (SetupWizardViewModel.cs:213-231)
without offering to run it. A first-time user finishes the wizard with
an app that can neither chat nor speak. Tooltips exist in 11 views but
roughly 15 views have none, including every dense settings section.
Markdown tables in assistant replies render as nothing, and links are
not clickable.

## 2.1 Guided setup: download a starter model from the wizard

Extend wizard step 2 (Model folder) with two paths, defaulting by
whether the chosen folder already contains a `.gguf`:

- **"I have models"**: current behavior (pick folder).
- **"Download a starter model (recommended)"**: hardware-aware
  recommendation plus one-click download.

Recommendation logic: new pure static
`StarterModelCatalog.Recommend(SystemInfoSnapshot)` in
`Aether.Services`, keyed on best-GPU VRAM from
`SystemInfoService` (src/Aether.Services/SystemInfoService.cs:47
Gpus): under 6 GB or no GPU -> ~3B Q4 tier; 6-12 GB -> ~8B Q4 tier;
over 12 GB -> ~14B Q4 tier. The catalog is a hardcoded list of three
entries, each with display name, size on disk, direct Hugging Face
URL, and pinned SHA256. The implementer picks current
instruct-tuned GGUFs at spec time and records the hashes in the
catalog; per the `security-posture` skill every download must verify
against the pinned hash using the existing
`ModelDownloadService.DownloadAsync` + `VerifyHashAsync`
(src/Aether.Services/ModelDownloadService.cs:25,100).

Download runs in the wizard with the existing `DownloadProgress`
surfaced as a progress bar, cancel supported, and on success writes
the model into the configured model folder and sets
`ManagedServers[0].ModelPath` exactly as the manual path does
(SetupWizardViewModel.cs:250-257). Failure shows the message inline
and leaves the wizard on the step (no partial state saved; a failed
hash check deletes the file, matching ModelDownloadService's existing
behavior).

**Acceptance criteria**

- Recommend() unit-tested for the three tiers plus the no-GPU probe
  case ("GPU probe unavailable" row must land in the smallest tier).
- Catalog entries all declare a SHA256; a test asserts none are
  empty and URLs are https.
- Wizard flow test (ViewModel level, downloader faked): choose
  download path, complete, assert settings persisted; fail hash,
  assert settings untouched and error text set.
- The wizard step's copy states file size before download starts.

## 2.2 Voice install from the wizard

The Voice step currently lists `VoiceInstallPlan` steps as text. Add
an "Install now" button that executes the plan for the selected
provider through the same code path Settings > Voice uses (the
install command on `TtsSettingsViewModel`; implementer wires the
existing command rather than duplicating install logic), with
progress text and a completed/failed state on the step. Consent
framing stays: the plan summary and `RiskNotes`
(SetupWizardViewModel.cs:226-227) remain visible above the button,
and nothing downloads until the user clicks.

**Acceptance criteria**

- Install invoked from the wizard is byte-identical in effect to
  install from Settings (same service call; assert via fake registry
  in a VM test).
- Wizard remains navigable while installing (Back disabled, Skip
  allowed; cancel stops the download).
- After a successful install, the Finish step's summary line confirms
  voice is ready; after skip/failure it says how to finish later
  (Settings > Voice).

## 2.3 Tooltip sweep

Add `ToolTip.Tip` to every interactive control (TextBox, ComboBox,
CheckBox/ToggleSwitch, Slider, NumericUpDown, icon-only Button) in
views that currently have zero tooltips: SettingsLlmSectionView,
SettingsMemorySectionView, SettingsMcpSectionView,
SettingsRagSectionView, SettingsLocalApiSectionView,
SettingsUiSectionView, SettingsLocalAiSetupSectionView, DoctorView,
MemoriesView, ModelManagementView, LogsView, SystemOverviewView,
SetupWizardView. Views that already have some (ChatView,
ServicesView, BenchmarkView, etc.) get a gap pass for icon-only
buttons and settings inputs.

Tooltip style rules (write once at the top of the first edited file
as a comment for consistency): one sentence, plain language, states
what the control does and, for settings, the default value and when
a restart/reload is needed. No marketing tone. Text lives inline in
AXAML (no resource indirection this round).

**Acceptance criteria**

- Grep-based checklist in the PR description: for each listed view,
  count of interactive controls vs `ToolTip.Tip` occurrences, with
  named exceptions justified (e.g. a TextBox whose watermark already
  fully explains it).
- Settings tooltips for numeric fields state units (tokens, ms, MB).

## 2.4 Markdown viewer: tables and clickable links

`MarkdownViewer` (src/Aether.Desktop/Controls/MarkdownViewer.cs)
parses with `UseAdvancedExtensions` (line 30-31), so `Markdig` emits
`Table` blocks, but `RenderBlock` (line 155-170) has no case for
them and `RenderFallback` returns empty text for container blocks:
**tables in assistant replies render as nothing**. Links render
colored but inert (line 401-409).

- Render `Markdig.Extensions.Tables.Table` as a `Grid`: header row
  bold with a bottom separator, cells as the existing inline
  rendering, column widths Auto with the last column Star, horizontal
  scroll via the row's parent when overflowing.
- Make `LinkInline` clickable: pointer cursor, underline on hover,
  `ToolTip.Tip` showing the target URL, click opens the OS browser.
  Security posture: only `http`/`https` schemes launch (mirror the
  scheme check used by existing open-URL helpers; `file:`, `javascript:`
  and everything else render as plain styled text, not clickable).

**Acceptance criteria**

- Unit-testable pieces extracted where practical (scheme gate is a
  pure function with tests: https ok, http ok, file/javascript/data
  refused).
- Manual verification note in the PR: a reply containing a 3x3 table,
  a link, and a code block renders all three (screenshot).
- Streaming a partial table (mid-generation) never throws; worst case
  it renders incompletely until the next debounce tick.

## 2.5 Fix the thinking indicator (dead markup from aea2326)

MessageControl.axaml:32-39: the avatar `PathIcon` sets
`RenderTransform="new ScaleTransform(1)"` (a C# expression pasted
into an attribute) alongside a property-element RenderTransform, and
`Classes.thinking-pulse` binds
`$parent[UserControl].DataContext.IsGenerating`, but MessageControl's
DataContext is `MessageViewModel`, which has no `IsGenerating`. The
pulse never runs.

Fix: bind the class to the message's own `IsStreaming` (already on
MessageViewModel and already used at line 42), delete the bogus
attribute, keep the property element. The animation style itself
(ChatView.axaml:21-41) is fine and stays.

**Acceptance criteria**

- Pulse animates only on the currently streaming assistant message
  and stops when `IsStreaming` flips false.
- No Avalonia binding errors for MessageControl in the debug log.

## 2.6 Empty states and first-run affordances

Audit the primary panels' empty states. Chat already has one
(ChatView.axaml:324); it must gain a branch for "no model
configured / no backend running": if `SelectedModel` is null or the
provider list is empty, show one sentence plus two buttons ("Open
setup wizard", "Open Services") instead of the generic prompt hint.
RAG (no datasets), Memories (no memories yet), Agent (no workspace
selected), and Benchmark (no runs) each get a one-sentence empty
state with the single most useful action button. Reuse the wording
style of the r6 "your data, your machine" copy: plain, short,
actionable.

**Acceptance criteria**

- Each listed view shows its empty state only when its collection is
  actually empty (bindings on existing Has*/Count properties; add a
  property only where none exists).
- The chat no-model branch is testable at ViewModel level (expose the
  condition as a computed property; test both branches).
