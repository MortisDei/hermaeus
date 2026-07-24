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

This silhouette spec (round face, pointed ears, mushroom/leaf tuft, two simple
eyes) was written for the superseded cute-era redesign, not the new
woodland-goblin direction. It's what `Controls/MossIcon.axaml` currently
implements (see below); revisit it alongside future illustration work rather than
assuming it still applies.

For the small in-app accent (not the app icon - see above), do not use the whole
character. Use only:

- round face silhouette
- pointed ears
- a small mushroom/leaf tuft
- two simple eyes
- flat shapes only

Must read clearly at 16x16, 32x32, and desktop icon size.

## Current in-app usage

A simple flat-vector approximation of the icon-scale silhouette above (round face,
ear tuft, mushroom sprout, two eyes) is implemented as `Controls/MossIcon.axaml` in
`Hermaeus.Desktop`, built from plain Avalonia shapes (no new rendering
dependency). It currently appears in exactly two places: next to the RAG
ingest progress row (`Views/RagView.axaml`) and in the Services error banner
(`Views/ServicesView.axaml`), each with a short in-character tooltip.
**This reflects the superseded cute-era
design, not the woodland-goblin direction above** - updating it is follow-up work,
not part of this doc revision.

The window/taskbar icon (`Assets/hermaeus.ico`), the Linux desktop icon
(`Assets/hermaeus-app.png`), the system tray icon (`Assets/hermaeus-tray.png`),
and the unused `hermaeus-tray-dark.png`/`hermaeus-tray-light.png` fallback assets
are all cropped and resized from the retired Archivist's Seal artwork (formerly
`docs/hermaeus-icons.png`, Option 1). Sizes at or below 32px use a contrast-boosted
crop - the fine engraved linework anti-aliases into mud that small, so boosting
contrast keeps the gold "H" legible against the dark medallion. Even boosted,
16x16 is tight; 32px and up read clearly.
