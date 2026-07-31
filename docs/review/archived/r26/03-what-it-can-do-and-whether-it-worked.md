# 03. What it can do, and whether it worked

Two questions the owner asks of this panel in daily use, neither of which the
panel answers well today. Both are answerable from data the app already
holds. Neither needs a new subsystem, a new store, or a new nav entry.

## 3.1 The capability text tells you about this workspace, not about a slice

`AgentViewModel.CapabilityNotes` (`AgentViewModel.cs:490-497`) is five
hardcoded strings, unchanged since they were written:

```
"Read-first workspace inspection: list, search, read, and summarise local files."
"Approval-gated patch drafting: propose content, queue it for review, and apply only after approval."
"Approval-gated command execution: only the workspace's own declared build/test recipes can run, never freeform shell text."
"No network, install, commit, push, or remote-control actions in this slice."
"Workspace memory and review queues remain local and explicit."
```

They are broadly true. They are also disconnected from every source of truth
they describe, and one of them ("in this slice") is written in the voice of a
review document rather than an application. The list does not know:

- **Which tools actually exist.** `AgentToolExecutor.CanExecute`
  (`src/Hermaeus.Agent/Services/AgentToolExecutor.cs:20-30`) is the real
  answer: `list_files`, `search_files`, `read_file`, `summarize_file`,
  `draft_patch`, `inspect_git_diff`, `apply_draft_patch`, `run_command`,
  `edit_file`, `create_file`, `glob_files`, `plan_subtasks`, plus any
  `mcp:` tool the configured bridge accepts. `plan_subtasks` and MCP are
  invisible in the current text.
- **Which commands this workspace actually permits.** `AgentSafetyGate`
  (`AgentSafetyGate.cs:79-91`) blocks a command unless its family is
  declared as a safe recipe for this workspace. `CommandRecipes` is already
  loaded and already rendered (`AgentView.axaml:571-597`). The capability
  text says "the workspace's own declared build/test recipes" without
  naming any of them, on a screen where they are listed thirty lines away.
- **Whether an MCP bridge is configured**, which changes the answer to "can
  it reach anything outside this folder" from no to "yes, through servers
  you configured, each call gated" (`AgentSafetyGate.cs:50`).

**The work:** replace the constant list with a derived one. A pure function
over (the executor's tool set, the workspace's command recipes, the workspace
policy, whether an MCP bridge is present) returning the lines to render. Put
it in `Hermaeus.Agent` as a static class beside `AgentApprovalPreview`, which
is the existing precedent for "turn agent state into a sentence a human
reads". The view model calls it and exposes the result; the view binds to it
unchanged.

Keep it short. Four to six lines, each one a fact with a source, not a
paragraph. When there is no workspace selected, say that the answer depends
on the workspace and name the one thing that is true regardless: nothing runs
without an approval.

Because it is a pure function, it tests properly:
- With no recipes declared, the command line says commands cannot run here
  and names `.hermaeus/workspace.json` as where recipes are declared.
- With recipes declared, they are named.
- With no MCP bridge, the text says the agent cannot reach outside the
  workspace. With one, it says calls go through configured servers and each
  is gated.
- Every tool the executor accepts is accounted for by exactly one line. This
  is the guard that stops the text drifting again: add a tool to
  `CanExecute` without classifying it here and the test fails.

That last test is the point of the whole item. The current text drifted
because nothing connected it to the code it described.

## 3.2 A finished run says what it did

The run ledger already records everything needed to answer "did that actually
work": `AgentRunLedger` (`src/Hermaeus.Agent/Models/AgentModels.cs:460-463`)
carries `Files`, `Commands` and `Approvals`, with per-file `Kind` (created or
edited) and `LineDelta` computed at
`src/Hermaeus.Agent/Services/AgentRunLedgerBuilder.cs:64-67`.

It is rendered at `AgentView.axaml:845-970`, one expander among thirteen,
below Draft Patch Decisions, above Recent Tasks. To find out whether a run
achieved anything you scroll past nine other panels and read three lists.

**The work:** when a task reaches a terminal status, the Run tab shows a
short outcome block above the fold, composed entirely from values that
already exist:

- **Files.** `LedgerFiles` count, split created and edited, with the summed
  line delta. "Changed 3 files (2 edited, 1 created), +81 -12."
- **Commands.** `LedgerCommands` count and how many succeeded, from the
  existing `OutcomeLabel`. A failing command in a run reported as Complete is
  the single most useful thing this block can say.
- **Approvals.** `LedgerApprovals` count, approved and rejected.
- **Unfinished plan.** `PrematureCompleteNote` / `HasPrematureCompleteNote`
  already exist (r19 3.3, `AgentView.axaml:63-64`) and already say that a
  terminal task still has pending steps. Move that line here, where it is
  the headline rather than a footnote in a tile.
- **Reservations.** `CurrentTask.Reservations` (`AgentView.axaml:475-490`),
  the model's own statement of what it could not verify. Doc 02 already moves
  this to the Run tab; it belongs inside this block, because "what it could
  not confirm" is part of the outcome, not a separate topic.
- **Nothing at all.** A run that changed no files and ran no commands says
  so plainly. That is a real outcome and currently reads as an empty panel.

Then one link to the Changes tab for the detail, and the renamed "Save this
run as a workspace note" button from doc 02 2.3, which is exactly the moment
a user wants it.

**Composition only.** This block computes no new facts and calls no new
service. If a number is not already derivable from `AgentRunLedger`, the
open `AgentTaskState`, or an existing view model property, it does not go in
the block. A summary that computes its own version of the truth is a second
source of truth, and this repository has spent two rounds removing those.

Tests, all against a seeded ledger and task state:
- Counts and line delta for a mixed created/edited/failed-command run.
- A run with no files and no commands produces the "changed nothing" text,
  not an empty string.
- A `Complete` task with pending plan steps surfaces the premature-complete
  line.
- A run with a failed command says so even when the task status is
  `Complete`. This is the assertion that earns the item.
- Reservations present and absent.
- A non-terminal (running, waiting) task produces no outcome block.

## What this doc does not do

- **No confidence score, no percentage, no grade.** r23 2.3 already settled
  this for Reservations and the reasoning is unchanged: the app reports what
  happened, it does not rate itself.
- **No LLM-written run summary.** `CurrentTaskSummaryLabel` is already the
  model's own account and is already on screen. 3.2 is the deterministic
  counterpart to it, and mixing the two would make it impossible to tell
  which one is evidence.
- **No new store and no new panel.** Both items render into space doc 02
  already allocated.
- **No change to `AgentToolExecutor`, `AgentSafetyGate`, or risk
  classification.** 3.1 reads the tool set and the policy. It does not
  define, extend, or reorder either.
