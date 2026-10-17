# R34: Harden, Simplify, Accelerate

Planning date: 2026-10-16 (Australia). Branch: `r34/round`.
Source baseline: `23cec510c86a65d71c0dbffeaebe31b33973c8f5`, released as
`v0.41.0-beta` (R33). **Plan approved; implementation in progress.**

One authority. One representation. One lifecycle. Prefer framework capability,
delete redundant code, and measure before optimising. Research is not adoption.
This pack defines bounded work and exit gates, not shipped R34 behaviour or a
release version. The owner controls dependencies/security-boundary changes and
all publication. Small verified local implementation commits are authorised; publication is not.

## Read order

| Document | Purpose |
| --- | --- |
| [01-current-state-and-evidence.md](01-current-state-and-evidence.md) | Verified baseline, inherited guarantees, source anchors and evidence limits |
| [02-architecture-framework-and-deletion.md](02-architecture-framework-and-deletion.md) | Authority map, framework audit, deletion register |
| [03-dependencies-and-native-assets.md](03-dependencies-and-native-assets.md) | All 13 projects, direct/resolved dependencies, native assets and licensing |
| [04-security-and-adversarial-acceptance.md](04-security-and-adversarial-acceptance.md) | Threat model, exposure priorities, sandbox comparison and hostile cases |
| [05-runtime-and-inference-research.md](05-runtime-and-inference-research.md) | Actual runtime baseline, upstream delta, inference experiments |
| [06-retrieval-and-storage-efficiency.md](06-retrieval-and-storage-efficiency.md) | Recall, RAG, embeddings, SQLite and relevance gates |
| [07-performance-baselines.md](07-performance-baselines.md) | Repeatable per-host baselines, measurements and evidence retention |
| [08-lifecycle-and-concurrency.md](08-lifecycle-and-concurrency.md) | View ownership hypotheses, reproduction matrix, process recovery |
| [09-moss-native-acceptance.md](09-moss-native-acceptance.md) | Inspected screenshots, missing evidence, native UX acceptance |
| [10-technology-decisions.md](10-technology-decisions.md) | Explicit dispositions and dated primary research sources |
| [11-dependency-roadmap.md](11-dependency-roadmap.md) | Authoritative implementation packages, dependencies and acceptance |
| [12-planning-validation-and-release-gates.md](12-planning-validation-and-release-gates.md) | Planning checks, release blockers, conditional/deferred gates |
| [13-execution-and-evidence.md](13-execution-and-evidence.md) | Actual implementation baseline, results and remaining gates |
| [14-vault-format-decision.md](14-vault-format-decision.md) | Conditionally approved S3 format/recovery contract; production activation remains gated |
| [15-native-and-vault-evidence.md](15-native-and-vault-evidence.md) | Windows walkthrough and separately verified inactive S3 implementation |
| [16-notice-delivery-and-integration.md](16-notice-delivery-and-integration.md) | Actual upstream-text archive delivery and its integration evidence; unresolved legal/native gates remain |
| [17-backup-credential-artifact-privacy.md](17-backup-credential-artifact-privacy.md) | Later credential artifact, backup path and snapshot privacy repairs and verification |

## Evidence vocabulary

Reuse R33's vocabulary:

- **RF, repository fact:** inspected source/configuration at the baseline.
- **VE, verified execution:** a command/test/runtime result actually observed
  in this planning pass. Record the command, date and limits.
- **OO, owner observation:** a named historical or new owner receipt. It is
  neither a fresh automated result nor proof of the proposed root cause.
- **UF, upstream fact:** dated primary documentation/source. It does not prove
  the installed binary or a benefit on either target GPU.
- **IN, hypothesis/inference:** requires the named reproduction.
- **P, proposed/recommendation:** work or acceptance for R34, not current behaviour.
- **U, Unknown:** missing/unavailable evidence, never Passed, zero or supported.

Retain R33 closure labels (`VERIFIED EXISTING`, `OWNER PASS`,
`NEEDS OWNER VALIDATION`, `UNRESOLVED`). Execution receipts use `FIXED` only
for exercised defects with source and automated evidence, never whole-round
or release acceptance.
Technology dispositions are Retain, Simplify, Replace, Defer or Reject; a
conditional Replace decision requires its stated evidence before deletion.

## Implementation spine

Start with **B0 minimum source/build/security evidence**, then **S1 command lifetime/output bounds**
and **S2 ingest bounds**, while **L1 view reproduction** and **D1 small deletions**
can proceed independently. Vault migration S3 and containment audit S4 follow
the baseline. B1 per-host performance/native/package evidence gates claims and release.
Measure P1 before selecting P2/P3 optimisation. Run U1 native
polish after ownership/framework choices settle. G1 integrates verification and
owner Windows/Linux acceptance. The exact DAG and owner decision points are
in [11](11-dependency-roadmap.md); phase numbers are workstreams, not waterfalls.

No new vector database, runtime authority, lifecycle framework, approval system,
browser frontend, replacement editor, distributed inference or multimodal
feature expansion is selected.
