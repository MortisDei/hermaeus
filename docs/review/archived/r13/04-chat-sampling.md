# 04 - Chat header sampling flyout (replace the orphan Temp spinner)

Owner screenshot 1: the chat header has a lone "Temp" NumericUpDown
(src/Aether.Desktop/Views/ChatView.axaml:90-97). Owner: "Does having
temp here serve any real benefit as top_p/k etc cannot be changed from
here. I feel it should not be here, or make it a clickable drop down
window, with all settings available ... You decide."

Decision: keep in-chat tunability, expand it. `ChatViewModel` already
holds the full per-session sampling state as local properties
initialized from settings (ChatViewModel.cs:229-231) and sends every
one of them per request via `BuildChatOptions`
(ChatViewModel.cs:873-884): Temperature, MaxTokens, TopP, TopK, MinP,
RepeatPenalty, FrequencyPenalty, PresencePenalty. Model selection
already re-applies per-model profile defaults on change
(ChatViewModel.cs:1257-1259, with the r12 guard against background
refresh stomping user-tuned values). Only the UI is missing. Removing
temp entirely would throw away a working, correctly-scoped feature;
a flyout completes it instead.

## 4.1 Sampling flyout

- Replace the Temp label + NumericUpDown with one compact header
  button showing the live temperature (e.g. "T 0.7") with tooltip
  "Sampling settings for this conversation". Click opens an Avalonia
  `Flyout` (repo has flyout/expander precedent; keep it simple).
- Flyout content: two-column grid of labeled NumericUpDowns bound to
  the existing ChatViewModel properties, same ranges/increments/
  format strings and the same tooltip wording as the Models page
  editor (ModelManagementView.axaml:95-165) so the two surfaces
  describe the parameters identically: Temperature, Top P, Top K,
  Min P, Repeat penalty, Frequency penalty, Presence penalty, Max
  tokens.
- "Reset to model defaults" button at the bottom: re-applies the
  selected model's profile defaults / global settings exactly like
  `OnSelectedModelChanged` does (extract that default-application
  block into a private method both call; do not duplicate the
  fallback chain).
- A one-line note in the flyout: "Applies to this conversation only."
  (true today: these are VM-local, never written back to settings -
  the r12 1.x work made that boundary explicit; keep it.)

Acceptance criteria:
- All eight parameters editable from the chat header; a send after
  editing carries the edited values in `LlmChatOptions` (existing
  BuildChatOptions test extended or a new one asserting a changed
  TopK/MinP flows through).
- Reset restores model-profile defaults where set, else global
  settings defaults, without touching `ISettingsService.Settings`
  (regression guard: settings object unchanged after open/edit/reset).
- The header button text tracks Temperature changes made inside the
  flyout.
- No live-settings writes from any of this (r12 rule).

## 4.2 Keep the header lean

While in the file: the new button must not widen the header
meaningfully at default window sizes; verify the header row still fits
at the app's minimum window width with nav labels on (Ui.ShowNavLabels
true) - screenshot the before/after in the implementation notes.
