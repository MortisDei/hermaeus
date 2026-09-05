---
name: storage-and-data-root
description: The current Hermaeus rules for SQLite, persistent files, backup/restore, and staged Data Root migration.
---

# Storage and Data Root

This skill supplements `AGENTS.md`. Describe the implementation that exists,
not an idealized migration system. Persistent artifacts belong under the
effective Data Root unless they are deliberately under the configured local AI
assets root or explicitly excluded with a documented reason.

## Data Root state vocabulary

Keep these states separate:

- **Requested**: the editable destination in
  `DataManagementSettingsViewModel.DataRootDirectory`.
- **Configured**: the committed `DataManagement.DataRootDirectory` in
  `settings.json`.
- **Pending**: `PendingDataRootDirectory` and its serialized
  `PendingDataRootMigrationPlan`, written after explicit migration approval.
- **Effective**: the root resolved and composed by the current process. Existing
  stores and cached views remain bound to it until restart.
- **Observed**: the current filesystem inventory, verification counts,
  exclusions, retained files, failures, and receipt produced by the migration.

## Actual safe migration lifecycle

1. The user selects a destination and reviews the preview inventory.
2. The explicit confirmation stages a pending migration. It does not move
   active SQLite, WAL, log, or live store files underneath running services.
3. The user chooses Restart now or Restart later. Restart later leaves the
   old configured/effective root active and retains the pending destination.
4. At bootstrap, before data-backed services are composed, the migration
   resolves and validates the pending destination and ensures it is writable.
5. `DataRootManifest` inventories the current source tree again instead of
   trusting only the earlier preview. It excludes the root settings bootstrap
   file and active process lock. Partial destination conflicts refuse the move.
6. The migration creates a destination backup, copies and SHA256-verifies each
   source file, moves files, and verifies moved files against the backup.
7. On failure, moved files and created directories are rolled back where
   possible. The old valid effective/configured state remains authoritative,
   the pending destination remains retryable, and the failure receipt is kept.
8. On success, the new root becomes configured at bootstrap, pending fields are
   cleared, the receipt records the observed evidence, and empty old-root
   cleanup is best effort after the new root is complete.

Migration evidence records initially discovered, excluded, discovered at
restart, copied/moved, verified, removed, retained, skipped, and failed
counts, plus bounded exclusion, retained-path, and failure details. A
destination that already contains every migratable file is treated as a safe
repoint with skipped evidence; a partial conflict remains refused.

## SQLite, backup, and file rules

- Use `Microsoft.Data.Sqlite` directly. Schema changes go through additive
  `SqliteMigrationRunner` migrations. Never edit or reorder an old migration
  and never introduce a destructive migration.
- `DataRootManifest` is the shared enumeration source for migration and
  backup. Backup excludes fallback secret files, rebuildable artwork, and raw
  SQLite sidecars; live databases are copied through SQLite backup so the
  archive represents a consistent snapshot. Restore rejects traversal, prefix
  escapes, and existing files unless its explicit overwrite path is in scope.
  The current restore path does not reject every pre-existing symlink ancestor;
  that remains a documented security limitation.
- Fallback secrets stay outside backup scope. Derived indexes such as
  `agent/task_index.db` remain rebuildable from `task_state.json`.
- Long-running RAG and indexing writes stay batched and cancellable. Settings,
  state files, receipts, scripts, and exports use atomic replacement. Redact
  log text before it reaches disk.
- Consider retention and growth for every new store or append-only record.
  Add data-safety tests for migration, backup inclusion/exclusion, rollback,
  cancellation, and reopening where applicable.
