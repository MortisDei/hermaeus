# 01 - Finish the open work before anything else

The working tree has uncommitted changes to nine files. This doc is the
punch list to make them correct, tested, and documented before r18
starts on new surface. Do not add scope beyond what's listed here until
this doc is clear.

## 1.1 Conversation title auto-save reloads the whole list on every keystroke

`MainWindowViewModel.LoadConversationsAsync` (`MainWindowViewModel.cs:211-219`)
now subscribes every `ConversationItemViewModel.MetadataChanged` event to
`SaveConversationMetadataAsync(item, showToast: false)`
(`MainWindowViewModel.cs:325-342`). `MetadataChanged` fires from
`OnTitleChanged`/`OnFolderChanged`/`OnTagsTextChanged`/`OnIsPinnedChanged`/
`OnIsArchivedChanged` (`ConversationItemViewModel.cs:44-65`) - i.e. on
every property write. The details flyout binds `Title` as
`Mode=TwoWay` with no `UpdateSourceTrigger`
(`ConversationListView.axaml:123`); Avalonia's `TextBox.Text` default
trigger is `PropertyChanged`, not `LostFocus`, so every character typed
fires the event.

`SaveConversationMetadataAsync` then calls `await LoadConversationsAsync()`
at the end (`MainWindowViewModel.cs:339`), which clears `Conversations`
and rebuilds it with brand-new `ConversationItemViewModel` instances
(`MainWindowViewModel.cs:211-219`). If the details flyout is bound to
the collection item (the normal case for a `ListBox`-hosted flyout),
the instance backing the open flyout is replaced out from under the
user on every keystroke - expect lost cursor position, lost focus, or
the flyout closing entirely while typing a title. This is worse than
the redundant Save button it replaced.

Fix:
- Debounce: do not save on every property-changed tick. Either (a)
  save on an explicit trigger only (flyout close / focus-lost), which
  is simplest and closest to "no more broken Save button" without the
  keystroke storm, or (b) keep live auto-save but debounce with a
  short timer (400-600ms of inactivity) per item before calling
  `SaveConversationMetadataAsync`, cancelling/restarting the timer on
  each `MetadataChanged`.
- Whichever is chosen, `SaveConversationMetadataAsync` must stop
  rebuilding the entire `Conversations` collection for an in-place
  metadata edit. Update the existing item's fields from the saved
  `Conversation` (title/folder/tags/pin/archive/`UpdatedAt`) instead of
  calling `LoadConversationsAsync()`; reserve the full reload for
  actions that change list membership or ordering (pin/archive toggle,
  delete, new conversation).
- Remove the now-dead `OnDetailsSaveClick` handler
  (`ConversationListView.axaml.cs:26`) - the button was removed from
  `ConversationListView.axaml` but the code-behind method was not.

## 1.2 `TimeDisplay` regression

The diff deleted `partial void OnUpdatedAtChanged(DateTime value) =>
OnPropertyChanged(nameof(TimeDisplay));` while rewriting the other
partial change handlers (`ConversationItemViewModel.cs:44-65`).
`TimeDisplay` (`ConversationItemViewModel.cs:30-42`) is the "12m ago /
Tue / 3 Jul" label bound in the conversation list row. Nothing now
raises its change notification when `UpdatedAt` is set (every save,
rename, new message), so the row's displayed time freezes at whatever
it first showed until the app restarts or the list is fully reloaded.
Restore the handler:

```csharp
partial void OnUpdatedAtChanged(DateTime value) => OnPropertyChanged(nameof(TimeDisplay));
```

## 1.3 `SuggestContextSize` now suggests raising context; doc comment does not agree

`ServerProcessManager.SuggestContextSize` (`ServerProcessManager.cs:347-378`)
changed in two ways from the r17 1.5 design (`archived/r17/01-gguf-context-and-tuning.md`):

- The ladder cap changed from `min(configuredContext, TrainingContextLength)`
  to `min(131072, TrainingContextLength)` - i.e. the ladder search is no
  longer bounded above by what the user configured.
- The final check changed from "return null whenever the configured
  context already fits" to "return null unless a *larger* ladder value
  also fits" (`ServerProcessManager.cs:373-376`), so the function can
  now suggest increasing context, not just downshifting it.

The doc comment above the method (`ServerProcessManager.cs:341-346`)
still describes the old contract verbatim ("the largest value ... that
is `<= min(configuredContext, info's training context)`... Returns null
when the configured context already fits"). That is now false and must
be rewritten to match whichever behavior r18 keeps.

This is a real design fork, not just a doc slip: the r17 spec deliberately
scoped Auto Tune to *shedding* context, never raising it, because Auto
Tune result assignment (`ServicesViewModel.AutoTuneAsync`,
`ServicesViewModel.cs:327-366`) writes straight into `ContextSize`
before the user hits Save, and the r17 acceptance criteria and both
renamed tests (`ServerProcessManagerTests.cs`, the
`SuggestContextSize_picks_the_largest_ladder_value_that_fits` and
`SuggestContextSize_does_not_downshift_sliding_window_models_that_fit`
cases were rewritten to assert the new upward behavior) only covered
the downshift case.

Owner guidance recorded 2026-07-21: the user must always be free to
set a larger context when the model supports it; suggestions and
warnings inform, nothing clamps or blocks. That is compatible with
either branch below (it constrains the *warning/limit* behavior, not
which direction Auto Tune suggests), but it tilts toward keeping the
upward suggestion. Note that doc 04's KV-cache-type option changes
this arithmetic: with q8_0/q4_0 cache the same VRAM fits a much larger
context, so whichever branch is kept must read the cache type (doc 04,
4.2) rather than assuming f16. Decide explicitly rather than by
accident:
- If upward suggestion is wanted (there is a real case for it: the
  user's own report - Gemma E4B QAT at ~4.8 GB VRAM used with 64k
  context configured - is exactly a case where the box has slack and a
  bigger context would fit), keep it, but rewrite the doc comment,
  update the status line in `ServicesViewModel.AutoTuneAsync` so it
  reads correctly when tuning *up* ("Auto-tune found headroom for a
  larger context: raised to 131,072" vs. today's down-shift phrasing),
  and re-verify the sliding-window (gemma) acceptance case in
  `KvCacheMath` against a real GGUF still holds - r17 doc 01 flagged
  sliding-window context as a "known overestimate" for the *fits*
  check; suggesting upward makes an overestimate error in the
  optimistic direction, which is the wrong direction for a feature
  whose failure mode is a server that won't start.
- If not, revert to the r17-scoped, downshift-only behavior and keep
  the reduced headroom constant (1.4) as the actual fix for the user's
  false-warning report.

## 1.4 `GpuHeadroomBytes`: 1.5 GiB to 512 MiB

`KvCacheMath.GpuHeadroomBytes` and `ModelFitEstimator`'s copy
(`KvCacheMath.cs:21`, `ModelFitEstimator.cs:17`) both dropped from
1,610,612,736 (1.5 GiB) to 536,870,912 (512 MiB). This lines up with
the user's report (Gemma E4B QAT, ~4.8 GB actual VRAM use at 64k
context, warning fired anyway) and the two constants were kept in
sync, which is correct - but there are now two independent literal
copies of the same headroom constant in two files
(`KvCacheMath.GpuHeadroomBytes` vs `ModelFitEstimator.GpuHeadroomBytes`
at `ModelFitEstimator.cs:17`). Collapse to one: have
`ModelFitEstimator` reference `KvCacheMath.GpuHeadroomBytes` directly
and delete its own copy, so the next tuning pass only has one place to
change. Also note in the doc comment that this value was itself
initially a rough estimate accepted with 1.2/1.3 (see r17 doc 01, KV
math section) and has now been corrected once against measured
behavior - if it comes up again, prefer deriving it from something
measurable (compute-buffer size llama.cpp actually reports) over a
second guess.

## 1.5 Voice cleanup test: claimed fix does not fix it

The commit message text (not yet an actual commit) describes replacing
"the static `Task.Delay` with a polling loop that checks for file
deletion every 200ms for up to 2 seconds." The actual diff
(`VoiceTempFileCleanupTests.cs:34-39`) only adds a single
`await Task.Delay(100);` before the assertion - no polling, no 2
second budget. Running the suite locally
(`dotnet test src/Aether.Tests/Aether.Tests.csproj`) confirms
`GenerateSpeechAsync_deletes_the_temp_wav_after_a_fake_synthesis_and_playback_cycle`
still fails with this change in place. Implement the polling loop as
actually described:

```csharp
var deadline = DateTime.UtcNow.AddSeconds(2);
while (File.Exists(result.OutputPath) && DateTime.UtcNow < deadline)
    await Task.Delay(200);
Assert.False(File.Exists(result.OutputPath), $"temp wav {result.OutputPath} should be deleted after playback");
```

If it still fails after a real polling wait, the bug is not test
flakiness - look at `OpenAiVoiceProvider.GenerateSpeechAsync`
(`OpenAiVoiceProvider.cs:74-97`): the `finally` block deletes the file
immediately after `PlayWavFileAsync` returns
(`VoiceProviderProcessRunner.cs:94-95`, delegating to
`Aether.Voice.AudioPlayback.PlayAsync`). Confirm whether
`AudioPlayback.PlayAsync` actually awaits playback completion or
returns once the OS player has merely *started* (fire-and-forget);
if it's the latter, the temp file is deleted while a real playback
device or the fake-body test harness might still hold the handle open,
and 100ms/200ms polling papers over a race that a real user could still
hit on a slow disk or antivirus scanner. Fix at the source (don't
delete until playback is confirmed complete, or don't delete
synchronously in the `finally` at all - schedule a delayed/best-effort
cleanup) rather than only in the test.

## 1.6 Docs and changelog

None of `docs/features.md`, `docs/agent.md`/`docs/benchmarks.md`
(context-fit/Auto-Tune behavior change), or `CHANGELOG.md` were updated
for the headroom change, the Auto Tune upward-suggestion change (if
kept), or the new conversation auto-save behavior. Update all three
once 1.1-1.5 are settled; this is a hard gate per `CLAUDE.md` before any
commit.

## Acceptance

- Typing a full title in the conversation details flyout does not
  reload the list or lose focus/cursor position.
- Conversation row timestamps update live again.
- `dotnet test` passes with zero failures, including the voice cleanup
  test, without a flat sleep as the actual fix.
- `SuggestContextSize`'s doc comment matches its implemented contract,
  whichever direction is chosen.
- `docs/features.md`, the relevant workflow doc, and `CHANGELOG.md`
  reflect every behavior change in the diff.
