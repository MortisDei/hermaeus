---
name: storage-and-data-root
description: Rules for Aether's SQLite stores, schema migrations, data root layout, backup/restore, and atomic file writes. Use for any change that reads or writes persistent state.
---

# Storage and the data root

## Data root

- Linux `~/.local/share/Aether/`, Windows `%LOCALAPPDATA%\Aether\`;
  user-configurable with migration support (`BackupService`, data-root
  migration). Agent state lives under `agent/` inside it.
- Any new persistent artifact must live under the data root (or the
  configured local AI assets root for model files), be included in data-root
  migration, and be considered for backup inclusion/exclusion. Secrets
  (fallback vault + key file) are **excluded** from backup by design — keep it
  that way.

## SQLite rules

- `Microsoft.Data.Sqlite` directly; no ORM, no repository abstraction.
- Every store records a schema version; changes are **additive migrations**
  through `SqliteMigrationRunner` (`src/Aether.Services`). Never modify or
  reorder an existing migration; never write destructive migrations.
- Derived indexes (like `agent/task_index.db`) must be rebuildable from their
  source of truth (`task_state.json` files). If you add an index store, add
  the reconciliation path too.
- Long-running writes (RAG ingest) are batched and cancellable; follow the
  batch-flush pattern in the ingest pipeline to avoid DB lock contention.

## File-write rules

- Settings, small state files, generated scripts, exports: atomic replacement
  (write temp, then move) — see `SettingsService` for the pattern. An
  unreadable `settings.json` is copied aside, not overwritten.
- Anything extracted from archives (restore) must reject path traversal and
  prefix-escape entries; anything resolving user paths must reject symlinks
  escaping the intended root. Reuse the existing guards; do not reimplement.
- Log text is redacted (`RedactionService`) **before** persistence, not at
  display time.

## Checklist for a storage change

1. Migration added via the runner, version bumped, additive only.
2. Included in data-root migration and backup logic (or deliberately
   excluded, with a comment saying why).
3. Retention/growth considered — unbounded append-only stores need a cap or
   cleanup story.
4. Data-safety tests updated (`BackupMigrationTests` and friends).
