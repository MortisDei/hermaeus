# 02. Models as cards

## Why

The Models panel lists twelve models as full-width rows, each an `Expander`
inside a `Border` (`ModelManagementView.axaml:180-190`). The header packs
the name, the raw name, the provider, the size, tags, a fit badge, a tune
summary, an update badge, a "Re-tune recommended" note, an Auto tune button
and a modified date into one line of small grey text
(`:191-247`). At the owner's window width that reads as a wall.

It also does not say where a model came from. `RepoId` is on the row VM
(`ModelManagementViewModel.cs:206-213`, set from the manifest) and is
surfaced only inside the expanded editor, on the label of the "Link to
Hugging Face repo" button. Eight of the owner's twelve models were
downloaded through the app's own Hugging Face browser and nothing on the
list says so.

The owner asked for cards in a wrapping grid, with a Hugging Face mark on
models that came from Hugging Face, explicitly so that a second download
provider would have somewhere to go.

All references verified against `f03e7c1`.

## Work items

### 2.1 A source kind on the row, not a Hugging Face flag

Do not bind a badge directly to `RepoId`. Add a derived source concept so a
second provider is a new enum value rather than a new binding in the
template:

```csharp
public enum ModelSourceKind { Unknown, LocalFile, HuggingFace }
```

On `ModelProfileItemViewModel`, derive it: a non-empty `RepoId` means
`HuggingFace`, otherwise `LocalFile` for `IsLocalGguf`
(`ModelManagementViewModel.cs:1109`), otherwise `Unknown`. It must
recompute when `RepoId` changes, which happens after a repo link
(`:536`) and on every refresh (`:206-213`), so raise the property change
alongside `RepoId`.

Expose `SourceLabel` ("Hugging Face", "Local file") and a boolean for
badge visibility. Keep the enum in `Hermaeus.ViewModels`; this is a
presentation concept and does not belong in `Hermaeus.Core`.

### 2.2 The tile

Replace the `ItemsControl`'s default vertical stacking
(`ModelManagementView.axaml:180-186`) with a `WrapPanel` items panel. The
file already uses this pattern for Hugging Face search results, and
`ChatView.axaml:397-401` and `:523-527` are further precedent.

Each tile is a `Border` card with the app's existing card treatment
(border brush `ControlStrokeColorDefaultBrush`, `CornerRadius="8"`,
padding around 14), a fixed `Width` near 340 and a `Margin` giving an 8-12px
gutter. Fixed width, free height: a `WrapPanel` of variable-width tiles
produces ragged columns.

Tile contents, in reading order:

1. **Name row.** `EffectiveName` at 15px semibold, wrapping to at most two
   lines with `TextTrimming="CharacterEllipsis"`, plus the Running badge
   (`:196-203`, unchanged) and the source badge from 2.3.
2. **Identity line.** `RawName` and `Provider`, faint, one line, trimmed.
3. **Badge row.** `SizeDisplay`, the fit badge (`:210-216`, unchanged
   including its `FitReason` tooltip), the update badge (`:221-227`,
   unchanged), and "Re-tune recommended" (`:228-230`).
4. **Tags.** `TagsDisplay`, faint, trimmed, hidden when empty.
5. **Footer row.** `ModifiedDisplay` faint on the left; on the right the
   Auto tune button (`:233-242`, unchanged binding, still hidden for
   non-local models and disabled while running) and a new icon-only
   "Configure" button that opens 2.4's flyout. Icon-only means a tooltip is
   mandatory; the guard test scans axaml and fails without one.

`TuneSummary` (`:217-220`) does not fit the tile without crowding it. Move
it to the tooltip of the Auto tune button, appended to that button's
existing tooltip text, so no information is lost.

The empty state (no models detected, or none matching the filter) uses
`MossEmptyState`, per CLAUDE.md.

### 2.3 The source badge

A small `Border` badge in the name row, visible only when the source is
known, carrying the source label as its tooltip (for Hugging Face, the
`RepoId` itself, which is more useful than the word "Hugging Face").

Draw the mark as a `PathIcon`, not an image: the app ships no external
image assets for this and adding one for a badge is not worth it. A neutral
glyph plus a two-letter label ("HF") is acceptable and is what makes the
badge extensible; a second provider gets its own glyph and label in the
same slot.

Do not reproduce the Hugging Face logo. It is a third-party trademark, this
repository is going public, and a generic cloud-or-download glyph with an
"HF" label communicates the same thing with none of the question.

### 2.4 The editor moves into a flyout

The expander body is eighteen controls in a three-column grid
(`ModelManagementView.axaml:250-400`): display name, description, tags,
temperature, context size, max tokens, top-p, top-k, min-p, repeat penalty,
frequency penalty, presence penalty, a Visible checkbox, an avatar label,
Save, Reset, Link to Hugging Face repo, and Update. None of that fits a
340px tile.

Move it verbatim into a `Flyout` on the tile's Configure button, with
`Width` around 620 so the three-column grid keeps its current proportions.
Reuse `ModelProfileItemViewModel.IsExpanded` (`:1099`) as the flyout's open
state; it is a plain observable property with no other consumer, so it
carries over cleanly.

**Every control moves. None is dropped, renamed, or given a different
binding.** Verify by diffing the control list before and after; a tile that
silently loses "presence penalty" is a regression that no test will catch.

### 2.5 Keep the parts that are not the list

The header (`:1-15`), the Hugging Face browser expander, the filter box
(`:164-171`) and the status line (`:173-178`) are unchanged. The filter
still narrows the same collection; it now narrows a grid instead of a
column, which needs no code change.

Check `ModelManagementView.axaml.cs` for anything that reaches into the old
visual tree by name. `ModelListScrollViewer` (`:179`) survives; anything
that walks `ItemsControl` children to find a row will not.

## Tests

The tile is XAML and mostly untestable, so test the part that is not:

- `ModelSourceKind` is `HuggingFace` when `RepoId` is set, `LocalFile` for a
  local GGUF with no repo, `Unknown` otherwise.
- Setting `RepoId` after construction raises a property change for the
  source kind and its badge visibility. This is the one that breaks in
  practice, because the badge is populated by an async manifest refresh
  after the row already exists.
- `SourceLabel` is stable text, not a model-written string.

Add the tiles' new icon-only Configure button to whatever the existing
tooltip guard test enumerates, or confirm the guard picks it up
automatically by scanning axaml.

## Explicitly not in this item

- No change to how models are discovered, downloaded, updated, tuned or
  organised. This is presentation.
- No new provider. 2.1 makes room for one; adding one is a future round.
- No sorting or grouping controls. The list order is unchanged.
