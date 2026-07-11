# Architecture & Strategy Review — July 2026

Principal-engineer review of Aether pre-1.0, covering the next several years
of evolution. Read in order; later documents reference earlier ones.

1. [Architecture Review](01-architecture-review.md) — strengths, weaknesses, ranked risks
2. [Dependency Review](02-dependency-review.md) — every package audited; containment strategy and reduction roadmap
3. [Architectural Opportunities](03-architectural-opportunities.md) — twelve candidates, each adopted, deferred, or rejected with reasons
4. [Vision](04-vision.md) — what Aether 2.0 is, who it serves, why it wins
5. [Feature Audit](05-feature-audit.md) — every feature rated Essential/Refine/Over-engineered/Merge/Deprecate
6. [Technical Debt](06-technical-debt.md) — ranked debt register
7. [Roadmap](07-roadmap.md) — 1.0 / 1.x / 2.0 / long-term, architecture-first
8. [Brutal Critique](08-brutal-critique.md) — the unvarnished pre-release assessment
9. [System Map](09-system-map.md) — spine/capabilities/tools buckets, the Context and Evaluation System unifications, and the dual-ownership register
10. [Evaluation System](10-evaluation-system.md) — design for folding Benchmarks, Compare Models, and the RAG eval harness into one engine with three projections

The recurring conclusion: Aether's foundations are strong; its risk is
parallel vertical features. Four unifications (memory scopes, check/fix
registry, context-pack builder, provider capability model) resolve most of
the audit findings and make the 2.0 vision mostly wiring.
