---
name: review-round
description: The current Hermaeus process for planning, implementing, verifying, and closing numbered review rounds.
---

# Review rounds

Numbered review/spec packs remain the planning mechanism. Active packs live in
`docs/review/rNN/`; archived packs under `docs/review/archived/rNN/` are process
evidence and historical records. Do not rewrite archived packs to match the
current workflow.

## Before editing

- Read the active `README.md` and roadmap. The roadmap's numbered sequencing
  and explicit rejections define implementation scope for that round.
- Revalidate every cited source line, current-facing doc, architecture rule,
  and test seam against the current working tree. A pasted handoff or old pack
  is evidence to inspect, not authority over current source.
- Group defects by shared root cause, repair the shared primitive or policy,
  audit bounded sibling paths, and do not redesign healthy areas.
- Keep security, privacy, dependency direction, settings placement, and
  documentation requirements from `AGENTS.md` in force.

## Implementation and closure

The evidence sequence is bounded implementation, focused regression tests,
full sequential verification, truthful docs, owner live gates where source
inspection cannot prove desktop behaviour, correction of owner findings, final
closure evidence, and then an owner-authorized commit. A live gate does not
replace unfinished audits, tests, builds, or documentation work.

Record distinctions explicitly: FIXED means source and automated evidence are
complete; VERIFIED EXISTING means the round confirmed an existing behaviour;
OWNER PASS is live owner evidence; NEEDS OWNER VALIDATION is not a claim of a
GUI pass; UNRESOLVED is a genuine remaining defect or runtime boundary.

Update `docs/features.md`, the relevant workflow document, and `CHANGELOG.md`
when behaviour changes. Do not document planned behaviour as existing. Use
the final `docs/review/rNN/` closure document for current evidence without
erasing prior owner-gated or unresolved findings.

## Publication ownership

The owner controls branch publication decisions, PR merge, versioning, tags,
releases, repository settings, and visibility. Do not assume a release version,
alpha tag, or automatic archive step merely because a round is numbered. Follow
the current PR workflow in `docs/pull-requests.md`: one open PR per maintainer,
reviewable scope, truthful docs, and the appropriate branch. Commit only after
explicit authorization. Use a Conventional Commit-style subject and body,
record correctness/security/privacy semantics and verification, and never add
an AI co-author trailer.
