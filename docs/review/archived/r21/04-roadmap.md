# 04. Roadmap and sequencing

## Version

Ships as **0.27.0-alpha** (`Directory.Build.props` only: VersionPrefix,
AssemblyVersion, FileVersion). Minor bump: a new user-visible capability
(chat knowledge attachment) plus an additive conversation-store schema
migration.

## Sequencing (strict)

1. **2.1 + 2.3** first (pipeline-side, self-contained, testable without any
   UI): embed-failure fallback, `GetDatasetAsync` seam. Full suite green.
2. **1.1** (Conversation field + store migration + round-trip tests).
3. **1.4** (public context-pack seam) then **1.3 + 1.5 + 1.6** (injection,
   trace, inspector, setting) as one piece; **2.2's** matrix tests land
   here with it.
4. **1.2** (picker UI) and **3.2** (missing-dataset honesty) together;
   they share the resolve-the-id code.
5. **3.3** (Open in chat), **3.4** (Privacy Audit), then **3.5** docs,
   CHANGELOG, security-review.
6. Close-out: archive this pack to `docs/review/archived/r21/`, commit
   (owner's standing pattern: implement in full, commit after build/tests/
   docs are truthful). No AI co-author trailer on the commit.

## Test estimate

Roughly 25-35 new tests from the current 1095+:

- Store: rag_dataset_id round-trip, legacy-row read, migration guard (2-3).
- Pipeline: embed-failure fallback matrix from doc 02.1 (5), GetDatasetAsync
  (1-2).
- Chat injection: block built and bounded by budget, pill/[n] ordering,
  weak-retrieval skip, mixed memory+RAG turn, best-effort matrix from doc
  02.2 (8-10).
- Trace/inspector: RagContextItems/RagMs populated, RagNote paths, inspector
  part present (3-4).
- Picker/lifecycle: missing-dataset display state, id survives restart, None
  detaches, Open-in-chat creates attached conversation (4-5).
- Privacy Audit entry appears/disappears with provider selection (1-2).

All without a live llama-server, embedding server, or network: use the
existing fake embed/LLM seams and per-test temp data roots (tests stay
sequential; do not re-enable parallelization). Register any new
harness-style methods in `XunitHarnessTests.HarnessCases`; the
`HarnessRegistrationGuardTests` reflection guard fails otherwise.

## Practical warnings for the implementer

- Re-verify every file:line in this pack before editing; the tree may have
  moved since spec time (67e9b01).
- `ChatViewModel` and `RagViewModel` are designated hot spots (AGENTS.md):
  minimal, focused edits; mirror the existing memory-injection shape rather
  than introducing a new abstraction layer.
- No em dashes in any code, docs, or UI copy this round adds; the
  architecture test scans all .cs/.axaml.
- The naming guard (`NamingConsistencyTests`) scans for stray "Aether";
  copy-pasting old comment blocks can trip it.
- UI copy says "Knowledge"; code and settings keys say Rag*. Do not leak
  "RAG" into the chat header button label.
- Do not write `settings.json` directly; the new budget setting rides
  `RagSettings` through `SettingsService` like every other field.

## Explicit rejections (do not do these)

- **No multi-dataset attachment.** One dataset per conversation. Merging
  ranked lists across corpora with different embedding models is a quality
  and honesty swamp; revisit only with a field report demanding it.
- **No auto-attachment or dataset auto-suggestion.** The user picks;
  nothing infers "this conversation looks like it is about dataset X".
- **No RAG refusal semantics in chat.** Weak retrieval skips injection and
  notes it in the trace; the model answers from its own knowledge as chat
  always has. The RAG panel remains the place for grounded-answer-or-refuse
  behaviour.
- **No retrieval timeout/racing design.** Retrieval is awaited pre-stream;
  its cost is visible as RagMs in the trace. If field use shows it hurting,
  a future round addresses it with numbers in hand.
- **No event bus between RagViewModel and ChatViewModel** for dataset-list
  changes; refresh-on-open is the contract (doc 03.1).
- **No conversation-scan warnings on dataset delete** (doc 03.2).
- **No per-conversation TopK/threshold/budget overrides.** One global
  budget setting; pipeline defaults for the rest.
- **The r10 rejections all stand**: no vector DB, no ANN index, no LLM
  query rewriting, no auto-reindex, no auto-removal of missing sources.
- **No LocalApi changes.** `/v1/rag/query` already exists; chat-attachment
  semantics stay desktop-only this round.
