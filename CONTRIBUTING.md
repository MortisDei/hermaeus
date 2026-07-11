# Contributing

Aether is currently a source-available product project, not an open-governance
open-source project. Contributions are welcome only when they align with the
project direction and licensing model.

## Contribution Terms

By submitting a contribution, you certify that:

- you have the right to submit the contribution
- the contribution is your own work or is submitted with permission
- the contribution does not knowingly include code, assets, models, prompts, or
  data that conflict with Aether's licensing model
- you grant the project owner the right to use, modify, distribute, sublicense,
  and commercially license the contribution as part of Aether

This dual-use contribution grant is required because Aether is free for private
and noncommercial use but may also be commercially licensed.

## Development Expectations

- Keep Aether native, local-first, and desktop-focused.
- Keep public docs focused on Aether's own product identity.
- Do not add hosted-service dependencies by default.
- Prefer optional integrations over hard dependencies.
- Run `dotnet build Aether.sln` before submitting changes.
- Run `dotnet run --project src/Aether.Tests/Aether.Tests.csproj` when changes
  affect storage, settings, security, RAG, backup, restore, or runtime launch.

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

The following are deliberately not part of Aether's design, decided during
the 2026-07 architecture review (see `docs/review/02`, `03`, `08` for the
reasoning behind each): hosted services, user accounts, telemetry, an
in-process plugin API, provider failover (silently answering with a
different model than the user chose is a bug, not resilience, for a
local-first/privacy-focused app), vector databases, an ORM, and a web UI.
Proposals that reintroduce one of these should engage with the existing
rationale rather than re-litigating it from scratch.

## Security

Do not commit secrets, API keys, private model files, personal datasets, or
generated audio. Report security concerns privately to the project owner rather
than opening a public issue with exploit details.
