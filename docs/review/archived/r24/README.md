# Review round 24: One place, one memory, one voice

Audience: the implementing agent. Read this file, then the numbered docs in
order. Doc 06 is the roadmap and sequencing contract.

## Why this round exists

Twenty-three rounds have made Hermaeus capable. This round makes it
coherent. Three forces shape it:

- **The owner's own report is that the app confuses him**, and he named the
  shape precisely: "what can it even do" and "did that actually work". Not
  navigation, not missing features. Capability that is present but not
  legible, and outcomes that happen somewhere he is not looking. An app with
  twelve panels and no way to ask it what it is capable of has an
  affordance problem, not a feature problem. Docs 02 and 04 answer this
  directly.
- **Four subsystems each hoard their own knowledge and none of them
  speak.** Conversations have conversation-level FTS. Memories have proper
  hybrid FTS-plus-cosine recall. Agent tasks have transcripts and reports
  nothing can search. RAG datasets have the best retrieval in the app and
  only ever answer questions about themselves. The user has one memory of
  working here; the app has four, and offers no way to ask across them.
  Doc 02 is the round's centerpiece.
- **The workstation is still assembled by hand, every time, and half of it
  rots.** Workspace root lives in the Agent, the dataset on the
  conversation, the model in the chat header, the prompt in Settings, and a
  dataset silently goes stale the moment a source file is edited. Docs 01
  and 03 give the app a first-class notion of what you are working on and
  keep its knowledge current.

And one gap that is simply missing: **Hermaeus speaks but cannot listen.**
Five TTS providers, a phonemizer, a pronunciation lexicon, chunked
playback, and no microphone path at all. Doc 05 closes the loop.

## Documents

| Doc | Theme |
| --- | --- |
| `01-projects.md` | A Project: one switchable context binding an optional folder root, a dataset, a model, a prompt, its conversations and its agent tasks |
| `02-recall-and-the-palette.md` | The flagship: one query across every conversation, memory, agent task and dataset, and a Ctrl+K palette that is both that search and a browsable index of everything the app can do |
| `03-living-knowledge.md` | Watched sources: datasets that notice their source files changed instead of rotting silently |
| `04-activity-and-legibility.md` | The Activity feed ("did that actually work"), one shared command registry behind both the palette and per-panel capability discovery, and settings-field search |
| `05-voice-input.md` | Local speech in: a managed whisper.cpp backend, microphone capture on both platforms, dictation anywhere, and an optional hands-free conversation mode |
| `06-roadmap.md` | Ships as 0.31.0-alpha; sequencing, test budget, descope order, housekeeping, explicit rejections |

## Standing rules for the implementing agent

- Verify before implementing. Every file:line reference in this pack was
  exact against tree `c398e43` (v0.30.0-alpha, 1185 tests green, zero
  warnings); re-verify before editing. `AgentService.cs`, `ChatViewModel.cs`
  and `AgentViewModel.cs` move often.
- No em dashes anywhere. Zero-warning build. All tests pass. Register any
  new harness-style test methods in `XunitHarnessTests.HarnessCases`; the
  `HarnessRegistrationGuardTests` reflection guard fails otherwise.
- **No new NuGet packages.** Everything in this pack is reachable with what
  is already referenced. In particular: microphone capture on Windows is
  `winmm` P/Invoke, not an audio package (doc 05 5.2), and rank fusion
  reuses the RRF already in `HybridRetriever`, not a new library.
- Every schema change is additive and goes through `SqliteMigrationRunner`.
  A Hermaeus install that never creates a project, never opens the palette
  and never enables voice input must behave exactly as 0.30.0 does today.
  This is the single most important correctness property of the round.
- Nothing in this round may put work on the chat send path. Recall indexing
  is a background pass, following the `MemoryStore` embedding-backfill
  precedent (`MemoryStore.cs:515-545`), never an inline cost per send.
- **Any new store that holds the user's own words ships with a visible
  switch, a real delete, and an honest size.** This round adds two stores
  that persist things about what the user did (`recall.db` in doc 02,
  system activity rows in doc 04). Neither may be invisible, neither may be
  permanent-by-default, and neither may be inferred from a feature toggle
  somewhere else. Doc 02 2.0 states the full requirement; doc 04 4.2
  carries the smaller version. This is the round's data-sovereignty
  boundary and it is not tradeable for schedule.
- The safety gate stays deterministic. A project, a recall hit, a watched
  source, and a transcript are all untrusted content; none of them may
  widen what the agent executes without approval. Doc 01 1.6 and doc 02 2.6
  restate this where it bites.
- Update `docs/features.md`, `docs/agent.md`, `docs/rag.md`, `docs/voice.md`
  and `CHANGELOG.md`, plus the new `docs/projects.md` and `docs/recall.md`.
  Do not document planned behaviour as existing behaviour. Doc 06's
  housekeeping section lists the specific stale passages this round must
  reconcile.
- Moss-attributed copy follows `docs/mascot.md` "Voice in UI copy". Icon-only
  controls need tooltips; the guard test scans axaml and fails without one.
  The palette, the Activity feed and the mic button are all new icon-heavy
  surfaces, so expect that guard to bite.
- This round lands via pull request per `docs/pull-requests.md`: branch
  `r24/round` from `main`, commit there, open the PR with the template,
  merge after CI is green on both matrix legs. One open PR at a time. No AI
  co-author trailer on commits.
