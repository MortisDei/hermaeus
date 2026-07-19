# Review round 16: Orchestration hardening, memory integrity, workbench truth

Written 2026-07-19 against v0.20.0-alpha (r15 implemented at 8b8cfcf).
Prior rounds live in `docs/review/archived/`; check each round's roadmap
"Explicit rejections" before proposing anything adjacent.

## Why this round

Three fronts, one theme: **what the app persists, recalls, and displays
must be true.** All findings below were verified in code (file:line refs
throughout); nothing here is speculative.

1. **Orchestration hardening (doc 01).** r15's sub-task orchestration was
   implemented by a different agent and has not had an independent audit
   until now (the r4 precedent: auditing the previous round's
   implementation found a full round of real issues). The audit found one
   stuck-forever failure mode, one bypass that can silently orphan an
   in-flight plan, and one pre-r15 workspace-identity gap that
   orchestration widens.
2. **Memory integrity (doc 02).** The memory subsystem has never had its
   own round. The audit found that "forget" does not actually forget
   (archived memories keep getting injected), the `[MEMORY: ...]`
   save-marker feature is a phantom (never wired into chat), and memory
   expiry is written but never enforced.
3. **Workbench and desktop truth (doc 03).** The recent-tasks list r15
   deferred (data layer shipped, UI did not exist to extend) gets built,
   which is also what makes doc 01's reconcile fix reachable and
   necessary. Plus a stale services indicator, an unconfirmed destructive
   delete, and converter/wheel-handler hygiene.

## Reading order

- `01-orchestration-hardening.md` - agent loop and sub-task fixes
- `02-memory-integrity.md` - memory store, markers, lifecycle
- `03-workbench-and-desktop.md` - recent tasks UI, status truth, hygiene
- `04-roadmap.md` - ship shape, sequencing, test expectations, rejections

## Ground rules (unchanged from prior rounds)

- Approval-gated agent security posture is non-negotiable. Nothing in
  this pack loosens a gate, widens an approval, or lets a child task
  inherit anything.
- Zero-warning build, sequential tests, no em dashes anywhere.
- JSON/SQLite changes must be additive (`SqliteMigrationRunner`,
  additive-only), with pre-r16 state files loading unchanged.
- User-visible behaviour changes update `docs/features.md`,
  `docs/agent.md`, `CHANGELOG.md`, and `docs/security-review.md` where
  relevant.
