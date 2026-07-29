# 04. Activity and legibility

## The problem

Asked what confuses him about his own app, the owner named two things, and
neither was navigation:

> "What can it even do?" and "Did that actually work?"

Both are legibility failures, and both are fixable with modest code.

**"What can it even do"** is a discovery failure. There are twelve panels
(`MainWindowViewModel.cs:53-64`) and roughly two hundred user-facing
actions distributed among them. Nothing anywhere enumerates them. A
capability you shipped, forgot, and cannot rediscover is worth the same as
one you never built.

**"Did that actually work"** is an outcome failure. Work happens in the
background all the time now: managed servers start and crash, downloads
run, ingest runs, Doctor scans at startup, backups run, the memory archiver
sweeps, model update checks fire. Each reports through a toast that
disappears, or a state change three panels away, or nothing at all. There
is no single place that answers "what has this app been doing".

## 4.1 One command registry

**Implement this first. Doc 02 2.5 depends on it.**

`ICommandRegistry` in `Hermaeus.Core`, populated at composition time, one
entry per user-facing action:

```
AppCommand(
  Id          string,     // stable, e.g. "chat.new-conversation"
  Title       string,     // "New conversation"
  Area        string,     // "Chat" - groups the palette's empty state
  Description string,     // one line, plain, no marketing
  Keywords    string[],   // synonyms the user might actually type
  Shortcut    string,     // "Ctrl+N" or empty
  CanExecute  Func<bool>,
  Execute     Func<Task>)
```

Rules:

- **One registry, two surfaces.** The palette (doc 02 2.5) and per-panel
  discovery (4.4) both read from it. Neither builds its own list. This is
  the only way the two stay in sync as the app grows.
- Registration lives next to the ViewModel that owns the action, not in one
  giant central file that will rot within two rounds.
- `CanExecute` is what makes the palette honest: a command that cannot run
  right now shows as disabled with the reason ("no workspace root
  selected", "server not running"), rather than being hidden. Hiding it
  recreates exactly the discovery problem this item exists to solve.
- Commands are **navigation and user actions only.** No command may bypass
  an approval gate, execute an agent tool, or perform a destructive action
  without its existing confirmation dialog. A palette entry is a shortcut to
  a UI affordance, never a new privileged path around one. Any command
  whose target is destructive routes through the same
  `ConfirmActionDialog` the panel uses.
- A guard test asserts every registered command has a non-empty Title,
  Area and Description, a unique Id, and that its Id is not duplicated. The
  registry is going to be the app's public self-description; it cannot ship
  half-filled.

Coverage target for this round: every navigation destination, plus every
action currently reachable from a top-level button on Chat, Agent, Models,
Services, RAG, Memories, Doctor and System. Deep sub-controls
(per-model spinners, per-chunk actions) are out of scope and should stay
out; the registry is for things a user would go looking for.

## 4.2 The Activity feed

A new panel, `ActivePanel == "activity"`, reachable from the nav and from
`Ctrl+K`. One reverse-chronological list of what the app has done.

Most of the plumbing exists. `ITraceStore` is already the "single store for
chat, RAG, and agent traces" with viewers as projections
(`ITraceStore.cs:6-9`), and `TraceKind` already covers Chat, Rag, Agent and
LocalApi (`TraceRecord.cs:6-12`). Activity is the projection nobody built.

**Extend, do not duplicate.** Add `TraceKind.System` and record background
work through a thin `IActivityRecorder` that wraps `ITraceStore.AppendAsync`
so callers do not each hand-build a `TraceRecord`. New events to record, all
of which currently vanish:

- managed server start, stop, crash, and port-conflict refusal
- llama.cpp and model downloads, updates, and hash verification outcomes
- RAG ingest and watched-source refresh runs (doc 03)
- Doctor scans, with the error/warning counts
- backup, restore, and data-root migration
- memory auto-archive sweeps, with counts
- voice backend start/stop (doc 05)

Constraints that are not negotiable:

- **Redaction applies.** These rows persist. Everything goes through
  `RedactionService` before it is written, exactly as runtime logs do
  (`docs/features.md:671-676`). A download URL with a token in the query
  string must not land in the activity store in the clear.
- **Pruning applies.** `ITraceStore` implementations already prune old rows
  per kind (`ITraceStore.cs:11`). System events must be pruned too, and
  they will be higher volume than agent traces. Do not let this table grow
  without bound on a machine that has been running for a year.
- **Deterministic rows only.** No model-written summary of the user's week.
  Every row is a fact the app observed.
- **The user can clear it.** A "Clear activity history" action behind the
  standard `ConfirmActionDialog`, and a stated retention window shown in
  the view so the pruning is visible rather than implied. This feed is a
  durable record of what its owner has been doing on their own machine;
  the same reasoning that gives Recall a clear action in doc 02 2.0
  applies here, at smaller scale. Clearing activity removes trace rows
  only and must not touch the durable `model_usage` rollup
  (`TraceRecord.cs:44-48`), which is aggregate and separately owned; say
  so in the confirmation.

The feed filters by kind and by project (doc 01), shows relative times,
and each row navigates to the thing it is about where one exists.

## 4.3 Outcomes, stated

Every activity row carries an explicit outcome, not just a description:

```
Succeeded | Failed | Cancelled | Partial | Running
```

with a one-line reason whenever it is not `Succeeded`. `Partial` matters
and must not be collapsed into success: an ingest where four of forty files
errored, a refresh that skipped a locked file, a rewind that skipped files
changed since. r23 already established that reporting partial success
honestly is the house style; this makes it structural.

Two connections back to the rest of the UI:

- A toast for anything with a lasting outcome gains a "See in Activity"
  action, so a missed toast is recoverable rather than gone. Toast history
  already exists (`MainWindow.axaml:357`); this points it at the durable
  record.
- Anything currently `Running` shows at the top of the feed with live
  status. "Is it still going, or did it die" should never require checking
  a process list.

## 4.4 What you can do here

Each major panel gets one small affordance, an icon button with a tooltip,
that lists that panel's own commands from the 4.1 registry with their
descriptions and shortcuts, including the disabled ones with their reasons.

This is deliberately not a tour, not a tutorial, and not a first-run
overlay. It is the same registry, filtered to `Area`, shown where you
already are. It costs almost nothing once 4.1 exists and it is the answer
to "what can it even do" at the exact moment the question occurs.

Copy rule: descriptions state what the action does. No marketing, no
personality, and per `docs/mascot.md`, when in doubt drop the personality
and state the fact.

## 4.5 Settings-field search

The Settings versus Services split is deliberate and documented (process
and server configuration on Services, preference-only knobs on Settings,
CLAUDE.md), and it is still a thing the user has to remember correctly
before they can find a knob.

Index the settings **fields** into the palette: label text plus the section
that owns it, so typing "context size", "flash attention", "auto-archive"
or "redaction" jumps straight to the right page and section regardless of
which of the two owns it. Field entries are a distinct kind in the palette
result list ("Setting" chip), separate from commands and recall hits.

This is a small item that removes an entire class of "which page has this"
friction, permanently, for every knob added from here on.

## Testing

Roughly 14 to 17 tests: registry guard (unique ids, required fields
populated, no duplicates); `CanExecute` false renders disabled with a
reason rather than hidden; a destructive command routes through its
confirmation rather than executing directly; activity recording for each
new system event kind; redaction applied to a recorded URL carrying a
token; pruning bounds the system-kind table; outcome classification
including `Partial`; clearing activity history removing trace rows while
leaving the `model_usage` rollup intact; the palette and per-panel
discovery returning identical sets for the same Area; settings-field
search resolving a field that lives on Services and one that lives on
Settings.
