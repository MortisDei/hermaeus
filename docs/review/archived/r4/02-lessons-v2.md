# r4-02: Lessons v2

The r3 lesson store shipped and works as a recorder, but the audit
found that its two core learning mechanics, contradiction and
counter-evidence, are structurally unreachable for most lesson kinds.
This doc fixes that, then adds the deferred fourth deterministic
source (task-terminal states), which doc 01-B makes possible.

Standing rule, restated from r3: lessons inform the model, never the
policy. Nothing below touches `AgentSafetyGate`.

## L1. Signature redesign: outcome out of the dedupe key

**Defect.** `SqliteLessonStore.RecordEvidenceAsync` implements
reinforcement (same outcome) and contradiction (different outcome) on
the row found by `(scope, scope_id, signature)`
(`SqliteLessonStore.cs:103-174`). But the capture sites bake the
outcome **into** the signature:

- commands: `command:{cmd}:{ok|fail}:{errorToken}`
  (`AgentService.cs:500`)
- patches: `patch:{tool}:{path}:{ok|fail}` (`AgentService.cs:517`)

So `dotnet build` failing then succeeding creates two independent
rows; each only ever receives same-outcome evidence; the contradiction
branch (`SqliteLessonStore.cs:143-161`) is dead code for both kinds,
and the injector happily presents "X succeeds" and "X fails" side by
side with growing confidence on both.

**Change.** The signature identifies the **subject**; the outcome
lives in the `outcome` column:

- commands: `command:{cmd lowercased}`
- patches: `patch:{tool}:{path lowercased}`
- approvals: `approval:{tool}` (drop the `:rejected` suffix,
  see L2)
- stated: unchanged (`stated:{hash}`; outcome is always Observation)

The error token no longer partitions rows; instead, keep it in the
claim text for failed commands as today (`AgentService.cs:502-505`).
Claim/guidance are already refreshed on every reinforcement, so the
latest error message wins, which is the desired behaviour.

**Migration.** Bump `SchemaVersion` to 2 in `SqliteLessonStore`
(`SqliteLessonStore.cs:15`, migration list at `:84-87`). The v2
migration rewrites existing `Command`/`Patch`/`Approval` signatures by
stripping the outcome suffix; on dedupe-key collision keep the row
with the highest `evidence_count` (ties: latest `updated_at`) and
delete the rest. The store is a rebuildable index, so a lossy collapse
is acceptable; do not attempt to merge evidence counts.

**Acceptance.** Test: record `fail` evidence for a command three
times, then `ok` evidence twice; a single row exists whose confidence
dropped on first contradiction and whose outcome flipped after the
floor was crossed, matching the existing curve logic. Migration test:
seed a v1 database with `command:x:ok:generic` and
`command:x:fail:CS1002` rows, run initialize, assert one surviving row
with the higher evidence count and signature `command:x`.

## L2. Approval counter-evidence

**Defect.** Only rejections are recorded
(`AgentService.cs:459-461, 533-553`). With signature
`approval:{tool}:rejected` and outcome always `UserRejected`, a single
rejection creates "The user rejects {tool} requests in this context"
and nothing the user ever does afterwards can weaken it. Ten
approvals plus one rejection reads as a standing rejection lesson.

**Change.** On an approved gated action in `AppendApprovalAsync`,
record evidence on the same `approval:{tool}` signature with outcome
`Worked`, **but only if a lesson with that signature already exists**.
Routine approvals must not spawn "user approves X" noise rows. Add an
optional `bool CounterOnly` (default false) to `AgentLessonEvidence`;
`RecordEvidenceAsync` returns without writing when `CounterOnly` is
set and no existing row matches. With L1's signature fix, this
approval evidence contradicts a prior rejection lesson through the
existing mechanism, no special casing.

**Acceptance.** Reject `edit_file` once (lesson appears), approve it
twice (confidence drops via contradiction, eventually flips/retires);
approving a tool that was never rejected writes no row.

## L3. Structured command outcomes

**Defect.** Success detection is string sniffing:
`resultText.Contains("Exit code 0")` (`AgentService.cs:498`). A
timeout returns "Command 'X' timed out..." with no exit-code line
(`AgentToolExecutor.cs:147`), which records a `Failed` lesson claiming
the command "fails in this workspace", misleading, since nothing is
known about the command itself. The heuristic also breaks silently if
the result format ever changes.

**Change.** Add `int? ExitCode` and `bool TimedOut` (default null /
false) to `AgentToolResult` (`AgentModels.cs`). `AgentToolExecutor`
sets them for `run_command` only (plumb a small internal result type
out of `RunCommandAsync` at `AgentToolExecutor.cs:116-156`). Lesson
capture then uses `ExitCode == 0` for ok, and **skips lesson capture
entirely when `TimedOut`**. Keep the JSON shape backward compatible
(nullable, omitted when default) since `AgentToolResult` is serialized
into task state.

**Acceptance.** Tests: exit 0 records Worked; nonzero records Failed;
timeout records nothing. Existing task-state JSON without the new
fields still deserializes.

## L4. Task-terminal capture (the r3 deferred item)

Now unblocked: doc 01-B makes `Failed` reachable and doc 01-A/E make
`Complete` trustworthy. Two signals, both deterministic:

**1. Terminal outcome lesson.** When `RunStepAsync` transitions a task
to `Complete` or (via 01-B) `Failed`:

- Signature: `task:{goal-fingerprint}` where the fingerprint is:
  lowercase the goal, split on non-alphanumerics, drop tokens shorter
  than 3 chars and a small fixed English stopword list, distinct,
  sort ordinal, take the first 8 tokens, SHA256, first 16 hex chars.
  Deterministic, no LLM. Reworded goals may fingerprint differently;
  accepted, the cross-task value comes mostly from signal 2.
- `Complete`: outcome `Worked`, claim
  `"Goals like '{truncated goal}' complete in this workspace."`,
  recorded **only if** the task had at least one failed tool result or
  parse error along the way (an uneventful success teaches nothing;
  this matches r3 doc 02 item 4's "complete after prior failures").
- `Failed`: outcome `Failed`, claim built from the first blocker plus
  the truncated goal; guidance
  `"Check the blockers from the failed task before retrying this goal."`.
- Kind: `AgentLessonKind.Task` (already defined, currently unused).

**2. Injected-lesson confirmation.** Add
`List<string> InjectedLessonIds` to `AgentTaskState`.
`AgentService.RunStepAsync` unions in the ids from
`context.Lessons` after each build. On `Complete`, call a new
`ILessonStore.ConfirmAsync(IReadOnlyList<string> ids, string sourceTaskId, CancellationToken)`
that, for each **Active, non-pinned** lesson, bumps `EvidenceCount`,
recomputes confidence through the existing curve, and updates
`LastConfirmedAt`. This is the compounding loop: lessons that were in
context during a successful task gain evidence. On `Failed`, do
nothing to injected lessons (a task can fail for reasons unrelated to
any lesson; only positive confirmation is deterministic enough).

Both capture paths are best-effort inside try/catch like the existing
sources, and live next to them in `AgentService`.

**Acceptance.** Fake-LLM test: a task that fails a command then
completes records the `task:` Worked lesson and bumps evidence on the
lessons that were injected; an uneventful completion records no task
lesson but still confirms injected lessons; a task failed via 01-B's
parse path records the `task:` Failed lesson with the blocker in the
claim. Fingerprint is stable across runs and case/punctuation
variants of the same goal.

## L5. Relevance-aware injection

**Gap.** r3 doc 02 specified relevance as "overlap between lesson
signature/claim terms and the current goal + recently used tools".
Shipped: `ListRelevantAsync` is scope + confidence ordering only
(`SqliteLessonStore.cs:185-210`), then token packing
(`AgentContextBuilder.cs:71-106`). With enough accumulated lessons the
1500-token budget fills with high-confidence rows irrelevant to the
current goal.

**Change.** Keep the store query as the candidate fetch (limit 50).
In `AddLessonsAsync`, score candidates:
`score = (pinned ? 1 : 0) * 10 + termOverlap * 2 + confidence`, where
`termOverlap` is the count of distinct goal-fingerprint-style tokens
(reuse L4's tokenizer) shared between (goal + last 3 tool names) and
(claim + signature). Order by score descending before packing. Pure
in-process, no schema change.

**Acceptance.** Test with a full candidate set where a low-confidence
lesson sharing goal terms outranks a high-confidence unrelated one;
pinned always first.

## L6. Store polish (do while in the file)

- `Map` parses timestamps with bare `DateTime.Parse`
  (`SqliteLessonStore.cs:356-358`), which applies local-time
  conversion to the round-trip strings written by `BindParameters`.
  Use `DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)`.
- `ExtractLessonErrorToken` (`AgentService.cs:602-606`) compiles its
  regex on every call; make it a static compiled regex with a timeout
  like `StatedLessonMarkerRegex`.

## Rejected for this doc

- **LLM-summarized lessons or an idle refinement pass.** Still
  rejected; every capture path above stays deterministic.
- **Cross-workspace lesson generalisation.** A lesson observed in one
  workspace stays there; only approval lessons may be Global (existing
  behaviour when no workspace root is present).
- **Negative confirmation on task failure** (docking every injected
  lesson when a task fails). Too noisy to be honest evidence.
