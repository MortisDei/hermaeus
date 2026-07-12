# 04 - Roadmap

## Version

This round ships as **`0.11.0-alpha`**: two user-facing capabilities
(answerability surfaces, usage-aware recommendations) justify the minor
bump, matching the r5 precedent. Version comes from the assembly info in
the Desktop csproj (`SystemInfoService.cs:29` reads
`AssemblyInformationalVersionAttribute`); bump it there.

## Sequencing

Every phase leaves main shippable.

- **Phase 0 - cleanup.** 3.1 InspectionEngine removal. Do it first so no
  other item is tempted to build on the dead path.
- **Phase 1 - usage data.** 2.1 rollup table + 2.2 service. Silent data
  collection lands early so real usage accumulates while the rest of the
  round is built.
- **Phase 2 - answerability, storage-touching items.** 1.4 per-message
  model persistence, 1.8 patch pre-images. These change persisted shapes
  (JSON-additive) and need the most care; land before UI polish.
- **Phase 3 - answerability, UI items.** 1.1 nav labels, 1.2 data-root
  affordances, 1.3 local/remote badges + audit summary, 1.6 retrieval
  breakdown, 1.7 risk reasons, 3.2 recipe transparency, 3.3 lesson strip,
  3.4 voice disclosure. Each is independently droppable.
- **Phase 4 - usage-aware insights.** 2.3 math + UI + doctor extension,
  2.4 audit line. Last, because it benefits from Phase 1 having run on a
  real machine for days.
- 1.5 (context receipt) can ride in Phase 3 or 4, whichever is lighter by
  then.

## Test requirements

Existing custom runner conventions in `src/Aether.Tests` (see
`build-and-verify` skill). Required coverage:

- model_usage: upsert/rollup semantics, prune independence, migration on a
  pre-r6 traces.db copy, window queries (2.1/2.2 acceptance bullets, one
  test each).
- Usage insights math: every 2.3 acceptance bullet as a pure test over
  synthetic usage + runs; no SQLite in math tests.
- Message persistence: pre-r6 messages_json loads, new messages
  round-trip ModelId, mid-conversation switch preserved.
- Patch revert: apply/revert byte identity, created-file deletion,
  external-edit warning path, pre-image-less legacy state.
- Counting/derivation logic for the outbound-destinations summary, the
  retrieval plain-language line, and the voice audit item.
- ViewModel tests for the lesson strip and risk-reason display, following
  existing AgentViewModel test patterns.

Coverage floor: raise 47 to **48**. The rollup and insights math are pure
logic and should carry it; do not raise further.

Zero-warning build policy applies. Items 1.4, 1.8, 2.1 touch persisted
state: follow `storage-and-data-root` (additive schema, atomic writes) and
re-check the `docs/security-review.md` validation checklist lines that
mention them before archiving the round.

## Explicit rejections

Recorded so future rounds do not re-propose them.

- **No telemetry, no cloud sync of usage data.** model_usage is local
  aggregation disclosed in Privacy Audit, nothing more.
- **No auto-switching models from usage insights.** Same rule as the r5
  advisory: recommendations inform, never act.
- **No onboarding tour/overlay/video.** The fix for a confusing UI is a
  legible UI, not a tour explaining an illegible one. If a question needs
  a tour to answer, the surface for that question is wrong.
- **No new approval gate for lessons.** 3.3 is visibility, not friction;
  the lesson lifecycle already has retire/delete.
- **No wiring InspectionEngine instead of deleting it.** Three services
  each own their checks and views; resurrect an aggregation layer only
  when a real consumer exists.
- **No per-message token that identifies the user or machine.** Provenance
  is model id + timing, nothing else.
- **No full history for Sources on reload.** 1.4 persists model
  attribution only; retro-associating memory/RAG sources for old turns is
  not worth a schema for data nobody recorded.

## Done means

All acceptance criteria in docs 01-03 pass, coverage floor 48, zero
warnings, pre-r6 settings/conversations/task state/traces.db load
unchanged, Doctor/Trust/Privacy Audit emit the same check ids after 3.1,
and a first-time user can answer all seven README questions from visible
UI. Then archive this pack to `docs/review/archived/r6/` and tag
`0.11.0-alpha`.
