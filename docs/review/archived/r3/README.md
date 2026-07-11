# Review Round 3 (r3) - July 2026

Third principal-engineer review. r1 and r2 are fully actioned (see
`archived/`). Unlike r1/r2, this round is user-directed rather than
defect-driven: the owner set five mandates, and the codebase audit was
run against those mandates instead of a general sweep.

The five mandates:

1. **Agent capability.** The reference bar is Claude Code and Codex.
   Close the gap where it is architectural, not model-quality.
2. **Memory improvements.**
3. **Agent self-learning.** Track what works and what does not, with
   evidence, so the agent gets better on a given machine/workspace over
   time. Entries must be inspectable, editable, and removable.
4. **Optimisations and improvements.**
5. **Unit test coverage**, plus relocating `tests/Aether.Tests` into
   `src/` with the other projects.

Read in order:

1. [Agent Capability](01-agent-capability.md) - the honest gap analysis
   against Claude Code/Codex and the architectural changes that close the
   closable part: a transcript loop, autonomous multi-step runs, native
   tool calling, surgical edit tools, better navigation tools, and a
   verify loop.
2. [Memory and Self-Learning](02-memory-and-self-learning.md) - memory
   retrieval/lifecycle upgrades, and the full design for the lesson
   store (the self-learning system), built on deterministic signals the
   agent already produces.
3. [Roadmap](03-roadmap.md) - everything sequenced into phases with
   acceptance criteria, including the test-project move, optimisation
   items, coverage policy, and explicit rejections.

Headline verdict: the current agent is a safe, auditable *single-step
planner*; Claude Code and Codex are *loops*. Nothing in Aether's
security posture prevents the loop - the approval gates, path
containment, and deterministic risk classification all survive
unchanged. The gap is: one LLM call per user click, no conversation
continuity between steps (tool results truncated to 4000 chars of JSON,
only the last five kept), whole-file rewrites as the only edit
primitive, and a one-word heuristic doing the searching the model
should do itself. Fix the loop and the tools, and the same local models
will look dramatically more capable. The lesson store then compounds
that: every command result, patch outcome, and approval decision is
already recorded somewhere; r3 turns those records into recallable,
confidence-weighted guidance.

**Status: fully actioned as of `0.9.43-alpha`.** Every item in docs 01-03
landed: transcript replay, the autonomous loop, native tool calling with
automatic JSON-protocol fallback, `edit_file`/`create_file`, navigation
tools, `set_plan`, `run_command` template families with per-task
remembered approvals, hybrid memory recall, relevance-aware injection,
memory lifecycle (decay/auto-archive/update-forget markers), structured
memory extraction, the workspace-memory-store unification, the full
lesson store (capture, injection, UI), the test-project move to `src/`,
a CI workflow, and the coverage ratchet. One deliberate scope
simplification versus the original doc: automatic lesson capture from
task-terminal states (Task kind) was not wired - goal-normalization
for a meaningful cross-task signature turned out to need more design
than the other three deterministic sources (command/patch/approval);
everything else, including the `[LESSON: ...]` stated-lesson path, is
live. r4 should revisit that gap once the shipped lesson sources have
real usage traces to learn from.
