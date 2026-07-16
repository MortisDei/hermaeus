# 03 - Store integrity

The SQLite stores and the settings/data-root machinery around them.

## 3.1 Data-root migration silently strands secrets, traces, evals, logs, and the voice lexicon

`SettingsService.EnumerateMigrationFiles` (SettingsService.cs:305-315)
moves only `conversations.db*`, `memories.db*`, `benchmarks.db*`, and
`agent/`. Everything else the app writes to the data root stays behind:
`secrets.local.json` + `secrets.local.key` (SecretStore.cs:98-102,
160-164), `traces.db` with the durable model_usage rollup
(SqliteTraceStore.cs:20-28), `eval_runs.db` (SqliteEvalStore.cs:23-31),
`logs/` (RuntimeLogService.cs:44-52), `voice/lexicon.txt` (r8),
`agent-scenarios/`, and `eval-runs/`. After a data-root move, every
store resolves paths against the new root, so fallback-stored secrets
vanish (providers silently lose credentials, per SecretStore's own
warning path), usage history resets, and the user lexicon is lost -
while the old root keeps a live copy of the most sensitive file in the
product. `PreviewDataRootMigration` undercounts for the same reason.

Fix direction: a single authoritative manifest of data-root contents
(one static list or enumerator) shared by migration, preview, and
`BackupService` (which has its own slightly different notion today), so
the three can never disagree again. Migration moves everything in the
manifest; secrets files move with restrictive permissions preserved
(reuse the temp-then-move pattern).

Acceptance criteria:

- Migration test: a fixture root containing every manifest entry moves
  completely; the old root retains nothing but the `.aether-backups`
  copy; preview counts match what migration moves.
- A new file family added to the manifest is picked up by migration,
  preview, and backup without further code (test constructs the union
  from the manifest).
- Secrets resolve correctly after migration (store + move + resolve
  round trip against a temp data root).

## 3.2 Memory saves embed inline with no timeout on the response path

`MemoryStore.SaveAsync` (MemoryStore.cs:238-243) awaits
`_embeddings.EmbedAsync` with no timeout before writing the row. The
query path learned this lesson in r9/r10 (`QueryEmbedTimeout` = 3 s,
line 16); the save path did not. `ConversationMemoryService.
ApplyInjectedMemoryMarkersAsync` and `MergeAndSaveAsync`
(ConversationMemoryService.cs:42-81, 292-323) call SaveAsync on the
post-response path, so a hung embedding endpoint stalls each memory
write for the full HTTP timeout (up to 60 s per row) exactly where r9
worked to remove stalls. The backfill path already handles rows saved
without embeddings, so failing fast costs nothing.

Acceptance criteria:

- Save-path embed bounded by the same 3 s class timeout; on timeout the
  row is saved with a null blob and backfill picks it up (existing
  COALESCE semantics; test with a never-completing fake embedder
  asserts SaveAsync returns promptly and the row exists).

## 3.3 Memory FTS candidates are ranked by importance, not by match quality

`SearchAsync` (MemoryStore.cs:335-341) orders FTS hits by
`is_pinned DESC, importance_score DESC, updated_at DESC` and then
`HybridRerankAsync` converts that order into the "FTS rank" half of the
hybrid score (`ftsRank[id] = 1/(i+1)`, lines 394-396). BM25 relevance
(`ORDER BY rank` in FTS5) is never consulted, so the lexical half of
hybrid recall measures importance, not how well the text matched. The
same pattern exists in ConversationStore.SearchAsync (fine there - it
is a browse list), but MemoryStore's ordering feeds scoring.

Acceptance criteria:

- FTS query orders by FTS5 `rank`; pinned/importance influence remains
  where it belongs (MemoryInjectionService's EffectiveScore and the
  final pinned-first ordering).
- Test: two rows where the lexically-better match has lower importance;
  it must receive the higher fts-rank component.

## 3.4 Stored UTC timestamps come back as Local-kind

`MemoryStore.Map`/`GetDateTimeNullable` (MemoryStore.cs:780-781,
850-856) and `ConversationStore.Map` (ConversationStore.cs:299-300) use
`DateTime.Parse` on round-trip ("O") strings without
`DateTimeStyles.RoundtripKind`, which converts the stored UTC instant
to a Local-kind DateTime. Downstream arithmetic against
`DateTime.UtcNow` (ArchiveStaleMemoriesAsync's staleness window,
MemoryLifecycle decay, auto-summary's 6-hour dedupe cutoff in
ConversationMemoryService.HasRecentAutoSummaryAsync) is then off by the
machine's UTC offset. SqliteTraceStore already corrects with
`.ToUniversalTime()` (SqliteTraceStore.cs:217).

Acceptance criteria:

- All store date parsing uses invariant culture +
  `DateTimeStyles.RoundtripKind` (helper shared per store); a
  round-trip test asserts `Kind == Utc` and the same instant in a
  non-UTC test culture/timezone setup.

## 3.5 Archiving stale memories re-embeds every row and resets its clock

`ArchiveStaleMemoriesAsync` (MemoryStore.cs:570-590) flips
`IsArchived` and calls the full `SaveAsync`, which re-embeds the
unchanged content (one embedding HTTP call per archived row) and bumps
`UpdatedAt`. Add a narrow archive update (single UPDATE of is_archived
+ updated_at policy decision) or a SaveAsync option that skips
embedding when content is unchanged.

Acceptance criteria:

- Archiving N rows performs zero embed calls (counting fake embedder);
  FTS row remains consistent with the archived state.

## 3.6 Backup zips live SQLite databases

`BackupService.BackupAsync` (BackupService.cs:23-38) streams `*.db`
(and `-wal`/`-shm` siblings when present) straight into a zip while the
app can be mid-write; a backup taken during a checkpoint or transaction
can be internally inconsistent. SQLite provides a safe primitive:
`VACUUM INTO` (or the online backup API via
`SqliteConnection.BackupDatabase`).

Fix direction: for each `.db` in the manifest (3.1), produce a
consistent snapshot via `BackupDatabase` into a temp file and zip that;
non-database files copy as today.

Acceptance criteria:

- Backup of a database with an open writer connection yields a zip
  whose extracted db passes `PRAGMA integrity_check` (test opens a
  write transaction during backup).

## 3.7 BenchmarkService initialization has no gate

`EnsureInitializedAsync` (BenchmarkService.cs:472-510) lacks the
`SemaphoreSlim` init gate every other store uses; concurrent first
calls race the CREATE TABLE + starter-suite seeding (seeding reads
`existing` then inserts, so a race can double-insert or throw on the
PK). Low likelihood, cheap fix, consistency with the codebase pattern.

Acceptance criteria:

- Same gate pattern as SqliteEvalStore; a parallel-first-call test
  completes with exactly one set of starter suites.
