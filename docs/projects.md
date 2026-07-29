# Projects

## Overview

A Project is a named container that Chat, Agent, and RAG all read from and
write into: a folder root, a default chat model, a default RAG dataset, a
default system prompt, and a color from the brand palette. Switching the
active project sets that context everywhere at once instead of re-picking a
model, dataset, and folder separately in each panel.

Projects are additive: a Hermaeus with no projects created behaves exactly as
before this existed. Conversations, RAG datasets, and agent tasks each carry
an optional project id (added via an additive SQLite migration on each
store), so they can be scoped and filtered without disturbing anything that
predates Projects.

## Getting Started

1. Open the project switcher (Ctrl+K, or the project indicator in the header)
   and choose **New project**.
2. Create it empty, from the current conversation, or by adopting the
   currently-selected Agent workspace (its folder root and accumulated
   workspace-memory notes come along).
3. Set a folder root, default model, default RAG dataset, default system
   prompt, and a color.
4. Switch projects any time from the same switcher. New conversations
   started while a project is active inherit its defaults; the Agent panel
   pre-fills (never auto-selects) the project's folder root as the
   workspace; RAG's default dataset follows.

## Model

```
Project(
  Id, Name, Description,
  FolderRoot, DatasetId, DefaultModelId, DefaultSystemPrompt,
  Color,
  CreatedAt, UpdatedAt, LastOpenedAt, IsArchived)
```

`FolderRoot` is validated the same way every other user-supplied root path in
Hermaeus is: normalized, traversal rejected, symlinks rejected
(`PathRootValidator`, shared with watched sources). `Color` is one of a fixed
set of brand-palette colors (Forest, Copper, Amber, Teal, Indigo, Berry), not
a free hex value, so the switcher's color dots stay legible in both themes.

## Behavior notes

- **Switching never force-selects.** Chat's new-conversation default and
  RAG's default-dataset selection follow the active project; the Agent
  panel only pre-fills the workspace root text box; the previous workspace
  selection is not silently replaced if you had already picked a different
  folder.
- **Archiving** hides a project from the switcher's default list without
  deleting it or touching anything it is bound to.
- **Deleting a project** does not delete its conversations, datasets, or
  tasks; they keep their project id pointing at a project that no longer
  resolves, the same as any other soft foreign-key reference in Hermaeus's
  local-first stores.
- A DI-singleton project list loads once per app session; the switcher
  reloads from that same live list rather than re-querying on every open, so
  a rename or new project is reflected without a restart as long as it went
  through the switcher/editor.

## Where it's wired

| Subsystem | What a project sets |
| --- | --- |
| Chat | New conversation's model, system prompt, and project id |
| Agent | Workspace root pre-fill (not auto-selected); task project id |
| RAG | Default dataset selection; dataset project id |
| Command palette | Project-scoped grouping alongside app commands |
