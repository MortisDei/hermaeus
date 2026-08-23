# Mascot: Moss

Hermaeus's mascot. This document is the source of truth for identity, personality,
and visual spec. It exists so future art (illustration, animation, marketing) stays
consistent, and so any in-app use of the character matches the brand intent.

This spec reflects `docs/hermaeus-branding.md`, which replaced the earlier
illustrated brand sheets (`docs/hermaeus-branding.png`, `docs/hermaeus-icons.png` -
both removed from the repo). The new spec revises Moss's appearance: a weathered
**woodland goblin**, not the softer "cute Studio Ghibli forest spirit" redesign the
character had under the old illustrated sheets. That superseded, cute-era design is
still what's actually implemented in-app today (see "Current in-app usage" below) -
bringing the shipped art in line with this spec is follow-up work, not done as part
of this doc update.

## Core identity

**Name:** Moss.

**Purpose:** Represents accumulated knowledge and patient guidance. He is not comic
relief.

Moss is **not the AI**. He is the presence behind the scenes who keeps the
workspace tidy and helps you navigate it, not the model doing the reasoning.

## Personality

Traits: experienced, thoughtful, observant, patient, quietly humorous, dependable.

He teaches. He does not entertain.

## Voice in UI copy

Any text attributed to Moss (tooltips on his icon, empty-state hints, tips)
follows these rules:

- One sentence, two at most. He is brief because he is busy, not curt.
- He states what is true and what to do next, in that order. "No datasets
  yet. Ingest a folder and Moss will keep it indexed." Never a bare "Nothing
  here!"
- Dry, understated humour is allowed roughly one line in five, and only
  where nothing is wrong. Error and warning surfaces get calm competence
  ("Right. Let's see what broke."), never jokes about the failure.
- No exclamation marks. No emoji. No "magic", "wizardry", or AI-mystique
  wording. No addressing the user as "friend" or similar.
- Moss says "Moss", not "I", when the line names its speaker at all; most
  lines should not.
- He never speaks for the model. Answers, refusals, and mistakes belong to
  the model or the app, not to Moss.
- Setup guidance follows the same contract: one factual next step, what can be
  skipped, and no claims that a provider, model, or runtime is ready before it
  has been verified.

## Brand personality

Moss should communicate:

> "Powerful technology, kept and tended by someone who genuinely cares about your workspace."

Not:

> "Magical AI assistant that knows everything."

He is the keeper, not the oracle. Local-first, provider-agnostic, and hands-on are
all traits Hermaeus already has as a product; Moss is the character that carries
that tone into anything visual.

## Visual style

Small woodland goblin. Weathered rather than cute.

### Appearance

- moss-covered clothing
- practical satchel
- notebooks
- keys
- scrolls
- walking staff
- lantern
- spectacles (optional)

Avoid: comic/monster framing, anything that reads as gaming or fantasy-crest
iconography - see `docs/hermaeus-branding.md`'s brand-wide "avoid" list (magic,
sci-fi, "artificial intelligence," gaming).

### Expression

Calm. Curious. Knowing. Never goofy.

## Behaviour

Suitable locations: onboarding, empty states, documentation, release notes,
achievements, tips.

Avoid: constant popups, excessive animation, interrupting workflows.

## Brand colour palette

`docs/hermaeus-branding.md` states the palette at brand level (Primary: Moss Green,
Deep Moss, Slate; Accent: Brass, Warm Gold, Parchment - "accent colours should
remain accents"). The table below is the fuller palette actually wired into the
app's theme resources (`App.axaml`); treat it as the implementation detail behind
that brand-level statement.

| Name | Hex | Use |
| --- | --- | --- |
| Deep Moss | `#2E3D2B` | darkest surface / ink-adjacent accents |
| Forest | `#436B3F` | primary brand green |
| Sage | `#7A8F6A` | secondary/lighter green |
| Parchment | `#E8DFC6` | warm light surface / text-on-dark |
| Ink | `#1A1D18` | near-black text/outline |
| Copper | `#B87333` | primary accent (buttons, highlights) |
| Amber | `#D19A42` | secondary accent / hover states |
| Teal | `#2F6F6D` | tertiary accent |
| Indigo | `#3D4A6B` | tertiary accent |
| Berry | `#7B3D5A` | tertiary accent |

## Typography

r21: the three embedded brand typefaces (Cinzel, Source Sans 3, JetBrains
Mono) were removed after they proved hard to read in daily chat use, sizing
aside. The UI now defaults to the OS-native font for headings/body text
(`Segoe UI,sans-serif`) and code/technical text (`Consolas,monospace`), set
in `App.axaml` and wired through `Styles/AppStyles.axaml`. Settings >
Interface > Typography lets the user override each of the three roles
(heading, body, code) with their own installed font; `AppFontService`
applies the choice live. `docs/hermaeus-branding.md` still documents the
brand palette and typeface intent for reference.

## App icon vs. mascot icon

The previously-shipped app icon (window/taskbar/tray) is the **Archivist's Seal**:
a gold "H" monogram grown through with a small tree and an open book, set in a
circular medallion. It reads as a mark, not a character, and remains the shipped
icon today - unaffected by the mascot direction change above. (Tree Ring was tried
first but read worse at tray/taskbar size; both options fight the same problem at
16x16 - fine detail disappears at that size regardless of which mark is used.)

The illustrated sheet these concepts came from (`docs/hermaeus-icons.png`) has been
removed from the repo with no replacement image; `docs/hermaeus-branding.md` now
documents logo motifs and app-icon requirements at the text/spec level only. No
illustrated Moss artwork currently exists matching the new woodland-goblin
appearance above - any future "full character" illustration (About screen,
onboarding, marketing) needs to start fresh from this spec, not from the retired
cute-era sheet.

## Icon-scale silhouette (in-app accent use)

Revised for the woodland-goblin direction: a hood, not a round cute face. For
the small in-app accent (not the app icon - see above), do not use the whole
character. Use only:

- a pointed, weathered hood silhouette (not bare skin/hair)
- pointed ears peeking out at the sides
- a small brass spectacles band across the eyes (the "spectacles (optional)"
  appearance trait, made non-optional at icon scale as the clearest way to
  read "knowing" rather than "cute")
- two simple, calm eyes
- flat shapes only

There is deliberately **no** free-floating accent dot. An earlier revision
carried a small amber "lantern glow" beside the head, standing in for the
retired mushroom/leaf tuft. With no lantern drawn to attach it to, it read as
an unexplained orange ball, which is exactly how the owner described it. If
the lantern comes back, it comes back as a lantern.

Must read clearly at 16x16, 32x32, and desktop icon size.

## Current in-app usage

A flat-vector approximation of the icon-scale silhouette above (pointed hood,
ears, brass spectacles band) is implemented as `Controls/MossIcon.axaml` in
`Hermaeus.Desktop`, built from plain Avalonia shapes (no new rendering
dependency). This now matches the woodland-goblin direction described above,
not the retired cute-era design.

**Moss is also the application and tray icon** as of 0.36.0-alpha, replacing
the Archivist's Seal monogram. The raster assets in
`src/Hermaeus.Desktop/Assets` are generated by `scripts/generate-icons.ps1`
from the palette above, so they are reproducible rather than being opaque
binaries nobody can regenerate.

The generator is a **shape-for-shape transcription of `MossIcon.axaml`**, not a
second drawing of the same character. The first attempt was the latter, a
redraw "at icon scale" with a wide hood and round spectacles, and it read as a
cowboy: recognisably not the Moss the app shows everywhere else. One mascot
means one geometry, and the only way that survives future edits is for the
generator to follow the control.

The icons are full bleed. The dark rounded field the redraw sat on cost about a
fifth of the width on every edge, so Moss rendered visibly smaller than
neighbouring taskbar icons, and Ink on a near-black taskbar was never a
background anyway.

That generator output is a programmatic render of a glyph designed at 16px, not
commissioned art. It is legible at 16px, which is the bar the Tree Ring mark
failed, and at 256px it looks like what it is. A designed replacement would be
an improvement and can drop straight into the same filenames; if it also
replaces `MossIcon.axaml`, the two must move together.

r22 spread that same icon to every place `docs/mascot.md` sanctions (empty
states, onboarding, tips) via one shared control,
`Controls/MossEmptyState.axaml`:

- Empty states: Chat ("Start a conversation" and "No chat model is set up
  yet"), Agent ("No tasks yet"), Benchmark ("No runs yet"), Memories ("No
  memories yet"), and RAG ("No datasets yet") - each with a short in-voice
  hint, no action buttons baked into the control itself.
- The first-run setup wizard header (`Views/SetupWizardView.axaml`) gets a
  small greeting icon with an in-voice tooltip; the individual wizard steps
  do not.
- The two pre-existing placements stay as they were: the RAG ingest progress
  row (`Views/RagView.axaml`) and the Services error banner
  (`Views/ServicesView.axaml`), each with its own short in-character
  tooltip.

Still no animation, no timed tips, no popups, and no Moss in the chat
transcript; he never appears to be answering for the model.

The window/taskbar icon (`Assets/hermaeus.ico`), the Linux desktop icon
(`Assets/hermaeus-app.png`), the system tray icon (`Assets/hermaeus-tray.png`)
and the `hermaeus-tray-dark.png`/`hermaeus-tray-light.png` fallbacks are all
written by `scripts/generate-icons.ps1`. Regenerate them from there; do not
hand-edit the PNGs, or the next run of the script silently reverts the edit.

Sizes below 128px are rendered at 8x and downsampled, because Moss's catchlights
are less than a pixel at tray size and drawing them directly gives a smear
rather than a highlight. The catchlights are dropped entirely below 32px. The
`.ico` carries 16, 24, 32, 48, 64, 128 and 256px frames.
