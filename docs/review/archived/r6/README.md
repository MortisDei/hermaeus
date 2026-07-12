# Review Round 6 (r6)

Theme: **answerability**. A developer opening Aether for the first time
should, within five minutes, be able to answer:

1. Where is my data stored?
2. Can anything leave my machine?
3. Which model answered this?
4. Why were these files selected?
5. Why did retrieval choose this chunk?
6. Why was this patch flagged as risky?
7. Can I undo everything?

If those answers are obvious, Aether is differentiated from most AI IDEs.
r6 closes the gaps between "the data exists somewhere" and "the answer is
visible where the question arises".

The security review (`docs/security-review.md`) was refreshed for
`0.10.0-alpha` during r6 planning; its two new follow-ups (recipe
transparency, lesson review moment) are specced here as items 3.2 and 3.3.

## Documents

- `01-first-five-minutes.md` - usability: one item per question above,
  with the verified current state and acceptance criteria.
- `02-usage-history-recommendations.md` - the item r5 deferred: local
  model-usage rollup counters feeding usage-aware benchmark insights.
- `03-platform-cleanup.md` - InspectionEngine dead-code resolution and
  the security-review follow-ups.
- `04-roadmap.md` - version, sequencing, test requirements, explicit
  rejections.

## How to work this pack

Same conventions as r1-r5 (see `docs/review/archived/`): every item has
acceptance criteria; check archived rounds before re-proposing anything
listed under explicit rejections; zero-warning builds; the custom test
runner in `src/Aether.Tests` (see `build-and-verify` skill); no em dashes
anywhere; the approval-gated agent security posture is non-negotiable.
