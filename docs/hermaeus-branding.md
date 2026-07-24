# Brand Specification

## Goal

Hermaeus presents itself as a premium local-first AI workspace.

The brand should communicate:

- Knowledge
- Trust
- Craftsmanship
- Privacy
- Quiet confidence

Not:

- Magic
- Sci-fi
- "Artificial intelligence"
- Gaming

## Visual style

Primary inspiration:

- Libraries
- Manuscripts
- Archives
- Botanical collections
- Natural materials
- High quality desktop software

Avoid futuristic themes.

## Colour palette

Primary:

- Moss Green `#436B3F`
- Deep Moss `#2E3D2B`
- Slate `#1C1D1A`

Accent:

- Brass `#B77738`
- Warm Gold `#D7B15B`
- Parchment `#EEE5D4`

Accent colours should remain accents.

The fuller implementation palette actually wired into the app's theme
resources (`App.axaml`) is tabulated in `docs/mascot.md`; treat it as the
implementation detail behind this brand-level statement.

## Typography

Default to system UI fonts.

Reasons:

- zero dependencies
- native appearance
- accessibility
- user configurable

Do not bundle fonts unless a future requirement justifies it.

## Logo

Requirements:

- recognisable silhouette
- simple geometry
- readable at 16x16
- works in monochrome
- works without gradients

Potential motifs:

- Archivist's seal
- Tree rings
- Open book
- Branches / roots
- Growth rings
- Abstract "H"

Avoid:

- Eyes
- Crystals
- Shields
- Swords
- Fantasy crests
- Generic AI symbols

## Application icon

Requirements:

- identical silhouette across platforms
- recognisable in system trays
- flat-first design
- gradients optional, never required

The shipped icon is the Archivist's Seal; see `docs/mascot.md` ("App icon
vs. mascot icon") for the history and small-size legibility notes.

## Mascot

Name: **Moss**.

Purpose: represents accumulated knowledge and patient guidance. He is not
comic relief.

### Personality

Moss should feel:

- experienced
- thoughtful
- observant
- patient
- quietly humorous
- dependable

He teaches. He does not entertain.

### Appearance

Small woodland goblin. Weathered rather than cute.

Features:

- moss-covered clothing
- practical satchel
- notebooks
- keys
- scrolls
- walking staff
- lantern
- spectacles (optional)

### Expression

Calm. Curious. Knowing. Never goofy.

### Behaviour

Suitable locations:

- onboarding
- empty states
- documentation
- release notes
- achievements
- tips

Avoid:

- constant popups
- excessive animation
- interrupting workflows

The full mascot spec, including the in-app icon silhouette and current
usage, lives in `docs/mascot.md`.

## Interface

Priorities:

- Readability
- Consistency
- Performance
- Native desktop feel

Branding should support usability, never compete with it.

## Overall impression

The application should feel like:

> Software maintained by an experienced archivist.

Not:

> Software built by a wizard.
