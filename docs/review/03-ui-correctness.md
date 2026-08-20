# 03. Controls that respond to the user

## 3.1 Finish the cursor-gap sweep

r29 measured the mechanism: transparent rounded icon buttons leave tiny pixels
where hit testing falls through to the window root, so the cursor alternates
between hand and arrow. `IconBarCursor` fixes only a button whose visual parent
is a Panel containing buttons and nothing else. That deliberate rule excludes
every newly reported location.

Apply `Background="Transparent"` and `Cursor="Hand"` to the smallest container
that fills only the dead pixels around these actions:

- sidebar toggle (`MainWindow.axaml:33-40`);
- Projects switcher trigger (`ProjectSwitcherView.axaml:13-21`);
- message branch/edit action and copy/read-aloud/stop rows
  (`MessageControl.axaml`, including the mixed ContentControl row at :266);
- regenerate in the composer row (`ChatView.axaml:712-719`).

Do not set Hand on a whole toolbar containing text boxes, labels, or menus. If
no suitable existing container exists, add a tight transparent Border or Panel
around the one button or button group, behind its children.

Acceptance criteria:

- pointer movement across every named outer edge remains a hand in a live run;
- adjacent text/input pixels keep their normal cursor;
- hover, click, tooltip, and keyboard focus still reach the button;
- extend the existing cursor guard to name these mixed-content manual sites.

## 3.2 Open ComboBox lists scroll with the wheel

The existing `WheelScrollHelper` is not a general dropdown solution. It is a
tunnel handler on three page roots that deliberately drives the outer page
ScrollViewer when a NumericUpDown consumes wheel input. A ComboBox popup is a
separate popup visual tree, so that handler neither proves nor fixes scrolling
inside the open list.

Reproduce on a list long enough to show its own scrollbar in Models, Services,
Settings, Chat, Benchmark, Agent, and conversation filters. Log the routed
source and handled state once. Then install one shared Desktop behavior at the
ComboBox/popup boundary that moves the open popup's own ScrollViewer and marks
the event handled only when that viewer can move in the requested direction.
At its top or bottom, leave the event available rather than scrolling an outer
page behind an open popup. Closed ComboBoxes retain existing page-wheel behavior.

Do not walk private template child indexes or copy a handler into every view.
Use named template parts, visual-ancestor lookup by type, or an Avalonia class
handler that survives theme changes.

Acceptance criteria:

- every long open ComboBox list scrolls by wheel on Windows and Linux;
- the selected value does not change merely from wheel input over an open list;
- the outer page does not move while the popup can move;
- short lists and closed ComboBoxes behave as before;
- one pure offset/edge helper is unit tested; popup routing is verified live.

## 3.3 Memories pin and delete execute

The commands exist and take a memory id
(`MemoriesViewModel.cs:242-279`). XAML supplies `CommandParameter="{Binding Id}"`
and reaches the view by element name (`MemoriesView.axaml:142-159`). Reproduce
with a real item and inspect binding diagnostics plus `CanExecute`. Do not add a
second command or style enabled buttons to look active.

Fix the actual boundary, expected to be the compiled DataTemplate command
source if the live binding resolves null. Prefer the repository's established
`$parent[UserControl]` or named-root pattern that binding tests already cover.
Add confirmation before permanent delete; the current command deletes
immediately despite its tooltip saying the action cannot be undone.

Acceptance criteria:

- pin toggles persisted `IsPinned` and the label without a panel refresh;
- delete asks for confirmation, cancel changes nothing, confirm removes the DB
  row and card;
- both controls are enabled for a valid loaded item and disabled only while
  their own async operation is running;
- storage failure leaves the card truthful and shows the existing error toast;
- tests exercise the command with the exact item/parameter shape the view uses.

## 3.4 Nullable numeric fields start at their neutral value

Avalonia's nullable NumericUpDown moves null to Minimum on the first increment.
That makes repeat penalty start at 0.50 instead of neutral 1.0 and frequency and
presence penalties start at -2.0 instead of neutral 0.0.

Add one small reusable neutral-on-first-spin behavior for nullable numeric
overrides. Apply it to all three surfaces that expose these fields: Settings LLM
defaults, Chat sampling, and Models per-model sampling. Audit nullable Top P,
Top K, and Min P in the same controls and define their first-spin baseline as
their provider-neutral/documented default, not automatically Minimum. Leaving
the field untouched remains null and continues to mean provider default.

Required neutral baselines:

| Field | First increment starts from |
| --- | ---: |
| Repeat penalty | 1.0 |
| Frequency penalty | 0.0 |
| Presence penalty | 0.0 |
| Top P | 1.0 |
| Top K | the documented provider default used by that surface |
| Min P | 0.0 |

If no stable Top K provider default exists across supported providers, leave
Top K unchanged and record that decision in the PR rather than inventing one.

Acceptance criteria:

- the first up/down action for the three reported penalties begins at neutral;
- untouched null values still serialize as null;
- keyboard, wheel, and spinner-button first edits use the same baseline;
- Settings, Chat, and Models do not drift to different behavior;
- pure behavior tests cover null, existing value, min/max clamp, and decrement.

## 3.5 Chat has one Export action

Replace the two adjacent toolbar buttons at `ChatView.axaml:260-279` with one
Export button and a `MenuFlyout` containing Markdown and JSON. Keep the existing
commands and file-picker/export service. The main action has tooltip `Export
conversation`; each menu item names its format. Do the same for any other place
where Markdown and JSON appear as two adjacent buttons, but keep context-menu
items that are already format choices inside one menu.

Acceptance criteria:

- one toolbar action exposes both formats;
- Markdown and JSON use the same filename, cancellation, and success/error paths
  as before;
- keyboard and screen-reader users can open and choose the menu;
- export service regression tests remain unchanged.

## Deferred from this theme

Audio feedback is not implemented. It is recorded in `deferred.md` for r31 and
must return with an event list, mute/accessibility policy, volume behavior, and
proof that it reuses an existing playback path without competing with TTS.

## Tests and documentation

Budget 10 to 14 tests plus live Windows/Linux pointer checks. Update
`docs/features.md` and `CHANGELOG.md`. No workflow doc is needed for cursor-only
layout, but Memories deletion confirmation and export behavior must be stated in
the feature inventory.
