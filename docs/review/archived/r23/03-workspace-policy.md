# 03. Workspace policy (capability narrowing, not theatre)

Adopts the "Workspace Policy" suggestion. The idea survives contact with
the codebase because the enforcement point already exists: every workspace
file operation funnels through `AgentWorkspaceTools` path resolution, and
`.hermaeus/workspace.json` already declares per-workspace capability
(command families, `WorkspaceManifest.AllowedCommands`, AgentModels.cs:543).
Policy extends that manifest with read/write scoping.

The one rule that keeps this safe: **policy only ever narrows.** A manifest
lives inside the workspace, so hostile workspace content can author one;
the worst it can do is restrict the agent inside that workspace. Policy
must never grant anything (no path outside the root, no new command
family, no gate relaxation), and the implementation must make that
structurally true, not just tested.

## 3.1 Manifest extension

New optional object on `WorkspaceManifest` (additive JSON, no schema
version bump needed since absent means unrestricted, matching today):

```json
{
  "policy": {
    "readAllow": ["src/**", "docs/**"],
    "writeAllow": ["docs/**", "reports/**"],
    "never": ["secrets/**", "certificates/**", ".git/**"],
    "maxFileReadsPerTask": 200
  }
}
```

Semantics:

- `readAllow` / `writeAllow`: glob allowlists, workspace-relative,
  `*`/`**` syntax identical to the existing `glob_files` matching. Empty
  or absent list means "allow all" (backwards compatible).
- `never`: deny list beating both allows, for reads and writes alike.
- `maxFileReadsPerTask`: cap on `read_file`/`summarize_file` executions
  per task (0 or absent = unlimited). Counted on `AgentTaskState`,
  persisted, so restarts do not reset it.
- Malformed policy (bad glob, negative cap, non-array): the workspace
  loads with the policy **rejected as a whole** and a visible warning in
  the workbench and log. Never silently fall back to a partial policy;
  half-parsed security config is worse than none because the user trusts
  a boundary that is not there.

## 3.2 Enforcement at the tool layer

- Enforce in `AgentWorkspaceTools` immediately after the existing
  containment checks (`ResolveSafePath` callers), so policy can never be
  consulted before traversal/symlink rejection. Containment stays the
  outer wall; policy is an inner fence.
- Denied **read**: the tool returns a structured refusal result naming the
  path and the rule ("read blocked by workspace policy: secrets/**"), so
  the model sees it in the transcript and can route around it. Not an
  exception, not a crash.
- Denied **write** (edit_file/create_file/apply_draft_patch target, and
  `run_command` optional path arguments): classified **Blocked** by the
  safety gate before ever reaching the approval queue, with the policy
  rule recorded as the reason in `agent.trace.jsonl`. A policy-denied
  write must not be approvable; that is the difference between a policy
  and a suggestion.
- Read-cap exhaustion behaves like a denied read, with a message telling
  the model the budget is spent.
- Draft-patch queue (`AgentPatchReviewService`) and Rewind (doc 01) apply
  the same write rules through the same code path; no second
  implementation. Rewind of a file the policy now denies writing is
  refused per file with the policy named (the ledger still shows it).

## 3.3 Visibility

- The workbench capability disclosure strip (docs/agent.md "compact
  capability disclosure") gains one line when a policy is active:
  "Workspace policy: reads limited to 2 rules, writes to 2, 3 paths off
  limits." Clicking or expanding shows the raw globs, read-only.
- No policy editor UI this round. The manifest is a hand-edited file, as
  `allowedCommands` already is; the Workspace panel's existing manifest
  affordances are enough.

## 3.4 Tests and scenario tie-in

- Harness tests: allow/deny/never precedence, `never` beating
  `writeAllow`, malformed-policy whole-rejection, read-cap counting
  across a save/load cycle, policy-denied write classified Blocked, and
  policy consulted only after containment (a `../escape` path must fail
  containment, not policy).
- One new scenario workspace exercising policy end to end lands with
  doc 04's scenarios (see 4.5).

## 3.5 Docs

`docs/agent.md`: new "Workspace policy" section with the JSON example, the
"only narrows" rule stated explicitly, and malformed-policy behaviour.
`docs/security-review.md` posture doc: policy joins the controls list as a
deterministic capability restriction. `docs/features.md`: one line.
