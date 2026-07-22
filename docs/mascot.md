# Mascot: Moss

Hermaeus's mascot. This document is the source of truth for identity, personality,
and visual spec. It exists so future art (illustration, animation, marketing) stays
consistent, and so any in-app use of the character matches the brand intent.

## Core identity

**Name:** Moss.

**Role:** A small mechanical tinkerer who maintains the AI workshop.

Moss is **not the AI**. He is the builder, maintainer, and curious presence behind
the scenes making everything work: part goblin, part engineer, part sysadmin, part
hardware gremlin.

## Personality

Traits: curious, clever, slightly chaotic, helpful, resourceful, proud of his work,
hates waste, loves tinkering.

He is the guy who has 17 cables in a drawer, 4 GPUs on a bench, three unfinished
experiments, a perfectly organised database, and absolutely no idea where his
coffee went.

## Brand personality

Moss should communicate:

> "Powerful technology, built by someone who actually enjoys understanding how it
> works."

Not:

> "Magical AI assistant that knows everything."

He is the workshop, not the oracle. Local-first, provider-agnostic, and hands-on
are all traits Hermaeus already has as a product; Moss is the character that carries
that tone into anything visual.

## Visual style

Overall: cute but capable. Not Disney cute, not childish. Closer to Tux, the
GitHub Octocat, Wall-E, or a fantasy workshop mechanic. A character developers
would put on stickers.

### Appearance

- Small stature, large head, slightly oversized hands, built for working on tiny
  components.
- Colour palette: moss green skin, charcoal grey clothing, copper/brass mechanical
  accents, warm amber highlights.
- Face: intelligent eyes, slightly mischievous grin, focused concentration.
- Avoid: evil goblin, fantasy monster, sharp/scary teeth. He should look like
  someone you'd trust with your server.

### Clothing

Workshop hoodie/jacket with utility pockets, tools hanging from a belt, circuit
board patches, tiny SSDs and cables as accessories. Optional: a small badge with
the Hermaeus logo.

### Signature items

1. **Oversized tool belt** - screwdriver, USB cable, soldering iron, tiny wrench,
   USB stick collection.
2. **GPU backpack** - cooling fans, glowing status lights, cables sticking out. A
   joke reference to local AI hardware.
3. **Notebook** - small, battered, full of model benchmarks, architecture
   diagrams, and weird experiments.

## Animation ideas (future / marketing use)

These are concept notes for future illustration or motion work, not implemented
in-app yet unless noted below.

- **Idle:** tightening a screw, checking logs, drinking coffee, looking confused
  at an error message, poking a GPU fan.
- **Loading ("Working..."):** Moss pushes a giant glowing core into place.
- **Indexing documents:** Moss stacks books/documents into organised piles.
- **Model download:** Moss drags a ridiculously oversized hard drive.
- **Error state:** Moss in safety glasses, surrounded by sparks. Not angry - just
  "Right. Let's see what broke."

## Icon version

For app-icon-scale use, do not use the whole character. Use only:

- goblin face silhouette
- goggles
- one glowing eye
- a small tuft of hair/ears
- simple, flat shapes

Must read clearly at 16x16, 32x32, and desktop icon size.

## Current in-app usage

A simple flat-vector approximation of the icon version (goggles, glowing eye,
ear tuft) is implemented as `Controls/MossIcon.axaml` in `Hermaeus.Desktop`, built
from plain Avalonia shapes (no new rendering dependency). It appears as a small
accent next to the two states in the concept notes that map cleanly to existing UI:
the agent task busy indicator and the shared error/status banner. This is a
placeholder built to the icon spec above, not final illustrated art - replace with
real illustration once one exists, keeping the same 16x16 silhouette rules.
