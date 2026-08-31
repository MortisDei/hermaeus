# 09. Adversarial reconciliation and CI topology

This reconciliation treats the independent review points as claims. Each item
below was checked against `b034182`, the uncommitted R32 pack, current primary
upstream contracts, and, for CI, the live repository ruleset and PR history on
2026-08-31. No separate review artifact was available in the workspace, so the
claims explicitly supplied for reconciliation are the complete claim set used
here.

## 9.1 Accepted contract corrections

| Claim | Verified evidence | Planning resolution |
| --- | --- | --- |
| Launch intent and precedence were ambiguous | legacy `GpuLayers` cannot express Auto; ordinary Start silently applies/saves tune profiles; core `ExtraArgs` can collide; upstream fit defaults on and respects explicit placement pins | doc 02 now fixes legacy migration, removes implicit tune-profile application, defines configured/evidence/Hermaeus/upstream/rendered/effective/observed precedence, and makes Batch 0 verification-only |
| Resource admission could be bypassed | Services, isolated Lab, restart/resume, and lazy in-process session owners allocate independently | docs 01-02 require a lease at every production allocation owner and architecture guards against an unleased callable path |
| Temporal writes could bypass revisions | production callers use generic `IMemoryStore.SaveAsync` upsert | doc 04 establishes one expected-current revision command authority and demotes generic row writes to internal migration/test plumbing |
| Deletion, privacy, backup, and encryption claims were too strong | content is plaintext despite an encryption marker; backups copy databases; CSV exports escape active-store deletion; SQLite deletion is not physical erasure proof | doc 04 promises transactional active-store deletion only, discloses plaintext at rest and prior-copy limits, and requires verified SQLite/WAL/filesystem handling before any physical-scrub claim |
| RAG publication was not failure-safe | current source chunks are deleted before parent/child replacement completes; file identity and embedding batches are not fully revalidated | doc 04 defines staged immutable source revisions and dataset generations, cardinality/dimension/finite validation, source revalidation, atomic current publication, and startup/cancellation scavenging |
| Consumer/device ownership was underdefined | current subsystem-specific lifecycles do not share allocation/component identity | doc 01 defines one allocation owner, child components, snapshot-scoped device ids, reservations, lifecycle state, and non-additive observation authority |
| Recommendation persistence was left open | empirical evidence has different immutability semantics; settings plus SQLite cannot commit atomically | doc 03 chooses normalized recommendation tables in `experience.db` through the Services migration runner and defines pending/apply/reconcile/undo records without automatic reapplication |
| Watched-root identity and embedding cardinality were incomplete | absolute case-insensitive paths and prefix containment are wrong on Linux; embedding indexing assumes one result per input | doc 04 uses stable watch-root plus platform-aware relative identity, descendant containment/symlink checks, and complete generation validation |
| Artwork acquisition and decode needed a hard boundary | `cardData.thumbnail` is publisher controlled; current HTTP redirect/decode behavior is not a safe decorative-media contract | doc 05 pins repo/revision provenance, manually validates exact current delivery hosts, strips credentials, bounds streaming, and preflights dimensions/pixels before decoder construction |
| Adaptive retry could reuse stale workload state | a failed allocation changes owned/external resource state | docs 01-02 require teardown/release, a fresh snapshot, and revalidation/reservation before the next deterministic candidate |
| Deleting Current could resurrect older content | naive current projection can select a predecessor after successor removal | doc 04 sets the current pointer to null; restoration is a reviewed command that creates a new revision, never implicit promotion |
| Evidence freshness precedence was vague | recommendation sources have different clocks, compatibility, and authority | doc 03 fixes target drift, deletion, contradiction, expiry, insufficiency, and actionable precedence plus evidence-basis ordering |
| README Batch 0 reference was ambiguous | it named docs without identifying doc 08 as the roadmap | the README now names docs 01-02 and doc 08 Batch 0 explicitly |
| Failure paths and continuation boundaries were too soft | several plans emphasized success acceptance while schema/cache/publication operations cross cancellation and crash seams | owning batches now carry failure tests; doc 08 defines atomic landing units and forbids using Batch 12 as a catch-all |
| Same-repository PR CI is duplicated | `ci.yml` runs identically named matrices for `r*/round` pushes and `main` pull requests; PR #9 contains both for the same SHA; the live main ruleset requires the two exact matrix names | section 9.3 defines distinct pre-PR branch feedback and authoritative PR checks without changing repository rules |

## 9.2 Narrowed or rejected expansion

No supplied correctness claim was rejected outright. The following implied
expansions are unsupported by the evidence and are deliberately narrower:

- R32 does not add content encryption merely to make temporal history sound
  safer. It removes the false implication and records encryption as absent.
- `Hard delete` means transactional removal from the active logical store and
  its content-bearing dependents. It cannot revoke user-created exports,
  backups, filesystem snapshots, or already overwritten storage sectors.
- Artwork does not accept arbitrary `thumbnail` hosts or a remote wildcard
  allowlist. It starts from pinned `huggingface.co` repository provenance and
  permits only exact, reviewed Hugging Face delivery hosts.
- The first PR run may repeat a head commit previously checked before the PR
  existed. That is justified because the required PR check tests the merge
  context. The waste to remove is the simultaneous branch-authority matrix on
  later pushes while that PR exists.
- Multi-device execution is not claimed from simulated devices. The ownership
  model and capability surface land; execution remains Unknown without real
  hardware.
- The audit does not justify a generic process hierarchy, universal optimizer,
  GraphRAG, automatic model routing, broad ViewModel refactor, new encryption
  subsystem, or transactional coordination across unrelated databases.
- A narrowly justified image parser/decoder dependency is permitted only if
  existing facilities cannot enforce limits before unsafe allocation. This is
  not permission for unrelated package churn.

## 9.3 CI trigger and required-check contract

Current facts at the pre-change audit baseline were:

- `.github/workflows/ci.yml` ran on `push` to `main` and `r*/round`, and on
  `pull_request` to `main`;
- both events published `build-and-test (ubuntu-latest)` and
  `build-and-test (windows-latest)`;
- the active `main` ruleset strictly requires those exact two GitHub Actions
  checks and a pull request, with no approval count requirement;
- GitHub treats a skipped job as successful, so keeping the required name on a
  no-op branch/PR job could falsely satisfy protection;
- the trusted-push-only Windows Defender exclusion is an intentional security
  boundary and is not generalized to pull requests.

The implemented workflow change removes `r*/round` from `ci.yml`. Those pushes
now use `branch-ci.yml`, which checks for an exact open same-repository PR and
otherwise runs only the distinct non-required `branch-build-and-test` matrix.
The required names remain in `ci.yml` for `main` pushes and `main` pull
requests. The live ruleset still has the required names and pull-request
requirement recorded above; a post-change required-check attachment exercise
remains an owner-only live gate.

The skipped-job and required-name constraints follow GitHub's current
[status-check contract](https://docs.github.com/en/pull-requests/reference/status-checks)
and [ruleset troubleshooting guidance](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/troubleshooting-rules).

R32 changes the workflow file only, not repository settings:

1. The required names remain exclusive to `pull_request` merge-context runs
   targeting `main` and post-merge `main` pushes. Fork/external pull requests
   receive the same required PR matrix.
2. Development-branch pushes first run a small read-only gate that asks GitHub
   whether an open same-repository PR exists for that exact head repository,
   branch, and `main` base. It uses only `contents: read` and
   `pull-requests: read`.
3. With no matching PR, run a distinctly named non-required matrix such as
   `branch-build-and-test (ubuntu-latest)` and its Windows peer. Preserve the
   branch patterns the project actually uses, including `r*/round`; adding
   `fix/**` or `docs/**` requires confirming they are intended CI branches, not
   guessing from naming alone.
4. With a matching PR, skip only that non-required branch matrix. The required
   PR matrix is authoritative. Never emit a skipped job under either required
   name as the de-duplication mechanism.
5. Give branch-push, pull-request, and main-push runs separate concurrency
   groups. Cancel superseded runs for the same development ref or PR number,
   but never let a branch event cancel its PR authority and never cancel a main
   run merely because a newer unrelated main run exists.
6. Preserve the Defender exclusion only on the trusted push paths where it
   already applies. Re-measure duration after de-duplication; do not weaken the
   fork/PR boundary for speed.

Implementation must re-read the live ruleset before editing because required
check names are external mutable state. If they differ, stop and revise this
mapping rather than changing repository settings. Automated tests/guards cover
event/name/gate/concurrency semantics, and one draft test PR or equivalent
owner-approved live exercise proves required checks still attach to the latest
merge SHA. Planning does not authorize that live mutation.

## 9.4 Remaining Unknown and live gates

Only environment-dependent facts remain:

- exact argument spelling/help/effective-placement reporting of every installed
  managed llama.cpp variant on Linux and Windows;
- real CUDA/Vulkan/WDDM memory attribution and pressure behavior;
- multi-device placement without a suitable two-device host;
- pinned reranker graph batch support and measured benefit;
- real artwork responses across current Hugging Face delivery backends, within
  the exact primary-source host set;
- desktop layout/accessibility and platform cache-clear behavior;
- a live CI event/ruleset exercise after the workflow-only change is authorized.

Batch 0 may narrow a capability or mark it Unknown when these facts fail. It
may not reopen launch precedence, mutation authority, admission, revision
publication, persistence ownership, privacy truth, or CI authority as casual
implementation choices.
