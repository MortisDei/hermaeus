# Review Round 4 (r4) - July 2026

Fourth principal-engineer review. r1-r3 are fully actioned (see
`archived/`). r4 was scoped by a fresh audit of the r3 implementation
as shipped in `0.9.43-alpha` (commit 107d5d5), plus the one item r3
explicitly deferred: automatic lesson capture from task-terminal
states.

The audit's central finding: the r3 loop works, but its **feedback
channels are half-wired**. The agent can ask the user a question but
there is no way for the answer to reach it. A task can never reach
`Failed` because nothing ever assigns that status, so step errors
strand tasks in `Running`. Approved tools execute without ever entering
the transcript, so the model never sees the results of the very
actions it most needed permission for. And the lesson store's
contradiction mechanism, the heart of its "learning" claim, can never
fire for command or patch lessons because the outcome is baked into
the dedupe signature: `command:X:ok` and `command:X:fail` are separate
rows that reinforce forever and never argue with each other.

r4 fixes the feedback channels first, then rebuilds lesson capture on
top of them, which is also the only honest order: task-terminal
lessons need terminal states that actually occur.

Read in order:

1. [Interaction and Failure Semantics](01-interaction-and-failure-semantics.md) -
   the user-reply channel, real failure states, transcript completeness,
   native tool-call fidelity, dead code removal, and stale-text fixes.
2. [Lessons v2](02-lessons-v2.md) - signature redesign so contradiction
   works, approval counter-evidence, structured command outcomes, the
   deferred task-terminal capture (now unblocked by doc 01), relevance
   scoring, and the schema v2 migration.
3. [Roadmap](03-roadmap.md) - sequencing, small verified optimisations,
   test requirements, coverage ratchet, and explicit rejections.

Everything here is deterministic-first, keeps the approval gate
untouched, and follows the standing rule from r3: lessons inform the
model, never the policy.

**Status: fully actioned as of `0.9.44-alpha`.** Every item in docs
01-02 landed: the user-reply channel and its workbench UI, real
failure semantics (`ConsecutiveStepErrors` escalation to `Failed`,
`WaitingForUser` on any unhandled step exception or step-budget
exhaustion, both with transcript/log notes), approved-tool transcript
entries plus removal of the dead `ExecuteApprovedToolAsync`, native
tool-call prose/dropped-call fidelity, the stale-text and
`PendingSteps`/step-1-only-search hygiene fixes, the lesson-signature
redesign with its schema v2 migration, approval counter-evidence,
structured `run_command` exit codes (timeouts skip lesson capture),
task-terminal capture via the new deterministic `AgentLessonText`
goal-fingerprint tokenizer plus injected-lesson confirmation, and
relevance-aware lesson ranking. Phase 4 (optional chat-side global
lesson consumption, off by default) landed too: a Memory settings
toggle folds Global-scope lessons into chat's system prompt as a
read-only block, kept out of the `[MEMORY_UPDATE]`/`[MEMORY_FORGET]`
marker path so chat can never mutate the lesson store. 23 new tests
(327 total), zero warnings, coverage floor raised 43 -> 45.
