# Mascot: Moss

This document is the source of truth for Moss's identity, personality, UI
voice, and in-app character use. Product-level visual language, colors,
typography, and icon requirements live in [`hermaeus-branding.md`](hermaeus-branding.md).

## Core identity

**Name:** Moss.

**Purpose:** Moss represents accumulated knowledge and patient guidance. He is
not comic relief and he is not the AI. He is the presence behind the scenes who
keeps the workspace understandable and helps the user navigate it.

## Personality

Moss is experienced, thoughtful, observant, patient, quietly humorous, and
dependable. He teaches. He does not entertain.

## Voice in UI copy

Text attributed to Moss, including tooltips, empty-state hints, and tips,
follows these rules:

- Use one sentence, or two at most. Be brief because Moss is busy, not curt.
- State what is true and what to do next, in that order. For example: "No
  datasets yet. Ingest a folder and Moss will keep it indexed."
- Dry, understated humor is allowed occasionally and only when nothing is
  wrong. Error and warning surfaces use calm competence, never jokes about the
  failure.
- Use no exclamation marks, emoji, magic, wizardry, or AI-mystique wording.
  Do not address the user as "friend" or with similar familiarity.
- Say "Moss", not "I", when the speaker needs to be named. Most lines should
  not name a speaker at all.
- Moss never speaks for the model. Answers, refusals, and mistakes belong to
  the model or the application.
- Setup guidance states one factual next step, what can be skipped, and no
  claim that a provider, model, or runtime is ready before it is verified.

## Visual style

Moss is a small, weathered woodland goblin rather than a cute or comic
character. The appearance may include moss-covered clothing, a practical
satchel, notebooks, keys, scrolls, a walking staff, a lantern, and optional
spectacles.

His expression is calm, curious, and knowing. It is never goofy. Approved
locations are onboarding, empty states, documentation, release notes,
achievements, and tips. Avoid constant popups, excessive animation, and
interrupting workflows.

## Icon-scale silhouette

The small in-app accent uses only a pointed, weathered hood, pointed ears,
simple calm eyes, and a small brass spectacles band. It uses flat shapes and
must remain legible at 16x16, 32x32, and desktop icon sizes.

There is no free-floating accent dot. Any lantern glow must be attached to a
lantern rather than rendered as an unexplained colored circle.

## Current in-app usage

`Controls/MossIcon.axaml` implements the icon-scale silhouette with plain
Avalonia shapes and no image or rendering dependency. The same geometry is
used for the application, desktop, taskbar, and tray raster assets by
[`scripts/generate-icons.ps1`](../scripts/generate-icons.ps1). Keep those two
representations synchronized when the silhouette changes.

The shared `Controls/MossEmptyState.axaml` control uses the icon in empty states
for Chat, Agent, Benchmarks, Memories, and RAG. Moss also appears in the setup
wizard greeting, the RAG ingest-progress row, and the Services error banner.
Those placements carry short factual hints. Moss does not appear in the Chat
transcript, answer for the model, or use timed popups and tips.

The current asset is a programmatic vector treatment intended to be clear at
small sizes. A future commissioned illustration can replace it, but the
application and in-app control should continue to share one approved
silhouette.
