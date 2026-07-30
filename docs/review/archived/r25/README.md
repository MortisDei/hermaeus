# Review round 25: Change your mind, and trust what it tells you

Audience: the implementing agent. Read this file, then the numbered docs in
order. Doc 06 is the roadmap and sequencing contract.

## Why this round exists

r24 made Hermaeus coherent: one place to work, one memory to search, one
voice to speak with. It also, in daily use, exposed a different class of
problem. Every item in this round comes from the same observation, made
three separate ways:

**The app tells you things, and some of what it tells you is not true.**

- The owner collapsed the memory pill on a chat message and the memories it
  claimed to be hiding were listed directly above it anyway. The collapse
  control does nothing visible, because r24's Recall pills render in a
  different strip that nobody taught to collapse.
- The Benchmarks panel names a "Best overall" model. It computes it by
  averaging each model over whatever cases that model happened to run, with
  no requirement that two models sat the same exam. A model that ran one
  easy suite can outrank a model that ran everything.
- Speech recognition returns `HELLO CAN YOU CHECK WHETHER THE BUILD PASSED`.
  Not because transcription failed, but because the model that shipped has
  a 32 symbol uppercase vocabulary with no punctuation in it at all.
- The README describes version 0.30.0. r24 shipped Projects, Recall, the
  command palette, watched sources, Activity and speech input, and the
  front page of the repository mentions none of them.

And one thing the app cannot do at all, which r24 named as this round's
leading candidate: **you cannot change your mind.** Regenerate deletes the
answer you had. There is no way to edit a question and keep the original
thread. `RegenerateAsync` removes both the assistant message and the user
message from the conversation and puts the text back in the input box. That
is data loss on a button that reads as "try again".

So: doc 01 lets you change your mind without losing anything. Docs 02
through 05 make the app's own reports match reality.

## Documents

| Doc | Theme |
| --- | --- |
| `01-conversation-branching.md` | The conversation is a tree. Regenerate and edit create branches instead of destroying history, with a `‹ 2/3 ›` switcher on any message that has siblings |
| `02-one-context-receipt.md` | One honest account of what went into an answer, replacing three inconsistent pill strips, and fixing the collapse that does not collapse |
| `03-speech-that-punctuates.md` | Keep in-process ONNX, replace the model with Whisper: punctuation, casing, real language detection, and long audio that cannot exhaust memory |
| `04-benchmarks-you-can-trust.md` | Rank "Best overall" on cases every candidate actually ran, name the axis that won, and say "not enough shared results" when that is the truth |
| `05-docs-that-match-the-app.md` | A guard test for documentation drift, a standing ledger of deferred items, and the audit of everything twenty-four rounds deferred |
| `06-roadmap.md` | Ships as 0.32.0-alpha; sequencing, test budget, descope order, housekeeping, explicit rejections |

## Standing rules for the implementing agent

- Verify before implementing. Every file:line reference in this pack was
  exact against tree `1bd3f2d` (v0.31.0-alpha, 1316 tests green, zero
  warnings); re-verify before editing. `ChatViewModel.cs`, `AgentViewModel.cs`
  and `ServicesViewModel.cs` move often and are named hot spots in CLAUDE.md.
- No em dashes anywhere. Zero warning build. All tests pass. Register any
  new harness-style test methods in `XunitHarnessTests.HarnessCases`; the
  `HarnessRegistrationGuardTests` reflection guard fails otherwise.
- **No new NuGet packages.** Doc 03 is this round's temptation and the
  answer is still no: the FFT is roughly sixty lines of radix-2 written
  in-tree, and Whisper's detokenizer is a dictionary lookup, not a
  tokenizer library. If a package feels necessary, re-read 3.2 and 3.3.
- **Nothing in this round may lose a user's words.** Doc 01 removes an
  existing data-loss path and must not introduce a new one; every branch
  operation is additive except one explicitly confirmed subtree delete.
  Doc 03 must not delete an already-downloaded model from disk, even a
  superseded one. This is the round's boundary and it is not tradeable for
  schedule.
- **The conversation backfill is the highest-risk change in the pack.**
  Every existing conversation has its parent chain inferred at load time.
  Test it against a seeded 0.31.0 `messages_json` blob, not only against
  data this round wrote. Same discipline as r24's four migrations.
- Schema changes are additive and go through `SqliteMigrationRunner`. An
  install that never branches a conversation and never reinstalls a speech
  model must behave exactly as 0.31.0 does today.
- Nothing goes on the chat send path. Doc 02 builds its receipt from source
  references that already exist in memory; it does no retrieval of its own.
- Update `README.md`, `docs/features.md`, `docs/voice.md`,
  `docs/benchmarks.md` and `CHANGELOG.md`, plus the new
  `docs/review/deferred.md`. Do not document planned behaviour as existing
  behaviour. Doc 05 exists because this rule has been followed for the
  workflow docs and quietly skipped for the README.
- Moss-attributed copy follows `docs/mascot.md` "Voice in UI copy".
  Icon-only controls need tooltips; the guard test scans axaml and fails
  without one. The branch switcher arrows and the receipt expander are new
  icon-adjacent chrome on every single message.
- This round lands via pull request per `docs/pull-requests.md`: branch
  `r25/round` from `main`, commit there, open the PR with the template,
  merge after CI is green on both matrix legs. One open PR at a time. No AI
  co-author trailer on commits.
