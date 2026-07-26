# 01. Projects

## The problem

There is no object in Hermaeus that represents "the thing I am working on".
The pieces of that idea exist, scattered across four owners:

- the Agent's workspace root (`AgentTaskState.WorkspaceRoot`,
  `AgentModels.cs:167`),
- the conversation's attached dataset (`Conversation.RagDatasetId`,
  `Conversation.cs:19`),
- the selected model (chat header state, per conversation),
- the system prompt (`Conversation.SystemPrompt`, `Conversation.cs:8`),
- workspace memory notes (`MemoryScope.Workspace` keyed by a normalized
  root, `Memory.cs:11` and `Memory.cs:34`).

Every one of those is configured independently, in a different panel, and
nothing remembers that they belong together. Switching from one body of
work to another means five manual steps and remembering all five.

`docs/features.md:722-746` already describes this idea under "Workbench
Glue" as though it exists ("Hermaeus connects its local-first systems
around a workspace root so chat, RAG, agent state, project instructions,
and local safety checks can share context"). It does not exist. This doc
builds it, and doc 06's housekeeping reconciles that text.

## Design decisions, settled

**A Project's folder root is optional.** The owner works on repositories
and on bodies of research in roughly equal measure. A Project anchored to a
folder behaves like a codebase; a Project with no root is a topic that owns
conversations, a dataset and memories. Do not build folder-first and bolt
on rootless later.

**A Project is a view over one data root, never a sandbox.** It does not
get its own data root, its own secrets, its own settings file, or its own
database. It is a label plus a set of defaults. This keeps backup,
migration and the data-root manifest exactly as they are.

**"No project" is the default and stays valid forever.** Every list, every
query, and every store must behave identically to 0.30.0 for a user who
never creates one. This is the acceptance bar for the whole doc.

**Switching a project never rewrites history.** It changes what new things
inherit. It never re-tags an existing conversation, never moves an existing
task, never edits an existing dataset. Retroactive reclassification of a
user's own records without asking is the kind of thing that destroys trust
in a local-first app permanently.

## 1.1 The Project model and store

New `Project` record in `src/Hermaeus.Core/Models/Project.cs`:

```
Id            string   (Guid "N")
Name          string
Description   string
FolderRoot    string   (empty = rootless project)
DatasetId     string   (empty = none)
DefaultModelId    string
DefaultSystemPrompt string
Color         string   (a palette key, not a raw hex; see below)
CreatedAt     DateTime
UpdatedAt     DateTime
LastOpenedAt  DateTime
IsArchived    bool
```

`Color` is a key into the existing brand palette (`docs/mascot.md`), not a
free hex string. The switcher and the Activity feed both tint by it, and a
free hex value would let a user produce unreadable text against either
theme. Keep the set small (six or so).

New `ProjectStore` in `src/Hermaeus.Services/ProjectStore.cs` backed by
`{DataRoot}/projects.db`, following `ConversationStore` exactly:
`SqliteMigrationRunner.ApplyAsync(c, "projects", SchemaVersion, ...)` with
`SchemaVersion = 1`, the same `_initGate` single-init pattern
(`ConversationStore.cs:32-84`), and the same `EnsureColumnAsync` helper for
future additive columns.

`FolderRoot` is user-controlled path input. Normalize it the same way the
Agent normalizes `WorkspaceRoot`, reject path traversal and reject symlinks
per `security-posture`. A Project's folder root grants no access by itself:
it only supplies a default the Agent may adopt (1.6).

**Register `projects.db` in `DataRootManifest`.** It is not optional.
`DataRootManifest` is documented as "the single source of truth for
everything under the data root" (`DataRootManifest.cs:4`); a store missing
from it silently breaks migration, backup preview and backup. Add it in the
same commit that creates the store, not later.

Acceptance:
- A fresh data root with no projects.db behaves exactly as today.
- Data-root migration and backup both move/include projects.db, verified by
  the existing data-safety harness extended with a project row.
- A `FolderRoot` containing `..` or pointing at a symlink is refused with a
  visible reason and nothing is saved.

## 1.2 Binding the four subsystems

Four additive changes, each through the existing migration path. **None of
them may change any existing query's default behaviour.**

**Conversations.** `ConversationStore.SchemaVersion` 2 to 3
(`ConversationStore.cs:10`), new migration 3 after the existing
`rag_dataset_id` migration (`ConversationStore.cs:76-77`):
`EnsureColumnAsync(db, "project_id", "TEXT NOT NULL DEFAULT ''", token)`.
Add `Project` to the FTS row so a conversation is findable by its project
name; that means bumping the FTS columns, and `schemaChanged` already
forces `RebuildFtsAsync` (`ConversationStore.cs:79-80`), so this works, but
verify the rebuild path rather than assuming it. Note the insert and read
paths also list columns explicitly (`ConversationStore.cs:170-179` and
:310); a new column must be added to all three or it silently never
persists.

**RAG datasets.** `SqliteRagStore.SchemaVersion` 1 to 2
(`SqliteRagStore.cs:16`), new migration 2 adding `project_id TEXT NOT NULL
DEFAULT ''` to `rag_datasets`. A dataset can belong to one project or to
none. A project-bound dataset is still fully usable from anywhere; the
binding is a default and a filter, never an access control.

**Agent tasks.** `AgentTaskState` gains `public string ProjectId { get;
set; } = string.Empty;` (`AgentModels.cs`, next to `WorkspaceRoot` at
:167). `task_state.json` is the source of truth and is JSON, so this is a
free additive field; a task file written by 0.30.0 deserializes with an
empty ProjectId and behaves exactly as before. `agent/task_index.db` is
explicitly rebuildable (CLAUDE.md), so add the column and let the rebuild
populate it.

**Memories.** Do not add a column. `MemoryScope` already exists
(`Memory.cs:7-12`) with `ScopeId` documented as "empty for Global,
conversation id for Conversation, normalized workspace root for Workspace"
(`Memory.cs:31-33`). Add `Project` to the enum and key `ScopeId` by project
id. Update that doc comment in the same edit. Confirm the enum is persisted
by name and not by ordinal before appending, and if it is persisted by
ordinal, append `Project` last so existing rows keep their meaning.

Acceptance:
- Every one of the four migrations is exercised by a test that opens a
  store seeded at the previous schema version and asserts the rows survive
  with the new column defaulted, not just that the migration runs.
- A task file from 0.30.0 loads, runs and completes with no project.

## 1.3 The project switcher

A compact control in the window header, visible from every panel: the
current project's name and colour dot, or "No project". Clicking opens a
list of projects (most recently opened first, archived hidden behind a
toggle) plus "No project" and "New project...".

This control is also the round's first answer to "what state am I in".
Keep it small; it is not a dashboard.

Rules:
- Switching is instant and never blocks on I/O in the UI thread.
- Switching while an agent task is running does **not** change that task's
  workspace root or project. A running task keeps the project it started
  with, permanently. Show the running task's project in the workbench so
  the divergence is visible rather than surprising.
- The active project id is UI state persisted in `UiSettings`
  (`UiSettings.cs`), through `SettingsService` like every other setting.
  Never write settings.json directly.

Note the wizard-singleton lesson: the ViewModel that owns the switcher is a
DI singleton and will be constructed once. Load projects on first use and
refresh on change events; do not assume a fresh load per navigation, and do
not let a not-yet-loaded list save an empty active project over a real one.

## 1.4 The project detail view

A modest editor, reachable from the switcher, not a new nav panel: name,
description, colour, optional folder root (with a folder picker and the
1.1 validation), default model, default system prompt, attached dataset,
plus read-only counts (conversations, agent tasks, memories, dataset chunk
count) and Archive / Delete.

Deleting a project **never deletes its contents.** It clears the binding on
everything that pointed at it and removes the project row. Say so in the
confirmation dialog in plain words, because every other delete in this app
does destroy data and the user will reasonably assume this one does too.
This is the single highest-risk piece of copy in the round; get it exactly
right, and follow the existing `ConfirmActionDialog` pattern.

## 1.5 Zero-ceremony creation

Creating a project must never feel like bookkeeping, or the feature dies of
neglect. Three entry points, all of which pre-fill from what already
exists:

- **From the current conversation**: name defaults to the conversation
  title, dataset and model default to whatever it already has.
- **From the Agent's selected workspace root**: name defaults to the folder
  name, folder root pre-filled, and any existing `MemoryScope.Workspace`
  notes for that root are offered for adoption into the project scope (a
  checkbox, default on, showing the count).
- **Empty, from the switcher**: name only. Everything else optional.

Name is the only required field. A project with nothing but a name is
valid and useful.

## 1.6 What switching actually does

Precisely this, and nothing more:

- New conversations inherit the project's default model, default system
  prompt, dataset and project id. Existing conversations are untouched.
- New agent tasks inherit the project id and, if the project has a folder
  root, that root as the pre-selected workspace root. **The user still
  confirms the root**, exactly as today: `docs/features.md:300-303` promises
  that no workspace root is selected by default and that choosing a folder
  is what turns on file listing and analysis. A project must not become a
  back door around that promise. Pre-fill, do not auto-select.
- The RAG panel defaults to the project's dataset when creating a query or
  adding documents.
- The Memories panel defaults its filter to Global plus this project's
  scope.
- Recall (doc 02) gains a scope chip defaulting to this project.

It does **not** change: the safety gate, the workspace policy from r23, the
approval flow, which commands are allowed, or any managed server. A project
is a set of defaults. It cannot widen anything.

Acceptance:
- A test asserts that switching projects with a task in `Running` state
  leaves that task's `WorkspaceRoot` and `ProjectId` byte-identical.
- A test asserts that a project with a folder root does not cause the Agent
  to read any file until a root is explicitly confirmed.

## Testing

Roughly 14 to 18 tests: store round trip and archive; the four migrations
against seeded previous-version databases; folder-root traversal and
symlink refusal; delete-clears-bindings-without-deleting-content; switching
with a running task; the no-project default path behaving identically to
today across conversations, datasets, tasks and memories; workspace-note
adoption counts; DataRootManifest inclusion via the data-safety harness.
