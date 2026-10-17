# 12. Planning validation and release gates

## Initial planning completion receipt

Date: 2026-10-16. Planning baseline and branch are in [01](01-current-state-and-evidence.md).
This receipt describes the original planning commit `01efd40`, before approved
implementation. Current results and owner decisions are recorded in
[13](13-execution-and-evidence.md); they do not rewrite this historical receipt.
This pack creates no R34 executable behaviour, tests, dependency, configuration,
schema, version, CI policy or repository setting. Current-reference prose
corrections document existing source; historical R33 packs are preserved.

Validation for this planning change:

- Read the complete final planning diff and current-reference corrections.
- Verify relative file links, document fragments, source-anchor bounds and
  important source symbols against the frozen baseline.
- Reconcile the 13-project and 64 package/version inventory with source and
  existing restore assets; identify native/package versus executed evidence.
- Review finding -> disposition -> package coverage, explicit P0 acceptance,
  prerequisite existence and acyclic ordering in document 11.
- Check every document uses planned versus current behaviour honestly, no
  fabricated speed/leak/vulnerability/platform-pass claims, no em dashes,
  no personal raw report data or generated executable artefacts.
- Run `git diff --check`, inspect staged file list and final status.

The final receipt below records checks actually executed, not merely this list.
No build, full suite or coverage run is required for this Markdown-only change,
as explicitly directed by the owner. The NuGet advisory query is read-only
dependency research; its exit/result and limitations are in [03](03-dependencies-and-native-assets.md).
No app, model, sandbox, inference benchmark or expensive CI workflow was launched.

## Release gates for future R34 implementation

All rows are **Unknown/not executed for R34**. Historical R33 evidence is linked
for provenance and cannot close these cells. Completion requires named receipts
with exact commit/package/host/protocol identities and raw evidence locations.

| Gate | Hard acceptance / evidence | Owner/package |
| --- | --- | --- |
| RG01 Build | Debug and Release solution builds, zero warnings/errors | G1, approved host |
| RG02 Automated behaviour | Full applicable sequential suite and representative original/sibling regressions; investigate skips/count drift; 60% canonical coverage at final authorised implementation precommit | G1 and each owner |
| RG03 Command safety | S1 timeout/cancel/read-failure/output-flood/descendant cases and native clean drain | S1/L2 |
| RG04 Ingest safety | Actual input/extraction/cumulative bounds and cancellation, old generation remains query-visible on failure | S2 |
| RG05 Approvals/containment | Prepared identity/preimage/policy/readback/recovery and queued/direct/rewind/owner-save/restore hostile cases; residual OS threat explicit | S4 |
| RG06 Secrets and data | Legacy/new vault reopen/tamper/migration/downgrade receipt; backup exclusion, restore rollback, migration and disk-full/cancel tests | S3/S4 |
| RG07 Native Windows | Installed/relocated exact package, GPU/CPU/runtime/Agent/RAG/Recall/Lab and keyboard/pointer/theme/font/DPI matrix | Owner, U1/G1 |
| RG08 Native Linux | Exact package on Pop!_OS/COSMIC, actual backend, portals/DBus/tray/activation/clean Quit plus GPU/CPU and product flows | Owner, U1/G1 |
| RG09 Runtime comparisons | Verified envelope and varied effective fields/kernel, exact assets/runtime, quality, actual released memory and reload cost | P3; unavailable experiment can defer, no unverified winner |
| RG10 Performance | Raw per-host R33/candidate measurements, same workload/fingerprint, explained variability/observer cost, no unexplained material regression | P1/G1, owner impact decision |
| RG11 Retrieval | Scope/archive/semantic fallback, ranking and citation generation/revision preserved; labelled corpus evals and scale/memory results | P2/G1 |
| RG12 View/process lifecycle | Covered obsolete views released, active callbacks once, stale zero; same-DC reload works; bounded shutdown/restart, no owned child after clean drain | L1/L2 |
| RG13 Packages/licences | RID-specific resolved graph/audit, native modules and SQLite version, notices actually delivered, hashes/layout/relocation/no-PDB checks | B0/G1; owner for unresolved asset terms |
| RG14 Documentation | Features, user guide and affected subsystem/security/workflow/upgrade/notices/CHANGELOG describe shipped state and limits; no planned behaviour presented as implemented | G1 |
| RG15 Publication | Exact PR/merge-context checks reviewed when owner chooses publication, explicit version/tag/release approval | Owner only; no workflow launched by this plan |

## Hard blockers, conditional acceptance and deferrals

**Hard blockers:** a known out-of-bound write/read, stale/unapproved mutation,
secret corruption/leak, unbounded selected input/output path, orphaned owned
child on a claimed clean path, falsely clean/ineligible evidence, failing build/
suite/coverage, lost retrieval quality without approved semantics, or unavailable
required Windows/Linux native acceptance. Do not call these Passed by reducing
assertions, removing functionality or adding broad catches.

**Conditional:** D1 deletions, D2 framework/retained-state changes, D3 tooltip/
framework trials, P2 codecs/embedding/extension experiments and P3 inference
features can end in Retain/Defer with documented evidence and no shipped claim.
An unsupported mechanism cannot become a fake control or an Apply winner.
Record a decision and affected gate; do not leave silent unfinished experiments.

**Explicitly deferred:** MXC/OpenShell production adoption, router integration,
private ternary runtime/fork adoption, SQLite vector extension dependency,
EmbeddingGemma 2 compatibility/adoption, signed distributions, backup manifest/
preview enhancements unless separately selected, and native macOS validation.
Existing fallback-vault UI visibility/provider redaction fixtures are covered
only where S3/S4 touches them; larger UI/security feature additions need a
concrete owner-approved scope. Workspace policy editor, MCP HTTP/SSE and Agent
HTTP execution remain deferred. PAIR/distributed inference, a second authority,
browser frontend/replacement editor and automatic routing are rejected for R34.

## Unresolved evidence and owner decision ledger

| Gap/decision | Status now | Resolution / consequence |
| --- | --- | --- |
| Current installed runtime/assets/hardware and matching R33 performance data | Windows identity and PDF compatibility observations recorded; comparable per-host performance baseline remains open | B1/P1 need controlled same-host measurements. No speed/VRAM win is claimed |
| Actual child survival after caller cancellation | Reproduced in controlled Windows fixtures, bounded cleanup repaired and regression-tested | See 13; best-effort termination does not guarantee containment or every descendant's exit |
| View retention, stale callback delivery, same-DC reattach | Reproduced defects repaired, controlled native/weak-reference regressions passed; partial Windows walkthrough in 15 | Complete Windows/COSMIC interaction matrix remains open |
| Final RID/native loading/licence delivery | Development Windows/Linux archives and SQLite identities inspected; notice-copy repair verified | See 13/15; final integrated archives, complete upstream texts and unresolved asset terms remain gates |
| Ingest ceilings/override semantics | Owner-approved 512 MiB source, 16 Mi characters, 2,000 pages, 256 MiB accounted staging payload implemented | Representative supplied PDFs fit. No hard parser timeout or whole-process memory guarantee; later user-visible changes require approval |
| Vault format and old-build downgrade | Conditional implementation approval, exact contract in 14; inactive implementation and Windows/Linux synthetic evidence in 15 | Production activation requires remaining native/provider evidence and explicit owner recovery/downgrade acceptance |
| Network aliases/env/preset hard policy | Core `--host` already rejects conflicts; broader audit open | S4 exact runtime/listener proof, owner approves any policy expansion |
| Sandbox release/backend security guarantee and native Windows parity | Research only | Exact release/OS/backend hostile proof plus owner dependency/security decision; otherwise Defer |
| Public Moss pet redistribution/licensing | Existing unresolved provenance issue | Owner resolves before any applicable distribution claim; this plan changes no legal model |
| Boro primary-source identity | Unknown | Defer without blocking; no invented product or capability |
| Minimise-to-tray/disable-reenable and full screen matrix | Partial Windows navigation and owner-assisted clean tray Quit verified in 15 | Remaining Windows/COSMIC interactions remain explicit U1/G1 gates |
| Package signing and release/version/settings policy | Owner controlled | No action until separately authorised |

## Final planning check receipt

**VE, 2026-10-16:** reviewed all 13 R34 documents and the eight existing-reference
corrections against the unchanged source baseline. An external scratch
PowerShell check returned zero issues for:

- 194 relative link occurrences, including eight Markdown heading fragments
  and 38 source-line anchors within file bounds. Important symbols and the
  numbered source index were also inspected directly.
- All 13 project/direct-package/project-reference records, 64 resolved
  package/version pairs and their direct/transitive consumers; all 13 restore
  library counts, net10.0-only targets and SHA256 fingerprints matched.
- All 15 work-package prerequisites matched the 29 graph edges; topological
  ordering found no cycle. B0/S1/S2/G1 each have explicit P0 acceptance.
- 20 adversarial cases, 32 technology decisions, and Markdown-only changes
  without em dashes. Manual coverage review maps findings to work or explicit
  Retain/Defer/Reject dispositions; no speed, leak or platform-pass claim was
  inferred from a planned test.

`git diff --cached --check` passed; the staged scope review contained exactly
21 Markdown files, with no executable, configuration or generated artefacts.
The local commit contains only this pack and bounded current-documentation
reconciliation. Commit identity and post-commit status
are reported in the handover, avoiding a self-referential hash. Implementation
and all future runtime/native acceptance remain open. No build/full suite or
coverage was run, as directed for this planning-only change.
