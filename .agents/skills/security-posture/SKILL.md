---
name: security-posture
description: The current Hermaeus security invariants for processes, secrets, downloads, paths, networking, and Agent tool safety.
---

# Security posture

Treat `docs/security-review.md` as current control truth,
`docs/security-history.md` as append-only rationale, and
`docs/security-roadmap.md` as accepted open hardening work. Apply this skill
for changes touching processes, user-controlled files, network, secrets,
downloads, persistent logs, or Agent capabilities. Never weaken a boundary to
make a workflow easier.

## Process and network controls

- Launch configured executables with `ProcessStartInfo.ArgumentList`; never
  build a shell command string. Preserve cancellation and clean child-process
  shutdown. Generated helper scripts must escape interpolated paths using the
  existing generator pattern.
- Managed llama.cpp servers receive loopback binding and health checks target
  loopback. Trust checks detect network-facing `--host`, `--host=`, listen, and
  related forms. User Extra Args can still override the later effective host,
  so the current posture warns rather than silently claiming a hard block.
  Do not broaden that warning without a reviewed policy change.

## Secrets, downloads, and paths

- Persist only `ISecretStore` references, never raw keys in settings, logs,
  exports, or traces. Redact through `RedactionService` before persistence.
  The fallback vault and key file remain excluded from backups.
- Model and binary downloads use an exact approved source or revision and
  SHA256 verification before extraction or execution. Failed downloads are
  removed. Installs are explicit actions, not query-time auto-downloads.
- Normalize every user or remote influenced path, reject absolute and
  traversal escapes, reject symlink/reparse escapes, and use platform-correct
  local path comparison. Archive extraction and workspace boundaries need the
  same checks.

## Agent safety

The Agent is read-first. Read-only tools are allowed directly. Local writes,
declared command recipes, sub-task delegation, and MCP calls are Review and
require approval. `run_command` is not blanket Blocked: dispatch resolves its
family through `AgentSafetyGate.EvaluateCommand`; an undeclared or forbidden
family is Blocked, while a declared workspace recipe is Review. An exact
command string already approved once in the same task may auto-execute, but
that does not approve another command in the family.

Installs, network access, uploads, downloads, system configuration changes,
commit, push, and history changes remain Blocked in the Agent tool surface.
Risk classification is deterministic, recorded in traces, and independent of
model-provided `requires_approval` or `risk_level` fields. Approval uses the
displayed pending-action fingerprint and refuses stale or changed actions.
Lessons and model output inform proposals but never widen the gate.

## Change discipline

For a new capability, inspect `docs/agent.md` and extend its risk table before
adding a tool. Add regression coverage for classification, containment,
redaction, integrity, cancellation, and approval semantics as applicable.
Update the current security review and the round's security history only when
the change creates a security-relevant delta. Do not close roadmap items by
rewriting them as implemented. Keep blocked capabilities and the Local API
ownership boundary truthful.
