# Review round 29: Things that look like they work

Audience: the implementing agent. Read this file, then the numbered docs in
order. Doc 06 is the roadmap and sequencing contract.

## Why this round exists

Every item in this round is something that presents itself as working and
is not. That is the whole theme, and it runs through the UI and the test
suite alike.

**A settings page with no way to save.** `ServicesView.axaml:598-599` hosts
the Voice and STT cards. Both are bound to DI singletons shared with
`SettingsViewModel` (`App.axaml.cs:226-229`). The only thing in the app that
writes those values to `settings.json` is `SettingsViewModel.SaveAsync`
(`SettingsViewModel.cs:218`), reached from exactly one button, on the
Settings page (`SettingsView.axaml:19`). The Services page has a per-profile
Save (`ServicesView.axaml:102`) and a per-server Save Config
(`:530`), so it teaches the user that Services saves itself, and then the
two cards at the bottom silently do not. The owner spent an unknown number
of sessions setting a base URL, a voice, a device and a speed on that page
and losing all of it on restart. The page's own header text says "Paths and
config are saved per server" (`:18`), which is true of the servers and
false of everything below them.

**A picker that cannot be told from a text box.** Settings > Voice routes
each channel through an `AutoCompleteBox`
(`SettingsVoiceSectionView.axaml:34-39`). It has no chevron, no
click-to-open, and Avalonia's `AutoCompleteBox` shows nothing until enough
characters are typed, so a control whose entire job is "choose one of the
provider's voices" looks like an empty text field reading "(Default
voice)". The owner has now reported twice that there are no voice
dropdowns in Settings. There are; they just do not look like, or behave
like, dropdowns.

**Copy attributed to a feature that does not exist.**
`ServicesVoiceSectionView.axaml:19` tells the user "Per-channel routing and
named voice profiles are in Settings > Voice." Per-channel routing is
there. Named voice profiles are not: `TtsSettings.Profiles` still exists on
the model (`TtsSettings.cs:96`) but `TtsSettingsViewModel.cs:237` describes
it as "no-longer-editable" and reads it exactly once, to migrate a legacy
`ProfileName`. This is the thing CLAUDE.md forbids in docs, sitting in the
UI.

**A keyboard setting that only works one way round.** With
`Ui.CtrlEnterToSend` true, `AcceptsReturn` is true, Enter inserts a newline
and Ctrl+Enter sends: correct. With it false, `ApplyAcceptsReturn`
(`ChatView.axaml.cs:270-275`) sets `AcceptsReturn` false, so the text box
cannot insert a newline at all, and `OnInputKeyDown` (`:277-288`) only
handles the send combination. In the app's default configuration there is
no key that produces a newline in the chat box. The 0.24.x fix that made
`AcceptsReturn` dynamic solved the send half and left the newline half
unimplemented.

**Action buttons pressed against the bottom of the window.** The transcript
`ScrollViewer` carries `Padding="16,8,16,28"` (`ChatView.axaml:443-446`) and
each assistant message's copy/read-aloud row lives outside the message
border, at the very bottom of the message (`MessageControl.axaml:255-296`).
On the last message in a conversation those buttons end up flush against
the input bar and cannot reliably be hit.

**A cursor flicker two fixes have not killed.** Tooltips were moved below
their control (`AppStyles.axaml:35`) and the nav row was made one
continuous hand-cursor region (`MainWindow.axaml:52-54`). The owner reports
it still flickers, and now reports where: **on the very outer edge of each
button**. That is the third cause and it points at the tooltip popup, not
the cursor, which is why two cursor fixes did not touch it.

**And a test suite nobody has ever looked at as a whole.** r28 doc 04 added
a TRX logger to CI and asked which tests spend Windows's 491 seconds. The
answer is in, it is measured on both legs and cross-checked against this
machine, and it says all three of r28's stated guesses were wrong in
different ways. It also turned up 16 tests that report **Passed** on Linux
without executing a single assertion, and roughly 9 seconds of real
wall-clock sleeping that both legs pay. Doc 04 has the numbers.

So: doc 01 makes five controls do what they look like they do. Doc 02 turns
the model list into cards and shows where a model came from. Doc 03 lets the
owner steer a task that is already running, without loosening the gate that
makes the agent safe. Doc 04 is the first whole-suite audit this project has
had. Doc 05 is small items and the copy that promises a feature that was
removed.

## Documents

| Doc | Theme |
| --- | --- |
| `01-controls-that-do-what-they-look-like.md` | Save on Services, a voice picker that is a picker, a reachable chat action row, Ctrl+Enter working in both directions, and the third cursor-flicker cause |
| `02-models-as-cards.md` | The model list as a wrapping grid of tiles, a Hugging Face badge sourced from the manifest, and the per-model editor moved into a flyout |
| `03-steering-a-running-task.md` | An instruction delivered into a run in progress, interrupting the planner call but never a tool mid-write, with the risk gate untouched and a test proving it |
| `04-tests-that-actually-run.md` | The Windows CI gap resolved with measurements, tests that pass without running, real-time sleeps replaced with deterministic waits, and a coverage read |
| `05-small-open-items.md` | The voice-profiles copy, the coverage-threshold drift, deferred items drained, docs |
| `06-roadmap.md` | Ships as 0.36.0-alpha; sequencing, test budget, descope order, explicit rejections |

## Standing rules for the implementing agent

- **Verify before implementing.** Every file:line reference in this pack was
  exact against `f03e7c1` (the r28 merge to `main`, v0.35.0-alpha).
  Re-verify before editing. `ChatViewModel.cs`, `AgentViewModel.cs`,
  `ServicesViewModel.cs`, `RagViewModel.cs` and `ModelManagementViewModel.cs`
  are all large and move often; CLAUDE.md names the first four as hot spots.

- **Doc 01 is five separate bugs, not one theme.** They are grouped because
  they share a cause in the owner's experience, not in the code. Land them
  as separate commits. If one of them turns out to be larger than specified,
  it is descoped alone and the other four still ship.

- **1.5's cause is a hypothesis and the doc says so.** Two rounds have now
  fixed a theorised cause of the cursor flicker and not fixed the flicker.
  1.5 requires the implementer to confirm the mechanism in the running app
  **before** editing, and to write down what was observed. A third
  theory-driven fix that ships unverified is worse than leaving it alone,
  because it adds a permanent workaround for something that was never the
  problem.

- **Doc 02 does not change what a model row can do.** Every control in the
  current expander body (`ModelManagementView.axaml:250-400`) survives the
  move into the flyout: display name, description, tags, temperature,
  context size, max tokens, top-p and the rest. A tile that quietly drops a
  field is a regression, not a simplification.

- **Doc 03 must not become a way to widen what runs unattended.** This is
  the one rule in this round that is not negotiable. An instruction injected
  into a running task is *user text*, exactly as untrusted as the goal it
  was created with. It never carries an approval, never marks a tool
  pre-approved, never changes a risk classification, and never sets
  `requires_approval`. `AgentSafetyGate` is not edited in this round. 3.6 is
  the regression test that pins this and is not optional; if implementing
  doc 03 seems to require touching the gate, stop, because it does not.

- **Doc 03's interrupt cancels the model call, never a tool mid-write.**
  The owner asked for mid-step interruption and that is what 3.4 specifies,
  with one boundary: the cancellable part of a step is the planner
  inference, which is also the long part. A tool that has begun executing
  runs to completion and records its result. Cancelling a half-written patch
  apply or a running `run_command` is how a workspace ends up in a state no
  task file describes.

- **Doc 04 changes CI and tests, never production behaviour to suit a
  test.** If a test is slow because the code it tests waits, the fix is to
  make the wait injectable, not to shorten a production timeout.

- **Doc 04's platform skips become skips, not deletions.** The 16 tests that
  currently `return` early on Linux are testing real Windows behaviour and
  are worth keeping. They must report **Skipped**, which is honest, instead
  of **Passed**, which is not. Do not delete them and do not make them
  cross-platform by weakening what they assert.

- No em dashes anywhere. Zero warning build. All tests pass. Register any
  new harness-style test methods in `XunitHarnessTests.HarnessCases`; the
  `HarnessRegistrationGuardTests` reflection guard fails otherwise.

- **No new NuGet packages.** Nothing here needs one. Doc 04's skip mechanism
  is a `[Fact(Skip=...)]`-equivalent built from xunit 2.9.2's existing
  surface (see 4.3); it does not need `Xunit.SkippableFact`.

- Settings changes go in the matching domain section on `AppSettings` and
  through `SettingsService`; a guard test fails if the section list and
  `AppSettings` disagree. Never write `settings.json` directly.

- Schema changes are additive and go through `SqliteMigrationRunner`.
  `task_state.json` stays the agent's source of truth; doc 03's additions to
  `AgentTaskState` are JSON-additive and must load a pre-existing task file
  written before this round.

- Icon-only controls need tooltips; the guard test scans axaml and fails
  without one. Doc 02's tiles add icon-only controls.

- Update `README.md`, `docs/features.md`, `docs/agent.md`, `docs/voice.md`
  and `CHANGELOG.md`. Run r25's doc-drift guard. Do not document planned
  behaviour as existing behaviour, and while you are in `docs/features.md`,
  fix the voice-profiles claim rather than describing it (5.1).

- `docs/review/deferred.md` is updated at close-out. Doc 04 closes one open
  row; see 06's housekeeping.

- This round lands via pull request per `docs/pull-requests.md`: branch
  `r29/round` from `main`, commit there, open the PR with the template,
  merge after CI is green on both matrix legs. One open PR at a time. No AI
  co-author trailer on commits.

## If this session was interrupted

- The pack is the contract. Nothing in it depends on remembering a
  conversation.
- Doc 06 carries a sequencing table with an explicit "landed / not landed"
  column. Update it as work lands, in the same commit as the work. That
  table, plus `git log --oneline main..HEAD`, is the whole recovery
  procedure.
- Commit after each document, and within doc 01 after each of the five
  fixes. An interrupted session should cost at most one item.
