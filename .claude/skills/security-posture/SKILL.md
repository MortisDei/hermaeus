---
name: security-posture
description: Hermaeus's security invariants for process launching, secrets, downloads, path handling, network binding, and agent safety gates. Use for any change touching processes, files from user input, network, secrets, or the Agent.
---

# Security posture (invariants, not suggestions)

Hermaeus's brand is local-first trustworthiness. These rules are enforced by
review and by the Trust & Safety / security docs (`docs/security-review.md`).

## Processes

- Launch via `ProcessStartInfo.ArgumentList` — never shell strings or
  concatenated command lines. See `ServerProcessManager`.
- Managed servers bind to `127.0.0.1` only. Flag anything that would expose
  `0.0.0.0` (including `--host=` style equals flags) through the trust checks.
- Generated scripts (e.g. voice helpers) must escape interpolated paths;
  follow the existing XTTS script generator pattern.

## Secrets

- Never persist raw API keys in settings, logs, exports, or traces. Store via
  `ISecretStore` (OS credential store, encrypted local fallback) and keep only
  secret *references* in settings.
- The fallback vault and its key file are excluded from backups. Preserve
  that exclusion in any backup/restore change.
- All text destined for logs/exports flows through `RedactionService` first.
  If you add a new token/credential shape to the app, add a redaction pattern
  and a test for it.

## Downloads

- Model/binary downloads must be pinned (exact URL or Hugging Face commit)
  and SHA256-verified before use; failed downloads are deleted. Follow the
  Doctor embedding-model install pattern. No auto-downloads at query time —
  installs are explicit, user-approved actions.

## Paths

- Any path derived from user input or workspace selection: normalise, reject
  symlinks and `..`/absolute escapes outside the intended root. Case
  sensitivity matches the platform (insensitive on Windows). Reuse the
  Agent's workspace-boundary helpers.

## Agent safety gates

- The Agent is read-first: read-only tools execute; writes are approval-gated
  through the patch queue with `baseHash` stale-file protection; shell,
  network, install, commit/push are **blocked** — even if the model asks.
- Risk classification is deterministic and recorded in `agent.trace.jsonl`.
  Never add a tool that bypasses classification or the review queue; new
  capabilities extend the risk table in `docs/agent.md` first.

## When you change anything above

Update `docs/security-review.md`, add/extend a data-safety or redaction test,
and mention the security-relevant delta explicitly in your final response.
