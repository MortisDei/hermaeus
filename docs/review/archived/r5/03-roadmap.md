# 03 - Roadmap

## Version

This round ships as **`0.10.0-alpha`** (not 0.9.45). Two new
user-facing capabilities (voice orchestration, benchmark insights)
justify the minor bump; it is the first minor increment since 0.9.
Update the version constant and anywhere `AetherVersion` is stamped
(benchmark metadata, doctor snapshot).

## Sequencing

Phases are ordered so every phase leaves main shippable.

- **Phase 0 - foundations.** 1.1 orchestrator + 1.2 profiles/settings,
  and 2.1 tag propagation. Nothing user-visible changes yet except the
  new settings section; the manual chat speak button migrates last in
  this phase (1.3 first half) as the proof the orchestrator behaves.
- **Phase 1 - insights engine.** 2.2 service + math, pure unit tests.
  No UI yet. This is the highest test-value work in the round; land it
  before any UI polish.
- **Phase 2 - voice consumers.** 1.3 auto-speak, 1.4 agent narration,
  1.5 doctor, 1.6 benchmark + notification bridge, 1.7 workspace
  profiles. Each consumer is a small independent PR-sized change;
  any can be dropped without destabilizing the others.
- **Phase 3 - insights UI.** 2.3 panel, 2.4 advisory check.
- **Phase 4 - streaming speech.** 1.8, last, behind its experimental
  flag. If the sentence chunker or overlap plumbing runs long, ship
  the round without it; the flag simply stays hidden.

## Test requirements

New tests follow the existing custom runner conventions in
`src/Aether.Tests` (see `build-and-verify` skill). Required coverage:

- Orchestrator: serialization, preemption ordering, Low-drop,
  DedupeKey, mute/disabled short-circuit, failure toast throttling
  (fake provider capturing `VoiceSynthesisRequest`s and playback
  intervals; extend `Helpers.cs` fakes rather than inventing new
  ones).
- Settings: pre-r5 settings JSON round-trip, profile resolution
  fallback chain, workspace.json optional field.
- Sanitizer and sentence chunker: pure-function table tests.
- Consumers: one behavioral test each (exact utterance count per the
  acceptance criteria in doc 01).
- Insights math: every acceptance bullet in 2.2 is one test. Use
  synthetic `BenchmarkRun` builders; do not touch SQLite for math
  tests.
- Insights service: tag join fallback (suite present/deleted) against
  a real temp-dir `benchmarks.db`, matching existing
  `ServiceTests.cs` benchmark patterns.
- ViewModel: insights panel states, benchmark completion utterance.

Coverage floor: raise from 45 to **47**. The insights math and
chunker are pure logic and should overshoot this; do not raise
further in this round.

Zero-warning build policy applies as always.

## Explicit rejections

Recorded so future rounds do not re-propose them.

- **No STT, wake word, or voice input.** This round makes Aether
  speak, not listen. Input is a different security and UX problem.
- **No audio mixer or ducking.** One utterance at a time is a feature:
  it forces prioritization instead of cacophony. Streaming speech
  overlaps synthesis with playback, never playback with playback.
- **No LLM anywhere in the insights engine.** Recommendations must be
  reproducible from stored runs. An LLM may someday *phrase* a
  summary; it will never *rank*.
- **No auto-enabling of any voice channel.** Unrequested audio is the
  fastest way to make users disable TTS forever. Chat manual speak is
  the only default-on surface, because it already was.
- **No speaking model output verbatim in agent narration.** Narration
  is built from task state. Model text reaches audio only in chat,
  where the user asked for it.
- **No per-step agent narration.** Milestones only.
- **No auto-switching models from the advisory check (2.4).** Info
  severity, human decides. Same philosophy as the agent approval
  gate: recommendations inform, never act.
- **No network calls in insights, no benchmark data leaving the
  machine.** Local-first is the product.
- **No leaderboard sharing/export format this round.** Premature until
  the schema stabilizes with real tag data.

## Done means

All doc 01 and 02 acceptance criteria pass, coverage floor 47, zero
warnings, pre-r5 settings and workspace files load unchanged, and the
app with every new toggle left at defaults behaves byte-identically to
0.9.44 except for the new (silent) settings UI. Then archive this pack
to `docs/review/archived/r5/` and tag `0.10.0-alpha`.
