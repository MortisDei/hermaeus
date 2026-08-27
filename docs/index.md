# Hermaeus documentation

This is the map for the documentation tree. You should not need to know the
review-round names to find current product behavior.

## Authority

The implementation is the source of truth for behavior. These documents have
different jobs:

- `README.md` is the public product entry point.
- `docs/features.md` is the concise current capability catalogue.
- `docs/user-guide.md` describes release-user workflows and recovery paths.
- The subsystem references below are the detailed current homes for their
  topics. The catalogue and user guide summarize them and should link here
  rather than become competing manuals.
- `CHANGELOG.md` records release and unreleased change history. It is not a
  behavioral specification.
- `docs/review/` contains implementation contracts, evidence, decisions,
  rejected alternatives, and round history. It is not current product
  authority. Existing review and round records are intentionally preserved.

When a branch contains unreleased work, current-facing documentation describes
only behavior verified in that checkout. The unreleased delta remains visible
in [`CHANGELOG.md`](../CHANGELOG.md)'s `Unreleased` section and in the active
review material. A review plan or roadmap never proves that a feature shipped.
Check `Directory.Build.props` and the changelog for release status.

## Start here

| Need | Read |
| --- | --- |
| Understand the product | [`README.md`](../README.md) |
| See the capability surface | [`features.md`](features.md) |
| Install or use a release archive | [`user-guide.md`](user-guide.md), [`packaging.md`](packaging.md) |
| See the current and unreleased change record | [`CHANGELOG.md`](../CHANGELOG.md) |
| Report a vulnerability | [`SECURITY.md`](../SECURITY.md) |

## Current subsystem references

| Area | Canonical current reference | Scope |
| --- | --- | --- |
| Chat and context | [`features.md`](features.md), [`user-guide.md`](user-guide.md) | Chat behavior, context inspection, attachments, Knowledge, branches, and recovery. There is no separate Chat manual. |
| Models and managed services | [`llama-cpp-features.md`](llama-cpp-features.md), [`user-guide.md`](user-guide.md) | Runtime flags and capability evidence; model, companion, GPU Fit, and Services workflows. |
| RAG and Knowledge | [`rag.md`](rag.md) | Ingest, watched sources, retrieval, Chat injection, reranking, budgets, and evaluation. |
| Recall | [`recall.md`](recall.md) | The federated local search index and its privacy controls. |
| Memories | [`features.md`](features.md), [`user-guide.md`](user-guide.md) | Memory behavior is documented with the current Chat and Memory workflows; no separate Memory manual exists. |
| Agent Workbench | [`agent.md`](agent.md) | Tasks, context, tools, plans, approvals, workspace policy, lessons, orchestration, traces, and rewind. |
| Agent Local API | [`agent-api.md`](agent-api.md) | Versioned DTO and authorization contract. Execution routes are deliberately not mapped. |
| Local API | [`local-api.md`](local-api.md) | Current loopback host, authentication, routes, capability reporting, and privacy boundaries. |
| Projects and Project State | [`projects.md`](projects.md) | Shared defaults, revisioned state, proposals, and context boundaries. |
| Voice and speech input | [`voice.md`](voice.md) | TTS, STT, providers, setup, playback, privacy, and wired versus unwired flows. |
| Benchmarks and Speed Check | [`benchmarks.md`](benchmarks.md) | Suites, run metadata, ranking, resource evidence, retention, and speed comparisons. |
| Lab and empirical Evidence | [`lab.md`](lab.md) | Isolated experiments, correctness gates, evidence records, comparisons, and Apply review. |

## Architecture, security, and design contracts

| Topic | Document |
| --- | --- |
| Repository architecture and working contract | [`AGENTS.md`](../AGENTS.md) |
| Current security controls and threat model | [`security-review.md`](security-review.md) |
| Open security hardening | [`security-roadmap.md`](security-roadmap.md) |
| Test shape, execution, coverage, and guard rules | [`testing.md`](testing.md) |
| Linux and Windows packaging | [`packaging.md`](packaging.md) |
| Avalonia upgrade policy | [`avalonia-upgrade.md`](avalonia-upgrade.md) |
| Product brand and visual language | [`hermaeus-branding.md`](hermaeus-branding.md) |
| Moss identity, UI voice, and mascot use | [`mascot.md`](mascot.md) |

Security history is intentionally separate from the current threat model. Read
[`security-history.md`](security-history.md) for the per-round record, not for
the current control list.

## Active development and research

These documents describe work in progress, open decisions, or proposed
extensions. They are not shipped-feature references:

- [R31 review index](review/README.md), its numbered contracts, and the
  [R31 evidence records](review/evidence/r31-batch-0.md) plus later batch files.
- [R31 deferred ledger](review/deferred.md).
- [RAG evaluation harness plan](rag-eval-harness.md), which proposes work beyond
  the native evaluation surface described in [`rag.md`](rag.md).
- The watchlists and dated upstream audits linked from
  [`llama-cpp-features.md`](llama-cpp-features.md).

The active R31 documents are preserved as engineering records. They may explain
why a current safety boundary exists, but they do not override the current
reference or authorize planned behavior.

## Historical and archive material

- [`review/archived/`](review/archived/) contains the immutable R1 through R30
  review packs. Each round's README explains its own document set.
- [`security-history.md`](security-history.md) contains the historical security
  narrative that used to compete with the current threat model.
- [`changelog-archive.md`](changelog-archive.md) contains older release entries
  removed from the ten-version root changelog window.

Old review links may intentionally describe paths or terminology from the
repository state in which they were written. Do not rewrite them as part of a
current-documentation update.

## Development, legal, and supporting documents

- [`pull-requests.md`](pull-requests.md) and [`../CONTRIBUTING.md`](../CONTRIBUTING.md)
  describe contribution and review workflow.
- [`../AGENTS.md`](../AGENTS.md) is the repository working contract. The tracked
  [`../CLAUDE.md`](../CLAUDE.md) mirrors the maintainer guidance for Claude
  integrations.
- [`../LICENSE.md`](../LICENSE.md), [`../COMMERCIAL.md`](../COMMERCIAL.md),
  [`../NOTICE.md`](../NOTICE.md), and [`../THIRD-PARTY-NOTICES.md`](../THIRD-PARTY-NOTICES.md)
  are legal and attribution documents.
- [`images/README.md`](images/README.md) documents public screenshots.
- [`../src/Tools/TraceValidator/README.md`](../src/Tools/TraceValidator/README.md)
  documents the standalone trace-validation tool.
