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
- Run `dotnet run --project tests/Aether.Tests/Aether.Tests.csproj` when changes
  affect storage, settings, security, RAG, backup, restore, or runtime launch.

## Security

Do not commit secrets, API keys, private model files, personal datasets, or
generated audio. Report security concerns privately to the project owner rather
than opening a public issue with exploit details.
