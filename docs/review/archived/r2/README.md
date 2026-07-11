# Review Round 2 (r2) - July 2026

Second principal-engineer review, following the fully actioned r1
(now in `archived/r1/`). r1's roadmap has no open items at any horizon;
this round audits the code that landed since (v2.0 and v3.0 waves:
Aether.LocalApi, Aether.Mcp, Aether.Voice, agent command execution,
Aether.Composition, the ViewModel orchestrator extractions) and defines
what "next level" means from here.

Read in order:

1. [Code Audit](01-code-audit.md) - concrete defects and risks found in the
   post-r1 code, ranked by severity, each with file references and
   acceptance criteria. Written to be actionable by an implementing agent.
2. [Architecture Assessment](02-architecture-assessment.md) - the structural
   state of the codebase after the v3.0 extraction pass: what worked, what
   residue remains, what to leave alone.
3. [Next-Level Roadmap](03-next-level-roadmap.md) - the r2 roadmap: hardening
   first, then the deferred provenance/API/agent work in dependency order,
   with explicit rejections.

Headline verdict: the r1 conclusions held. The four unifications did their
job; the v2.0 features (workspace manifest, gated command execution, MCP,
local API, native voice) landed with the security posture intact and with
tests. The defects found this round are seam-level, not architectural:
one phantom setting, a handful of process/stream handling bugs in the new
MCP and agent code, and JSON hygiene in the local API trace path. Nothing
requires a redesign. The next level is: fix the audit list, then ship the
deferred provenance and per-app-token work that turns the local API from
a demo into infrastructure other apps can trust.

**Status: fully actioned as of `0.9.42-alpha`.** Every item in docs 01 and
03 is DONE; see 03's per-item notes for the handful of places where
implementation surfaced a real gap beyond what was originally scoped
(memory injection existed but nothing called it; the MCP bridge never
checked a tool was actually declared before forwarding it) and the two
Phase 4 items that are manual/CI practice rather than code
(first-run VM walk, suite-time watch). r3 should be run against real usage
traces, not code alone.
