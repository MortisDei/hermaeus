# Review Round 8 (r8)

Theme: **polish what exists**. r3-r7 built capability (agent, memory,
lessons, voice orchestration, insights, scenario evals). r8 makes the
existing product feel finished: a voice that pronounces English
correctly, a first launch that sets up a novice end to end, an
interface that explains itself, measured performance instead of assumed
performance, and payment of the specific debts the last three rounds
left behind.

This round also folds in corrections to two optimization commits that
landed between r6 and r7 without a review pass (`ad618da`, `aea2326`).
They shipped real improvements but also a probable memory-recall
regression and a dead-markup thinking indicator; both are itemized
below rather than re-litigated.

Priorities (from the owner's brief; treat as binding):

- **Usability.** Everything self-explanatory; tooltips where needed;
  onboarding that works regardless of user skill level.
- **Performance.** Measure, then optimize; no speculative tuning.
- **Voice.** The phonemizer's rule-based approach has hit its ceiling;
  pronunciation is still not acceptable. Replace guessing with a real
  lexicon.
- **Technical debt.** Pay it down where it has already caused bugs
  (dead unregistered tests) or blocks review (multi-class files).

## Documents

- `01-voice-pronunciation.md` - text normalization (the digit-dropping
  bug), CMUdict lexicon with stress, user override lexicon, golden
  pronunciation tests.
- `02-onboarding-and-usability.md` - guided setup path with hardware
  aware model download, voice install from the wizard, tooltip sweep,
  markdown tables + clickable links, empty states, thinking indicator
  fix.
- `03-performance.md` - startup phase timing, warm-up off the critical
  path, memory recall regression fix, long-conversation rendering,
  streaming markdown tail-rebuild, redundant server restart audit.
- `04-tech-debt.md` - test-registration guard, mechanical file splits,
  phonemizer dictionary cleanup, misc corrections.
- `05-roadmap.md` - version, sequencing, test requirements, security
  review touch, explicit rejections.

## How to work this pack

Same conventions as r1-r7 (see `docs/review/archived/`): every item has
acceptance criteria; check archived rounds before re-proposing anything
listed under explicit rejections; zero-warning builds
(`TreatWarningsAsErrors` solution-wide); tests run via
`dotnet test src/Aether.Tests/Aether.Tests.csproj` (see the
`build-and-verify` skill); no em dashes anywhere in code, comments, or
docs; the approval-gated agent security posture is non-negotiable.
Downloads added by this round (starter models, voice assets) must
follow the pinned-hash verification rules in the `security-posture`
skill.
