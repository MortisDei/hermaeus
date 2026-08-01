# 01. Controls that do what they look like

## Why

Five separate defects, grouped because they share a symptom rather than a
cause: in each one the app renders a control that implies a capability it
does not deliver. Land them as five commits. Each is independently
descopable.

All references verified against `f03e7c1`.

---

## 1.1 The Services page can save

### The defect

`ServicesView.axaml:598-599` hosts two cards:

```xml
<views:ServicesVoiceSectionView DataContext="{Binding Tts}" />
<views:ServicesSttSectionView DataContext="{Binding Stt}" ... />
```

`ServicesViewModel.Tts` and `.Stt` (`ServicesViewModel.cs:1382`, `:1385`)
are the DI singletons shared with `SettingsViewModel`
(`App.axaml.cs:226-229`). Editing Base URL, Voice, Device, Speed, Preload,
Output directory or any XTTS/Python path on the Services page mutates those
singletons and nothing else. The only code that persists them is
`SettingsViewModel.ApplyTtsTo` (`SettingsViewModel.cs:305-321`), called from
`SettingsViewModel.SaveAsync` (`:218`), reached from the single Save button
on the Settings page (`SettingsView.axaml:19`).

The page teaches the opposite. Runtime Profiles has a per-row Save
(`ServicesView.axaml:102`), each managed server has Save Config (`:530`),
and the page header says "Paths and config are saved per server" (`:18`).
A user who has just used two working Save buttons has every reason to
believe the third card saves too.

The STT card has the same defect and is not visible in the owner's
screenshot; fix both.

### The change

Add a Save button to the Services page header, in the `Grid` at
`ServicesView.axaml:16-18`, laid out and captioned exactly like
`SettingsView.axaml:19` including the transient "Saved" confirmation
(`SettingsView.axaml:44`). It saves through the one existing flow: no
second serializer, no partial write, no direct `settings.json` access.

Wire it as a settable delegate rather than a constructor dependency:

```csharp
// ServicesViewModel
/// <summary>Set by the DI root to the single settings save flow
/// (SettingsViewModel.SaveAsync). Null in tests that do not exercise saving.</summary>
public Func<Task>? SaveAllSettings { get; set; }

[RelayCommand]
private async Task SaveSettingsAsync()
{
    if (SaveAllSettings is null) return;
    await SaveAllSettings();
    IsSaved = true;
    _ = ResetIsSavedAfterDelayAsync();
}
```

`ModelManagementViewModel.RequestRepoIdInput`
(`ModelManagementViewModel.cs:504`) is the existing precedent for a
settable delegate wired at the DI root. Use it. Do **not** inject
`SettingsViewModel` into `ServicesViewModel`: there is no cycle today
(`SettingsViewModel`'s constructor does not take `ServicesViewModel`), but a
2000-line VM is the wrong dependency for one button, and the delegate keeps
`SettingsViewModel` resolved lazily.

### Why the whole-settings save and not a Tts-only one

`SettingsViewModel.SaveAsync` applies every section onto a **clone** of the
live settings and only swaps it in on success (`:207-216`, `:220`). Saving
from Services therefore behaves identically to saving from Settings, which
is what a user pressing a button labelled "Save" expects. A Services-only
save path that wrote just `Tts` and `Stt` would be a second flow with its
own validation story, and CLAUDE.md's rule is one save flow.

**Warning.** This makes the Services page capable of persisting the whole
settings object. Every section VM must therefore hold current state when
the button is pressed. `SettingsViewModel.Reload()` (`:188-205`) is what
guarantees that, and it runs in the constructor. If the implementer finds
any navigation path that leaves a section VM stale, that is a bug worth
fixing here and not worth working around: a stale section saved from
Services would overwrite real settings with blanks.

### Tests

- `ServicesViewModel` with `SaveAllSettings` set: invoking
  `SaveSettingsCommand` calls the delegate exactly once and sets `IsSaved`.
- A voice field edited through `ServicesViewModel.Tts` and then saved
  through the real `SettingsViewModel.SaveAsync` reaches
  `ISettingsService.Settings.Tts`. This is the regression that would have
  caught the defect; assert on the persisted value, not on the VM property.
- `SaveAllSettings` unset (the test default) does not throw.

---

## 1.2 A voice picker that looks and behaves like a picker

### The defect

Two problems compound.

**It does not look like a picker.**
`SettingsVoiceSectionView.axaml:34-39` uses `AutoCompleteBox`:

```xml
<AutoCompleteBox ItemsSource="{Binding $parent[ItemsControl].DataContext.ChannelVoiceOptions}"
                 Text="{Binding VoiceDisplay, Mode=TwoWay}"
                 FilterMode="Contains" ... />
```

Avalonia's `AutoCompleteBox` renders as a bare `TextBox`: no chevron, no
click-to-open, and with the default `MinimumPrefixLength` it shows nothing
until the user types. The control's comment (`:27-33`) explains the choice
correctly (Avalonia 11 has no free-text `ComboBox`, and providers that
cannot enumerate voices still need typed input) and that reasoning stands.
What is missing is the affordance.

**It usually has nothing in it.** `ChannelVoiceOptions`
(`TtsSettingsViewModel.cs:70`, rebuilt at `:207-212`) is the sentinel plus
whatever is in `TtsVoices`. `TtsVoices` is initialised to `["default"]`
(`:111`) and only replaced by `RefreshTtsVoicesAsync` (`:415-433`), which
is fired and forgotten at construction (`:322`) and, when the voice service
is not running, shows a warning toast and leaves the initial list in place
(`:429-432`). So the common state of the picker is two entries, "(Default
voice)" and "default", which are not obviously different from each other.

### The change

Keep `AutoCompleteBox`. Give it the affordance and an honest empty state.

1. `MinimumPrefixLength="0"` so focusing the control shows the full list
   rather than requiring a guess at the first character.
2. A chevron `PathIcon` inside the control's right edge, or a `Button`
   adjacent to it that sets `IsDropDownOpen`, whichever reads better against
   the existing card. Icon-only means a tooltip is required (guard test).
3. When `ChannelVoiceOptions` holds only the sentinel, or only the sentinel
   and the literal `"default"`, replace the row's helper text with a factual
   line naming the reason and the fix: the voice service has not listed its
   voices, and Services > Voice > Refresh is where that happens. Add
   `ChannelVoiceOptionsAreProviderSupplied` (or similarly named) to
   `TtsSettingsViewModel` for the visibility binding. Do not attribute this
   copy to Moss; it is a factual state, and `docs/mascot.md` says drop the
   personality when in doubt.

Do not delete `"default"` from `TtsVoices`' initial value without checking
`ServicesVoiceSectionView.axaml:46`, which binds the same collection as the
global voice `ComboBox`; the Services page relies on having at least one
entry.

### Tests

- `ChannelVoiceOptions` tracks `TtsVoices`: after a refresh populates three
  provider voices, the options list is the sentinel plus those three, in
  order, with no duplicate sentinel.
- `ChannelVoiceOptionsAreProviderSupplied` is false for the initial
  `["default"]` state and true once a refresh supplies real voices.
- `VoiceChannelSettingViewModel.VoiceDisplay` still round-trips the sentinel
  to `string.Empty` and back (`TtsSettingsViewModel.cs:37-41`); this is
  existing behaviour and the change must not disturb it.

---

## 1.3 The chat action row is reachable

### The defect

The transcript `ScrollViewer` (`ChatView.axaml:443-446`) carries
`Padding="16,8,16,28"`. Each assistant message's copy and read-aloud
buttons sit in a `Grid` **outside** the message border
(`MessageControl.axaml:255-296`), deliberately, so that r19 6.3's
stick-to-bottom arithmetic is unaffected (`:135-139`). On the last message
in a conversation those buttons end up flush against the input bar and
cannot reliably be clicked.

`ScrollViewer.Padding` in Avalonia 11.3 is template-bound to
`ScrollContentPresenter`, and whether it contributes to the scrollable
extent is a detail of that control, not something this app should depend
on. It is currently not producing usable space.

### The change

Stop relying on the `ScrollViewer`'s padding for the bottom gap and put the
space inside the scrolled content, where it is unambiguously part of the
extent:

- Reduce the `ScrollViewer`'s bottom padding to match the top (`16,8,16,8`).
- Add a trailing spacer of at least 32px as the last child of the
  `StackPanel` at `ChatView.axaml:447`, after the `ItemsControl`. A
  zero-opacity `Border` with a fixed `Height` is enough; give it a comment
  saying why it exists so a future round does not tidy it away.

While in `MessageControl.axaml`, raise the action buttons' hit target. They
are `Classes="icon-btn"` with `Padding="6"` around a 12-13px icon
(`AppStyles.axaml:116-121`), giving roughly a 24px square. Set a `MinWidth`
and `MinHeight` of 28 on that row's buttons only, scoped in
`MessageControl.axaml`, not on `.icon-btn` globally.

### Verification

This one is visual and must be confirmed in the running app, not only by
test. Follow the `build-and-verify` skill's rules for a manual run: the dev
run shares the owner's real `settings.json`, so do not resave settings and
do not force-kill the process.

### Tests

A layout assertion is not available here. Add a guard instead: a test that
reads `ChatView.axaml` and fails if the trailing spacer element is absent,
in the style of the existing axaml-scanning guards. State plainly in the
test's comment that it pins the presence of the fix, not the pixel result.

---

## 1.4 Ctrl+Enter works in both directions

### The defect

`Ui.CtrlEnterToSend` defaults to false (`UiSettings.cs:17`) and is presented
as "Ctrl+Enter to send (Enter inserts newline)"
(`SettingsUiSectionView.axaml:31`). The unchecked state therefore means
"Enter sends, Ctrl+Enter inserts a newline". The app implements only the
first half of that.

`ApplyAcceptsReturn` (`ChatView.axaml.cs:270-275`):

```csharp
input.AcceptsReturn = _vm.Settings.Settings.Ui.CtrlEnterToSend;
```

`OnInputKeyDown` (`:277-288`):

```csharp
var sendModifier = ctrlEnter ? KeyModifiers.Control : KeyModifiers.None;
if (e.Key == Key.Return && e.KeyModifiers == sendModifier) { ...send... }
```

With the setting false: `AcceptsReturn` is false, so the `TextBox` will not
insert a newline for any key combination, and `OnInputKeyDown` ignores
Ctrl+Enter. There is no way to enter a newline in the chat box in the app's
default configuration. The v0.24.x fix that made `AcceptsReturn` dynamic
(`docs/changelog-archive.md:717-723`) fixed the send half correctly and
left this half unwritten.

### The change

Own both combinations explicitly instead of delegating one of them to
`AcceptsReturn`.

- `AcceptsReturn` is `true` always. Remove `ApplyAcceptsReturn` and its
  `SettingsChanged` subscription, and update the comment at
  `AppStyles.axaml:105-108` which documents the old mechanism.
- Handle the key in a **tunnelling** handler so the app sees Enter before
  `TextBox`'s own class handler consumes it. This is the mechanism the
  original fix worked around rather than used:

```csharp
// In OnAttachedToVisualTree / control initialisation:
input.AddHandler(InputElement.KeyDownEvent, OnInputKeyDown,
                 RoutingStrategies.Tunnel);
```

Remove `KeyDown="OnInputKeyDown"` from `ChatView.axaml:668` so the handler
is registered once, in code, with the tunnelling strategy.

- In the handler, with `ctrlEnter` read as today:
  - send combination (`Control` when `ctrlEnter`, `None` otherwise): send,
    `e.Handled = true`.
  - newline combination (the other one): insert `Environment.NewLine` at
    the caret, move the caret past it, `e.Handled = true`. Reuse the caret
    arithmetic in `InsertDictatedTextAtCursor` (`:294-309`) rather than
    writing a second version.
  - Shift+Enter keeps inserting a newline in both modes, which is what
    every chat client does and what `AcceptsReturn="true"` gives for free
    once the handler stops swallowing it.

### Tests

The key handling lives in code-behind, which the test project does not
reach. Extract the decision into a pure, testable function in
`Hermaeus.ViewModels` (a `ChatInputKeyAction` enum returning `Send`,
`Newline` or `Pass`, taking the key, the modifiers and the setting), have
the code-behind call it, and test the function:

- `CtrlEnterToSend` false: Enter is `Send`, Ctrl+Enter is `Newline`,
  Shift+Enter is `Newline`, any other key is `Pass`.
- `CtrlEnterToSend` true: Ctrl+Enter is `Send`, Enter is `Newline`,
  Shift+Enter is `Newline`.
- Neither setting produces a state where no combination yields `Newline`.
  Write that one as its own named test; it is the invariant that was
  broken.

---

## 1.5 The cursor flicker: confirm the mechanism before fixing it

### What is known

Two fixes have landed against theorised causes and the flicker survives
both:

- Tooltips are placed below their control (`AppStyles.axaml:34-37`) with
  `VerticalOffset` 6, because a tooltip that opens under the pointer makes
  the control count as exited.
- The nav row is one continuous hand-cursor region
  (`MainWindow.axaml:52-54`, `Spacing="0"` plus `Cursor="Hand"` on the
  panel), because the gap between buttons was a dead strip.

The owner now reports the flicker occurs **on the very outer edge of each
button**.

### The leading hypothesis, and it is a hypothesis

That location is where the tooltip popup is closest to the pointer. The
popup opens 6px below the button's bottom edge; a pointer resting on or
jittering at that edge can land on the popup, which is a separate top-level
that is not the button. The control counts as exited, the tooltip closes,
the pointer is over the button again, and the loop restarts. The 6px offset
is small enough that ordinary hand tremor crosses it.

This explains why two cursor-region fixes did not help: the cursor region
was never the problem, the popup stealing pointer-over was.

### Required first step: confirm it

**Do not edit anything until the mechanism is observed.** Run the app and
determine, at a button's edge where the flicker occurs:

1. Does the flicker still happen with `ToolTip.Tip` removed from that one
   button? If it stops, the tooltip is the mechanism and the fix below is
   right.
2. If it still flickers with no tooltip, the mechanism is something else and
   this item is descoped to a written finding in the roadmap, not a third
   speculative fix.

Record what was observed in the commit message. That record is the point of
this item even if no code changes.

### The fix, if the tooltip is confirmed

Make the tooltip popup unable to take pointer-over at all, rather than
moving it further away:

```xml
<Style Selector="ToolTip">
    <Setter Property="IsHitTestVisible" Value="False" />
    ...existing setters...
</Style>
```

A tooltip is not interactive; nothing in this app has a clickable tooltip.
With hit-testing off, the pointer passes through the popup to whatever is
underneath, the control never counts as exited, and the loop cannot form
regardless of geometry. Raise `ToolTip.VerticalOffset` to 10 as well, so
the popup is not visually touching the control it describes.

Add the reasoning to the existing comment block at `AppStyles.axaml:26-33`
rather than starting a new one; that comment is the history of this bug and
should stay in one place.

### Tests

None available: this is pointer behaviour in a live visual tree. The
verification is the observation recorded in step 1. Do not write a test that
asserts the style setter exists; a style guard would pass forever without
telling anyone whether the flicker stopped.
