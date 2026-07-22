# 02. Runtime identity: paths, stores, protocol literals

Everything here changes behaviour, not just text. Each item names the
compat story explicitly: the owner is the only user, automated migration is
waved off, but a handful of one-line shims are cheap enough to be in scope.
Anything not listed as a shim is a clean break, documented in doc 04's
owner checklist.

## 2.1 Data root default folder

`SettingsService.cs:14` builds the default data root as
`{LocalApplicationData}/Aether`. The same fallback is duplicated in many
stores and services (grep `GetFolderPath` + `"Aether"`): `SqliteRagStore.cs:68`,
`AppLifecycleJournalService.cs:48`, `WorkspaceMemoryStore.cs:36`,
`SqliteLessonStore.cs:37`, `FileWorkspaceProfileStore.cs:51`,
`FileAgentTaskStateStore.cs:36`, `AgentScenarioStore.cs:50`,
`OnnxCrossEncoderReranker.cs:252`, `RagEvalService.cs:205`,
`NativeKokoroVoiceProvider.cs:46,56`, `SetupWizardViewModel.cs:281`,
`SettingsViewModel.cs:329`, `Desktop/Program.cs:27`, and any others the
grep finds. All become `Hermaeus`.

No migration code. The owner renames their existing folder by hand (doc 04).
Note that `settings.json` itself lives in that folder: after the rename the
app will cold-start with defaults until the owner moves the folder, which
is expected and harmless.

**Acceptance:** fresh launch with no existing folder creates
`{LocalApplicationData}/Hermaeus` and nothing under an `Aether` path
(assert via a test on the resolved default, and a manual launch check).

## 2.2 SQLite schema-version table (shim in scope)

Both migration-runner copies (`src/Aether.Rag/Storage/SqliteMigrationRunner.cs:40,51,61`
and `src/Aether.Agent/Services/SqliteMigrationRunner.cs:51` plus its CREATE)
own `aether_schema_versions`. Rename to `hermaeus_schema_versions`, with a
one-time shim: before CREATE, if `aether_schema_versions` exists and
`hermaeus_schema_versions` does not, `ALTER TABLE ... RENAME TO ...`.
Without the shim, every existing database re-runs all migrations from
version 0 against already-migrated tables and additive ALTERs throw. The
shim is three lines and protects the owner's live conversations/RAG/agent
databases; it satisfies the additive-only migration rule (a rename of the
bookkeeping table, no schema shape change).

Update `MigrationRunnerTests.cs:43,94,106` and `Tests/Program.cs:215`, and
add one test per runner copy: seed a db with the legacy table at version N,
open with the new runner, assert version still reads N and no migration
re-runs.

## 2.3 Single-instance guard and process identity

`SingleInstanceGuard.cs:19`: mutex/base name `"Aether"` and `"aether.lock"`
become `"Hermaeus"` / `"hermaeus.lock"`. Side effect: one old-named
instance and one new build could run simultaneously exactly once during
the owner's upgrade; not worth guarding.

## 2.4 Crash and lifecycle log filenames

- `Desktop/Program.cs:69,77`: `aether_unhandled.log` / `aether_unobserved.log`
  become `hermaeus_unhandled.log` / `hermaeus_unobserved.log`.
- `DoctorService.Startup.cs:65` reads the same names; keep reader and
  writer in one commit. `CrashLogReaderTests.cs:14,31` follow.
- Old crash logs under the previous names stop being surfaced by Doctor.
  Clean break, no fallback read: acceptable, note it in the CHANGELOG entry.
- `AppLifecycleJournalService.cs:48` journal folder follows 2.1.

## 2.5 Local API headers

`LocalApiTokenAuth.cs:22` `X-Aether-Token` and `LocalApiEndpoints.cs:29`
`X-Aether-Client` become `X-Hermaeus-Token` / `X-Hermaeus-Client`. Breaking
for any external client script; the app is alpha and the owner's scripts
are the only clients. Update `LocalApiTests.cs` (:396 and header uses) and
the Local API section of the docs (doc 03 sweep).

## 2.6 Outbound identity strings

- User agents: `DoctorService.Runtime.cs:514` and `LlamaServerSetupService.cs:62`
  `"Aether-Doctor/1.0"` become `"Hermaeus-Doctor/1.0"`;
  `HuggingFaceClient.cs:29` `"Aether/1.0"` becomes `"Hermaeus/1.0"`.
- MCP client identity: `McpClient.cs:108` client name `"Aether"` becomes
  `"Hermaeus"` (sent to MCP servers during initialize).

## 2.7 OS secret store service names

`SecretStore.cs:349-390`: Linux `secret-tool` attribute `application Aether`
and macOS keychain service `-s Aether` become `Hermaeus`. Existing secrets
stored under the old service name on those platforms are orphaned (not
deleted); the owner runs Windows (DPAPI file under the data root, follows
2.1's folder move), so no shim. Doctor already surfaces missing provider
keys, which is the recovery path.

## 2.8 Agent workspace manifest directory (shim in scope)

`WorkspaceManifestService.cs:8` `.aether/workspace.json` becomes
`.hermaeus/workspace.json`. Shim: when reading and `.hermaeus/workspace.json`
is absent but `.aether/workspace.json` exists, read the legacy file; always
write to the new path. This preserves the owner's existing workspace
manifests (voice profiles etc.) in real repos at near-zero cost. Update
`AgentViewModel.cs:1368` status text, `Tests/Program.cs:337`,
`AgentScenarioRunnerTests.cs:191,193`. Add one test for the legacy-read
fallback.

## 2.9 Voice lexicon and Kokoro literals

- `KokoroUserLexicon.cs:25` seeds a pronunciation for `"aether"`; `:85`
  writes the file header comment. Keep the `aether` entry (it is a real
  English word CMUdict may still miss), retitle the header, and add a
  `hermaeus` seed entry so the product name is pronounced correctly:
  ARPAbet `HH ER0 M IY1 AH0 S` (her-MEE-us), mapped through the existing
  `ArpabetIpaMap` conventions. Reminder from r8: Kokoro's vocab has no
  U+025D (rhotacized open-mid central vowel); the rhotic must land as the
  two-glyph form the map already emits.
  Non-ASCII goes into the .cs file as the map produces it at runtime or as
  escape sequences, and either way byte-verify the result (see the
  Unicode-escape gotcha in AGENTS.md workflow memory; construct expected
  glyphs via codepoints in the verification script, never by typing them).
- `NativeKokoroVoiceProvider.cs:192` temp wav prefix `aether-kokoro-native-`
  becomes `hermaeus-kokoro-native-`.
- Add a golden test: `hermaeus` phonemizes to the seeded IPA.

## 2.10 Benchmark fixtures and metadata

- `BenchmarkService.cs:590,688-690`: the JSON-format test prompts use
  `Aether` as a literal expected value; change prompt and expected value to
  `Hermaeus` together (the check is exact-match, so half-changing it breaks
  the benchmark case).
- `:727-729` suite id `aether-workflows` / name `Aether Workflows` become
  `hermaeus-workflows` / `Hermaeus Workflows`. Old persisted runs keep the
  old suite id; the r5-era suite-join fallback tolerates this. No rescoring.
- `BenchmarkMetadata.cs:18` `AetherVersion` becomes `AppVersion` (neutral,
  so a future rename never touches it again). Callers: `BenchmarkService.cs:851,990`,
  `BenchmarkInsightsModels.cs:271`, tests at `BenchmarkInsightsServiceTests.cs:71`,
  `BenchmarkInsightsMathTests.cs:21`. Check how runs are serialized in the
  eval store: if the JSON key is the property name, old runs deserialize
  with an empty AppVersion and the insights staleness check already treats
  version mismatch as stale, which is the correct degraded behaviour;
  confirm no exception path, and add a compat test deserializing a
  pre-rename run JSON blob.
- `ServiceTests.cs:105` suite id fixture follows.

## 2.11 Remaining internal literals

Rename for consistency (none has a compat concern):

- `BackupService.cs:23,41`: `aether-backup-*` zip and snapshot names.
- `ChatViewModel.cs:872` export filename prefix; `ChatView.axaml.cs:124`
  `aether-conversation.{ext}`.
- `AgentScenarioRunner.cs:67` `aether-scenario-runs` temp dir; `:306`
  data-root fallback (2.1).
- `MemoryStore.cs:666` `aether-memory-dimension-probe` probe string.
- `DoctorService.cs:152` `.aether-write-test-*` probe filename.
- `LocalAiSetupService.cs:763-844`: the Python check protocol markers
  `AETHER_VERSION` / `AETHER_ERROR` / `AETHER_CHECK` become `HERMAEUS_*`;
  emitter and parser live in the same file, change both plus the fake-python
  stub tests.

**Acceptance for the doc:** full suite green; a case-insensitive grep for
`aether` across `src/` finds zero hits outside the two deliberate legacy
shims (2.2 table rename, 2.8 manifest fallback) and the seeded `aether`
lexicon word, all of which doc 04's guard test allowlists by exact
file+content match.
