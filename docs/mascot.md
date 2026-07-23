# Mascot: Moss

Hermaeus's mascot. This document is the source of truth for identity, personality,
and visual spec. It exists so future art (illustration, animation, marketing) stays
consistent, and so any in-app use of the character matches the brand intent.

This spec reflects the illustrated brand sheets `docs/hermaeus-branding.png` and
`docs/hermaeus-icons.png`, which superseded the earlier "mechanical tinkerer"
placeholder concept.

## Core identity

**Name:** Moss.

**Role:** Keeper of Knowledge. A small forest-dwelling companion who lives in your
workspace and tends it: organising, remembering, and helping you understand what's
in it.

Moss is **not the AI**. He is the presence behind the scenes who keeps the
workspace tidy and helps you navigate it, not the model doing the reasoning.

## Personality

Traits: curious, diligent, loyal, warm, patient, quietly proud of a well-organised
shelf.

## Brand personality

Moss should communicate:

> "Powerful technology, kept and tended by someone who genuinely cares about your
> workspace."

Not:

> "Magical AI assistant that knows everything."

He is the keeper, not the oracle. Local-first, provider-agnostic, and hands-on are
all traits Hermaeus already has as a product; Moss is the character that carries
that tone into anything visual.

## Visual style

Overall: cute but capable. Not Disney cute, not childish. Closer to Tux, the
GitHub Octocat, or a Studio Ghibli forest spirit. A character developers would put
on stickers.

### Appearance

- Small, round stature, big expressive eyes, pointed ears.
- Moss/lichen-covered green skin, small mushroom caps and a leaf sprout growing
  from the head as a natural "hairstyle."
- Face: intelligent eyes, gentle expression, focused concentration when working.
- Avoid: goblin/monster framing, sharp teeth, anything mechanical (goggles, gears,
  cables) - that was the retired tinkerer concept. He should look like a forest
  creature you'd trust with your notes.

### Signature items

Per the illustrated sheet's pose set: a battered leather-bound book, a small
lantern, a mug, and (for "working" poses) a laptop. No tool belt, no GPU backpack -
those belonged to the retired mechanical-tinkerer concept.

### Expressions and poses (reference set)

From `docs/hermaeus-branding.png`, for consistency in future illustration:

- **Expressions:** Curious, Thinking, Happy, Helping, Sleepy.
- **Poses:** Reading, Explaining, Working, Celebrating, Resting.

## Brand colour palette

Formalised in `docs/hermaeus-branding.png`. Used for illustration and, where noted
below, pulled into the app's theme resources.

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
applies the choice live. `docs/hermaeus-branding.png` still documents the
original brand palette and typeface intent for reference.

## App icon vs. mascot icon

`docs/hermaeus-icons.png` presents four app-icon concepts: Archivist's Seal, Moss
(the full character face), Wax Seal, and Tree Ring. **Archivist's Seal was
chosen** for the actual window/taskbar/tray icon - a gold "H" monogram grown
through with a small tree and an open book, set in a circular medallion. It
reads as a mark, not a character. (Tree Ring was tried first but read worse at
tray/taskbar size; both options fight the same problem at 16x16 - fine detail
disappears at that size regardless of which mark is used.)

The full Moss character (Option 2) is not wired into the app icon. It remains
available for future use (About screen, onboarding, marketing) but needs real
illustration work to use anywhere in-app beyond the icon-scale silhouette below.

## Icon-scale silhouette (in-app accent use)

For the small in-app accent (not the app icon - see above), do not use the whole
character. Use only:

- round face silhouette
- pointed ears
- a small mushroom/leaf tuft
- two simple eyes
- flat shapes only

Must read clearly at 16x16, 32x32, and desktop icon size.

## Current in-app usage

A simple flat-vector approximation of the icon-scale silhouette (round face, ear
tuft, mushroom sprout, two eyes) is implemented as `Controls/MossIcon.axaml` in
`Hermaeus.Desktop`, built from plain Avalonia shapes (no new rendering
dependency). It appears as a small accent next to the agent task busy indicator
and the shared error/status banner. This is a placeholder built to the icon spec
above, not final illustrated art - replace with real illustration once one exists,
keeping the same 16x16 silhouette rules.

The window/taskbar icon (`Assets/hermaeus.ico`), the Linux desktop icon
(`Assets/hermaeus-app.png`), the system tray icon (`Assets/hermaeus-tray.png`),
and the unused `hermaeus-tray-dark.png`/`hermaeus-tray-light.png` fallback assets
are all cropped and resized from the Archivist's Seal artwork in
`docs/hermaeus-icons.png` (Option 1). Sizes at or below 32px use a contrast-boosted
crop - the fine engraved linework anti-aliases into mud that small, so boosting
contrast keeps the gold "H" legible against the dark medallion. Even boosted,
16x16 is tight; 32px and up read clearly.
