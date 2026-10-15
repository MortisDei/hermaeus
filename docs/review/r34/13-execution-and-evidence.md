# 13. Execution and evidence

## Starting state and approved refinement

2026-10-16: `r34/round`, clean at
`1984384e680dbb3219ee29d7a8807d5002ec21f6`. The additional owner commit archives
R33 without changing source. Released source baseline remains
`23cec510c86a65d71c0dbffeaebe31b33973c8f5`. Active links now follow the archive;
historical packs are not rewritten.

The approved plan now separates B0 minimum reproducible source/build/security
evidence from B1 per-host performance/native/package evidence. It explicitly
investigates Job Object/group/session ownership, gates new ingestion ceilings
on legitimate workload evidence and owner approval, isolates owner-gated vault
migration, and permits evidence-backed conditional Retain/Defer dispositions.
Notice delivery and unresolved asset licensing remain release gates.

## B0 minimum baseline: VERIFIED EXISTING

All commands below ran on the approved Windows host before production repairs:

| Evidence | Actual observation |
| --- | --- |
| Source/graph | Clean start above; 13 projects; Agent depends on Core, not Services. No dependency/configuration changes |
| Host | Windows `10.0.26300`, `win-x64`; .NET SDK `10.0.113`, runtime `10.0.13`, MSBuild `18.0.36` |
| `dotnet build Hermaeus.sln` | Exit 0, zero warnings/errors |
| `dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj --no-build --no-restore --results-directory "$env:TEMP\hermaeus-r34-baseline" --logger "trx;LogFileName=baseline.trx"` | Exit 0; 2,871 passed, zero failed/skipped; duration 3m52s |
| `dotnet list Hermaeus.sln package --vulnerable --include-transitive --no-restore --format json` | Exit 0; NuGet source named, all 13 projects, no vulnerable-package entries/errors. Advisory observation, not complete native/external-runtime security proof |

Baseline suite includes existing Agent, RAG, containment and native-control
regressions. No owner app, settings editor, model or sandbox was launched.
B0 unlocks S1/S2/L1. It does not close B1, any repair, Linux execution, performance
comparability or native owner acceptance.

## Work in progress

New red-first regression cases cover approved recipe cancellation, bounded web
reads, probe cancellation/redirected stderr, and native Services/Settings view
ownership. Until their results are recorded, these remain reproduction targets.
Test additions and a behaviour-preserving internal telemetry extraction do not
count as repairs. Full verification and coverage are required before committing
behaviour changes.

## Explicit pending decisions and limits

- **S2 budgets:** tracked supported text/source fixtures total 1,227 files,
  11,980,577 bytes and 11,968,165 decoded .NET characters; largest file is
  `AgentService.cs`, 228,907 bytes. Docs plus root README/CHANGELOG are 263 files,
  2,920,917 bytes. There are no tracked PDFs. These legitimate workloads do not
  validate 50 MiB local/16 Mi-character/2,000-page/256 MiB staging proposals.
  Local/PDF/cumulative changes await representative workloads and owner approval.
  Existing 2 MiB web acceptance can be enforced while streaming independently.
- **S3:** no new vault format or migration implementation is authorised until
  exact format, atomic recovery and downgrade semantics are approved. Retain
  current format meanwhile; reversible recovery must state technical limits.
- **S1 ownership:** the shared Services Windows job is post-start, process-wide
  and unsuitable for per-command cancellation. Linux has no current group/
  session launcher. Best-effort tree termination is not containment, and waiting
  for a root process is not proof every descendant exited. Native launch design
  changes need explicit review before a new boundary is introduced.
- **B1/G1:** both target-host baselines/native workflows, actual RID archives,
  delivered notices and unresolved asset redistribution terms remain open.
