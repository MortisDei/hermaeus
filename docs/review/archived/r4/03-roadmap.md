# r4-03: Roadmap

Sequenced so each phase makes the next cheaper. Doc 01 items are
lettered (A-E), doc 02 items are L1-L6. Every phase lands with tests
in the matching `*Tests.cs` file (create new ones as needed), zero
warnings, and doc updates per AGENTS.md (`docs/agent.md`,
`docs/features.md`, `CHANGELOG.md`; version target `0.9.44-alpha`).

## Phase 0 - Hygiene (small, do first)

01-C (approved-tool transcript entry + delete
`ExecuteApprovedToolAsync`), 01-E (stale `KnownRisks` text, stale doc
comment, step-1-only workspace search, `PendingSteps` reconciliation),
L6 (round-trip timestamp parsing, static error-token regex).

All are contained, independently testable, and remove traps the later
phases would otherwise trip over (any new approval-path work would
have inherited the transcript gap; any new lesson test would have
inherited the timezone drift).

## Phase 1 - Interaction and failure semantics (doc 01: A, B, D)

Order: B (failure states) -> A (user reply) -> D (native-call
fidelity). B first because A's tests want deterministic terminal and
waiting states to assert against, and because nothing in Phase 3 makes
sense until `Failed` is reachable.

End-to-end regression for the phase: on a fixture workspace with a
fake LLM, (1) a task asks a question, is answered from the service
API, resumes, and completes, with the reply visible as a
`transcript-user` pack item on the following step; (2) a task whose
model output is garbage three times ends `Failed` with every bad step
in the transcript; (3) a budget-exhausted task ends `WaitingForUser`
with the budget note.

## Phase 2 - Lesson mechanics (doc 02: L1, L2, L3)

Order: L3 (structured command outcomes) -> L1 (signature redesign +
schema v2 migration) -> L2 (approval counter-evidence). L3 first so
L1's new capture sites are written once against the structured fields
rather than rewritten twice. L2 last because it depends on L1's
signature shape.

The migration test (seeded v1 database collapsing correctly) is part
of the definition of done, not optional; owner machines already have
populated `lessons.db` files from r3 usage.

## Phase 3 - Task-terminal capture and relevance (doc 02: L4, L5)

L4 needs Phase 1-B (terminal states) and Phase 2-L1 (subject-keyed
signatures). L5 shares L4's tokenizer, so build the tokenizer once as
an internal static helper with its own tests, then use it from both.

## Phase 4 - Optional, unchanged from r3

Chat-side consumption of Global lessons behind a settings toggle
(default off), exactly as r3 doc 02 sketched it. Carried, not
required: it was optional in r3 and nothing this round makes it more
urgent. Skip unless the earlier phases land fast.

## Coverage ratchet

The floor was 43 (`scripts/coverage.sh:4`, `scripts/coverage.ps1`).
Measured 45.45% line coverage after Phases 0-3 landed (325 tests, up
from 304); floor raised to 45.

## Rejected this round

- **Auto-approve / yolo mode.** Permanently rejected; restated so no
  future round relitigates it. Per-task remembered exact-command
  approvals remain the ceiling.
- **A second edit primitive (multi-hunk patch format).**
  `edit_file` + `apply_draft_patch` cover the need; a diff-format
  parser is a large correctness surface for marginal gain with local
  models. Reassess only with evidence of repeated multi-hunk failure
  from real transcripts.
- **Letting a user reply approve a pending tool.** The reply channel
  (01-A) refuses to double as an approval; approvals stay on their
  own explicit path.
- **LLM-based goal similarity for task lessons.** The deterministic
  fingerprint is deliberately conservative; false negatives (reworded
  goal, new fingerprint) are acceptable, false positives are not.
- **Retiring the JSON protocol now that native tool calling works.**
  Local models without tool support remain first-class; the fallback
  stays permanently tested.

## Definition of done for r4

Phases 0-3 complete per acceptance criteria in docs 01 and 02; the
coverage floor raised; docs and CHANGELOG truthful. At that point the
loop is conversational (questions get answers), honest about failure
(tasks end in real terminal states, visibly), fully self-documenting
(every executed action reaches the transcript), and the lesson store
does what r3 promised: reinforce, contradict, retire, and compound
across tasks, all deterministically.
