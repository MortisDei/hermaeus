# Hermaeus Security Roadmap

Hardening work identified during review but not yet implemented. An entry
here is a known, accepted gap, not an oversight: each names what, why it
matters, and a rough trigger for doing it. Harvested from
`docs/security-review.md` and `docs/security-history.md`; nothing here is a
new commitment beyond what those two already imply.

A round's own explicit rejections (`docs/review/<round>/06-roadmap.md`
"Explicit rejections") are a distinct, permanent decision, not a deferred
item - they are not moved here.

## Network and process

- **Optional blocking policy for network-affecting `llama-server` flags.**
  Trust scans already detect a `--host` override to a non-loopback address,
  but only warn. Trigger: before public release, decide whether detection
  should become a hard block.

## RAG

- **Stronger RAG file-size enforcement.** Oversized local files are
  currently warned, not refused. Trigger: a reported resource-exhaustion
  complaint, or before public release.
- **Broader text sanitization / domain allow-listing for web ingest.**
  Current web ingest strips script/style and caps pages; there is no domain
  allow-list. Trigger: if web ingest grows beyond explicit single-page
  fetches.

## Secrets and redaction

- **Broader redaction fixtures for provider-specific secret formats.**
  Current redaction covers the common shapes (OpenAI-style keys, bearer
  tokens, GitHub tokens, AWS/Azure key shapes). Trigger: a new provider
  integration whose credential format is not already covered.
- **Clearer UI state when the local fallback secret vault is active** (as
  opposed to an OS credential store). Trigger: a support report tied to
  vault-readability confusion, or before public release.
- **Migrate the local fallback vault's encryption from AES-CBC to an AEAD
  cipher (AES-GCM).** r24 closed the practical "wrong-key decrypt silently
  returns garbage instead of failing" gap with a UTF8 structural-validity
  check, but that is not a formal cryptographic integrity guarantee - a real
  authentication tag would be. Needs a versioned payload format (a new
  prefix alongside the existing `v2:`) so already-encrypted secrets stay
  readable without a forced re-encryption pass. Trigger: before public
  release, or if the vault ever needs to defend against a more active
  local threat than "the key file was corrupted or replaced."

## Backup and restore

- **Backup manifest recording app version and source platform.** Trigger:
  before public release, or the first cross-platform restore support
  request.
- **Restore preview / dry-run before extracting.** Trigger: before public
  release.

## Packaging

- **Signed packages/installers and trusted checksum publication.** Archives
  are currently unsigned; users must verify checksums from a trusted
  channel. Trigger: public release.

## Agent

- **Local API per-server tool-level scoping**, analogous to the MCP
  per-server allowed-tools list. Trigger: if the Local API surface grows
  beyond its current five endpoints.
- **Hash display/pinning for known local runtime binaries** (llama-server,
  Python, XTTS scripts) in Trust & Safety. Trigger: a reported
  binary-substitution concern, or before public release.
- **A workspace policy editor UI.** The `.hermaeus/workspace.json` `policy`
  block (r23) is hand-edited, like `AllowedCommands` already is. Trigger: if
  hand-editing JSON proves to be a real adoption barrier once policy sees
  daily use.
- **`run_command`'s optional path argument is not classified Blocked at
  approval time for a workspace-policy denial**, unlike
  `edit_file`/`create_file`/`apply_draft_patch` (r23 3.2). The path
  argument's safety was already deferred to execution time before this
  round (containment only, inside `WorkspaceCommandRecipes.
  WithOptionalPath`); policy enforcement follows that same existing timing
  rather than introducing a second, earlier check specifically for
  `run_command`. The net safety property holds - a policy-denied path can
  never actually execute - only the classification-time UX differs (an
  execution-time refusal rather than a pre-approval Blocked disposition).
  Trigger: if `run_command`'s argument shape grows more sophisticated, or a
  user reports the inconsistency as confusing.
