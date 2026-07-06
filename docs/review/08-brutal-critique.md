# Brutal Critique — pre-public-release review

Written as if hired to say what friends won't. The codebase earns real praise
below; the criticism is proportionate to how good the foundations are.

## The headline

Aether is a well-engineered collection of vertical features in search of a
product spine. Every subsystem is individually defensible — and that is
exactly the problem. Chat memory, workspace memory, workspace profiles,
Doctor, Trust scan, Privacy Audit, chat traces, RAG traces, agent traces,
Compare Models, Benchmarks: I count at least five pairs of features that are
the same idea implemented twice. Your own philosophy says "features should
compose naturally rather than becoming isolated modules." By that standard,
the memory and inspection features currently fail your own constitution.

## Feature creep, named

- **Tasks/reminders/automations.** A to-do list inside an AI workstation.
  It composes with nothing, competes with the OS, and every hour spent on it
  is stolen from the agent. Cut it or rebuild it as agent scheduling; do not
  keep it as-is.
- **The benchmark suite** has rankings, per-case modals, bulk timestamped
  exports, three export formats. This is a hobby that escaped into the
  product. Users need one answer — "which of my models for this job" — not a
  reporting platform.
- **Four voice providers across two Python versions.** You built a Python
  distribution-management subsystem (venvs, GPU detection, generated scripts,
  health validation) to support TTS backends most users will never switch
  between. This is the single worst maintenance-cost-to-value ratio in the
  repo, and it drags a shadow dependency your dependency philosophy would
  never have approved as a NuGet package.
- **A file browser inside the agent panel.** Every OS ships a better one.
- **Organizational everything in chat:** folders *and* tags *and* pins *and*
  archive. Pick two.

## Where the discipline slipped

- The zero-warning, minimal-dependency, atomic-write discipline is real and
  admirable — and then the test story is a custom runner with ~3.5k lines of
  mostly integration tests, and the security-critical logic (path boundaries,
  risk classification, redaction regexes) is exactly where dense unit
  coverage is thinnest. For a product whose brand is "auditable and safe,"
  the tests are the audit. This gap is the least forgivable one pre-release.
- 25 interfaces with one implementation each is cargo-cult SOLID in a
  codebase that is otherwise refreshingly direct.
- README says 0.9.4; the build says 0.9.16. A trust-branded product cannot
  ship docs that are wrong about itself.

## What is genuinely elegant (and why)

- **The read-first agent contract.** Deterministic risk classes, baseHash
  stale-file protection, approval queues, JSONL traces, and — rarest of all —
  *documented non-goals*. Most agent products define themselves by what they
  can do; Aether defines itself by what it will not do without permission.
  That is a durable idea, not a feature.
- **The refusal path in RAG.** Declining to answer on weak context is the
  kind of anti-feature that only a confident design ships.
- **Attachment handling.** Read once at send, bounded block, summary in
  history, paths persisted for regenerate. Small design, no flaws.
- **Storage humility.** SQLite + versioned migrations + atomic writes + JSON
  source-of-truth with DB index. No ORM, no event sourcing, no cleverness.
  This will still be maintainable in 2036.
- **The layering.** ViewModels genuinely free of Avalonia. Most MVVM
  codebases claim this; this one did it.

## The strategic error to avoid

The temptation now is to keep shipping vertical features because each is easy
on these good foundations. Resist it. The gap between Aether and its
competitors is not feature count — you will lose any feature race against
VC-funded teams and first-party model vendors. The gap is that Aether can be
*trusted and inspected*, natively, on the user's machine. Every roadmap
decision should be scored against one question: **does this make Aether's
knowledge more unified or its actions more accountable?** Tasks/reminders
fails that test. The benchmark reporting suite fails it. Unified memory,
the check registry, context-pack provenance, and gated execution pass it.

## Bottom line

Ship 1.0 smaller than the current build, not larger. Cut or freeze the items
above, close the test gap on the security surface, fix the docs, and spend
1.x deleting duplicate concepts. The foundations deserve it: this is one of
the few codebases I've reviewed where the ten-year version is *simpler* than
the current one, and that is high praise.
