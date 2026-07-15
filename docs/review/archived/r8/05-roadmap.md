# 05 - Roadmap

## Version

This round ships as **`0.13.0-alpha`**: user-facing capability
(pronunciation overhaul, guided onboarding) justifies the minor bump,
matching r5-r7 precedent. Update `CHANGELOG.md` (FIFO rules at the
top of that file apply).

## Sequencing

Every phase leaves main shippable.

- **Phase 1 - measurement and corrections.** 3.1 (startup timing),
  3.2 (warm-up off critical path), 3.3 (recall regression fix), 2.5
  (thinking indicator). Small, independent, and they close the
  unreviewed-commit debt first so everything later builds on
  reviewed ground.
- **Phase 2 - voice.** Doc 01 in order: 1.1 normalization, 1.2
  CMUdict, 1.3 user lexicon, 1.4 morphology, 1.5 goldens. 1.1 and
  1.5 alone already fix the worst class of complaints (dropped
  digits); 1.2 is the quality jump.
- **Phase 3 - usability.** 2.1 guided model download, 2.2 voice
  install from wizard, 2.4 markdown tables/links, 2.6 empty states,
  then the 2.3 tooltip sweep last (mechanical, easy to rebase).
- **Phase 4 - performance rendering.** 3.4 pagination, 3.5
  incremental markdown, 3.6 restart audit. Independently droppable
  if the round runs long; measured numbers from Phase 1 decide
  whether 3.4/3.5 are worth their complexity (if a 500-message
  conversation opens in under ~300 ms on the owner's machine, 3.4
  may be recorded as measured-and-skipped).
- **Phase 5 - debt.** 4.1 guard test, 4.2/4.3 splits. Splits go last
  so they do not create rebase pain for the round's real changes.

## Test requirements

Conventions per the `build-and-verify` skill: xunit via
`dotnet test src/Aether.Tests/Aether.Tests.csproj`, harness
registration in `XunitHarnessTests.cs`, no test may require a running
llama-server, network, or audio device. The voice tests assert IPA
strings and token sequences, never synthesized audio.

Required, mapped to acceptance criteria in docs 01-04:

- Normalizer: every 1.1 bullet, positive + boundary.
- Lexicon: ARPABET map vocab-membership sweep, golden words, user
  override precedence, invalid-line skip, suffix morphology (1.2-1.4).
- Golden sentence set with zero-dropped-token assertion (1.5).
- StarterModelCatalog tiers + wizard download flow with faked
  downloader, success and hash-failure paths (2.1).
- Wizard voice install parity via fake registry (2.2).
- Link scheme gate (2.4); chat no-model empty-state condition (2.6).
- Startup timing formatter (3.1); recall regression + single-query
  hydration (3.3); render window invariants + prompt-history
  independence (3.4); incremental markdown equivalence + reuse
  counter (3.5); restart guard (3.6).
- Registration guard demonstrated against a dummy (4.1).

Expect roughly 45-60 new tests (461 at round start). Coverage floor:
raise 49 to **50** (narrative target in CHANGELOG, as before).
Zero-warning build policy applies solution-wide.

## Security review touch

Add to `docs/security-review.md` (currently 0.12.0-alpha):

- **Starter model downloads** (2.1): catalog is hardcoded, https
  only, pinned SHA256 verified via the existing
  `ModelDownloadService` path; a hash mismatch deletes the file. No
  new download primitive is introduced.
- **Clickable links in chat** (2.4): model output can now open the
  user's browser on click. Mitigations: explicit user click required
  (never auto-open), scheme allowlist http/https only, full URL
  shown in the tooltip before click. Non-allowlisted schemes render
  inert.
- **User pronunciation lexicon** (1.3): plain text parsed with
  strict symbol validation; invalid lines are skipped, never
  executed or interpolated.

## Explicit rejections

Recorded so future rounds do not re-propose them.

- **No espeak-ng native dependency for G2P.** It is the "correct"
  Kokoro pipeline but brings a GPLv3 native binary and per-platform
  packaging. Revisit only if CMUdict + normalization is still judged
  unacceptable by ear after this round.
- **No multi-language phonemization and no SSML.** English-first
  stays the scope, as set in r1.
- **No LLM-judged pronunciation scoring.** Goldens are exact IPA
  string assertions, same determinism rule as the r7 scenario suite.
- **No year-style number reading** ("nineteen ninety nine") and no
  unit expansion ("5 MB") in 1.1 this round; cardinal reading is
  correct, just verbose.
- **No virtualizing panel for chat.** Pagination chosen instead:
  variable-height selectable text plus scroll-to-bottom makes
  virtualization fragile; revisit only if pagination measurably
  fails.
- **No harness-to-plain-xunit migration.** The registration guard
  (4.1) removes the failure mode at a fraction of the churn.
- **No InspectionEngine resurrection** for the DoctorService split;
  r6 deleted it as dead code and the split is purely mechanical.
- **No telemetry.** Performance numbers come from the local runtime
  log and tests, never from phoning home.
