# 05. Security docs split and the PR workflow

## 5.1 Split docs/security-review.md into posture, history, roadmap

External feedback, accepted: the file (931 lines at 74c0c00) is three
documents interleaved. Current posture, per-round change history (the
`### r8:` through `### r21:` sections are over half the file), and
rationale for rejected alternatives all live under one title, and a reader
has to filter "is this a current control or historical context?" line by
line.

Target layout:

- **`docs/security-review.md`** stays the entry point and keeps: the
  threat model (Assets, Trust Boundaries, In-Scope Threats, Out of
  Scope), Reviewed Controls, the Threat Scenarios sections rewritten
  where needed so every statement is present tense and currently true,
  Release Gate Status, and the Validation Checklist. Add a short header
  pointing to the other two files.
- **`docs/security-history.md`**: the r8 through r21 (and now r23)
  sections move here verbatim under a heading per round, newest first.
  History is append-only; rounds add a section here instead of growing
  the posture doc. Where a historical section contains the only statement
  of a *current* control, copy the control into the posture doc rather
  than leaving it stranded in history.
- **`docs/security-roadmap.md`**: remaining hardening items. Harvest
  these from the existing text (any "future work", "not yet", "deferred"
  statements in the current file) plus open items this round creates.
  Each entry: what, why it matters, rough trigger for doing it. Do not
  invent new commitments while harvesting; if it is not already implied
  by the review or this round, it does not go in.

Mechanics:

- Move, do not rewrite, the history sections (byte-identical except
  heading level adjustments); rewriting history invites accidental
  falsification. The posture rewrite is where editorial judgment goes.
- `grep -rn "security-review" .` and update every reference (README,
  docs cross-links, `.claude/skills/security-posture/SKILL.md`,
  CONTRIBUTING, templates) to point at the right one of the three files
  for its purpose. The PR template's security line points at the posture
  doc.
- r23's own security-relevant deltas (approval fingerprint, stated-lesson
  filter, workspace policy, rewind) are written directly into the new
  structure: controls into the posture doc, the round narrative into
  history, anything deferred into the roadmap.

## 5.2 First round through the PR gate

Process shipped ahead of this round (already on main):
`docs/pull-requests.md`, `.github/PULL_REQUEST_TEMPLATE.md`, and the
CONTRIBUTING update. This round is the proving run:

- All r23 work happens on branch `r23/round`; nothing lands on `main`
  directly.
- One PR for the whole round (per `docs/pull-requests.md`, review-round
  branches merge with a merge commit so the commit sequence documents the
  round). Fill the template honestly, including the Security notes
  section, which this round actually has content for.
- CI must be green on the PR before merge. After merge, the owner tags
  `v0.30.0-alpha` from `main` per the release policy.
- If the PR process itself fights back (CI differences on the branch,
  template friction), record what happened in the final response; that
  feedback is the point of a proving run.
