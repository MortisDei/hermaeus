# Review round 23: Trust you can operate

Audience: the implementing agent. Read this file, then the numbered docs in
order. Doc 06 is the roadmap and sequencing contract.

## Why this round exists

The repository is almost public-facing, releases are tag-driven, and the owner is
daily-driving the Agent workbench. Three forces shape this round:

- **The trust story is strong but passive.** Every mutation is approval-gated
  and per-patch revert exists, yet the user experiences trust only as
  friction (clicking approve). This round adds the flagship active trust
  feature: a per-run change ledger and one-click revert of an entire agent
  run (doc 01). No local agent workbench ships "every run has an undo
  button"; Hermaeus will.
- **External review feedback arrived and most of it has merit.** A reviewer
  who read the docs (not the code) proposed execution plans, evidence
  exposure, workspace policy, confidence-based completion, and four
  honourable mentions; a second reviewer proposed agent scenarios 14-17 and
  flagged that `docs/security-review.md` has become three documents in one.
  Docs 02-05 adopt the pieces that survive contact with the codebase and
  explicitly reject the rest (rejection rationale lives in doc 06).
- **Process is maturing.** From this round forward, work lands via pull
  request (`docs/pull-requests.md`, one open PR per maintainer). r23 is the
  first round implemented on a branch and merged through a PR.

## Documents

| Doc | Theme |
| --- | --- |
| `01-run-ledger-and-task-rewind.md` | The killer feature: a PR-style ledger of everything a run changed, plus one-click, conflict-safe revert of the whole run |
| `02-execution-plan-and-completion-honesty.md` | Optional plan-approval checkpoint before autonomous runs; visible plan revisions; "completed with reservations"; surfacing the existing context receipt |
| `03-workspace-policy.md` | Deterministic read/write/never glob policy in `.hermaeus/workspace.json`, enforced at the tool layer as a new safety-gate input |
| `04-scenarios-and-approval-integrity.md` | Approval fingerprint binding (TOCTOU hardening), stated-lesson gate-claim filter, and scenarios 14, 15, 17 |
| `05-security-docs-and-process.md` | Split security-review.md into posture / history / roadmap; adopt the PR workflow for this round |
| `06-roadmap.md` | Ships as 0.30.0-alpha; sequencing, test budget, descope order, explicit rejections |

## Standing rules for the implementing agent

- Verify before implementing. File:line references were exact at spec time
  (tree at 74c0c00, v0.29.1-alpha); re-verify before editing.
- No em dashes anywhere. Zero-warning build. All tests pass. Register any
  new harness-style test methods in `XunitHarnessTests.HarnessCases`; the
  `HarnessRegistrationGuardTests` reflection guard fails otherwise.
- No new NuGet packages. Everything here is achievable with what is already
  referenced (SHA256 via `System.Security.Cryptography`, globs via
  existing matching helpers or `Matcher`-free custom code as used today).
- The safety gate stays deterministic. Nothing in this round may let a
  lesson, a memory, a workspace file, or model output widen what executes
  without approval. Policy (doc 03) may only narrow.
- Update `docs/features.md`, `docs/agent.md`, `CHANGELOG.md`, and the
  security docs per doc 05. Do not document planned behaviour as existing.
- This round lands via pull request per `docs/pull-requests.md`: branch
  `r23/round` from `main`, commit there, open the PR with the template,
  merge after CI is green. One open PR at a time.
