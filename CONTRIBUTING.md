# Contributing

Hermaeus is a source-available product project maintained by one person, not an
open-governance open-source project. External code contributions are considered
only by prior arrangement. Opening a pull request does not grant review, merge,
write, release, secret, or workflow authority.

**Issues and bug reports are welcome and useful.** So are feature suggestions,
though the answer to most of them will be no or not yet, and the "Explicit
non-goals" section below explains several of those in advance.

If you want to build on Hermaeus for yourself, the licence already allows that
for private and noncommercial use. Fork it and enjoy.

The rest of this file documents how the project is built and the conventions it
holds itself to. It is written for the maintainer and for the AI agents working
in this repository, and it is public because the reasoning is worth reading even
if you are only here to look.

## Contribution Terms

These apply to any contribution accepted by prior arrangement with the project
owner. By submitting a contribution, you certify that:

- you have the right to submit the contribution
- the contribution is your own work or is submitted with permission
- the contribution does not knowingly include code, assets, models, prompts, or
  data that conflict with Hermaeus's licensing model
- you grant the project owner the right to use, modify, distribute, sublicense,
  and commercially license the contribution as part of Hermaeus

This dual-use contribution grant is required because Hermaeus is free for private
and noncommercial use but may also be commercially licensed.

## Development Expectations

- Keep Hermaeus native, local-first, and desktop-focused.
- Keep public docs focused on Hermaeus's own product identity.
- Do not add hosted-service dependencies by default.
- Prefer optional integrations over hard dependencies.
- Run `dotnet build Hermaeus.sln` before submitting changes.
- Run `dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj` when changes
  affect storage, settings, security, RAG, backup, restore, or runtime launch.
- Update the relevant workflow docs, `docs/features.md`, and `CHANGELOG.md`
  whenever behaviour, configuration, APIs, workflows, or UI semantics change.
  Stale documentation is a defect, and planned behaviour must not be described
  as implemented. If no documentation update is required, say so explicitly in
  the pull request or review instead of silently omitting it.
- Changes land as pull requests, never direct pushes to `main`; see
  `docs/pull-requests.md`. Only the repository owner may approve, merge, push
  protected branches, publish releases, or change workflow permissions.

## Vocabulary

A few words are intentionally reused for different, unrelated concepts across
the codebase. Knowing the distinction up front saves a grep-and-guess later:

- **Memory** means one thing at the storage layer (`IMemoryStore`, with
  `MemoryScope.Global/Workspace/Conversation`) but reads like three things in
  conversation: "chat memory," "workspace memory," and "workspace profile
  facts" are all the same store at different scopes, not different systems.
- **Workspace** means both "the agent's active root directory" and "the app
  generally" (as in "AI workspace" the product). Context disambiguates; when
  writing docs, prefer "workspace root" for the former.
- **Profile** is reused across four unrelated schemas: `ModelProfile`
  (per-model chat defaults), `RuntimeProfile` (a configured llama.cpp/Ollama/
  OpenAI-compatible endpoint), `WorkspaceProfile` (app-side per-root analysis
  results), and tune profiles (per-GGUF sampling parameters). These are
  deliberately separate types, not a naming bug; each is scoped to its own
  domain and none of them read or write another's storage.

Renaming any of these is deliberately not planned: they're stable public
types by this point, and a cosmetic rename carries more churn/regression risk
than the naming friction it would relieve. New code should still avoid
introducing a fifth meaning for any of these words.

## Explicit non-goals

The following are deliberately not part of Hermaeus's design, decided during
the July 2026 architecture review (r1). The reasoning behind each is in
`docs/review/archived/r1/`: `02-dependency-review.md`,
`03-architectural-opportunities.md` and `08-brutal-critique.md`. The
non-goals are: hosted services, user accounts, telemetry, an
in-process plugin API, provider failover (silently answering with a
different model than the user chose is a bug, not resilience, for a
local-first/privacy-focused app), vector databases, an ORM, and a web UI.
Proposals that reintroduce one of these should engage with the existing
rationale rather than re-litigating it from scratch.

## Security

Do not commit secrets, API keys, private model files, personal datasets, or
generated audio. Report security concerns privately to the project owner rather
than opening a public issue with exploit details.
