# 03 - Platform Cleanup And Security Follow-Ups

Items 3.2-3.4 implement the follow-ups added to `docs/security-review.md`
in the `0.10.0-alpha` refresh (done during r6 planning).

## 3.1 InspectionEngine dead-code resolution

r5 implementation found that `InspectionEngine`
(`src/Aether.Services/InspectionEngine.cs`) is registered in DI
(`AetherServiceRegistration.cs:42`) but consumed by nothing; checks
registered only through `IInspectionCheckProvider` never appear anywhere a
user looks, which is why the r5 doctor advisory landed directly in
`DoctorService.ScanAsync` instead.

Decision for r6: **remove the dead path** rather than wire it up. Doctor,
Trust, and Privacy Audit each already own their check lists and views; an
extra aggregation layer with zero consumers is speculative generality.

- First verify reachability: for each implementer of
  `IInspectionCheckProvider` (`DoctorService`, `TrustService`,
  `PrivacyAuditService`), confirm every check it can emit is also reachable
  through that service's own primary entry point (ScanAsync or equivalent).
  If any check exists only behind the provider interface, move it into the
  owning service's real check list first.
- Then delete `InspectionEngine`, `IInspectionCheckProvider`,
  `InspectionModels.cs` types that nothing else uses, the DI registration,
  the interface implementations, and their tests.

Acceptance criteria:

- No behavioral change to Doctor/Trust/Privacy Audit output (snapshot the
  check ids before and after).
- Zero remaining references to InspectionEngine/IInspectionCheckProvider.
- Build has zero warnings (no orphaned usings).

## 3.2 Recipe transparency in command approvals

Security-review follow-up. When an agent `run_command` approval is shown:

- `npm run <script>`: read the script body from the workspace's
  package.json (the same lookup `WorkspaceCommandRecipes.MatchNpmRunScript`
  already does, `WorkspaceCommandRecipes.cs:95-118`) and display it
  verbatim in the approval prompt: "Runs: <script body>".
- `dotnet build/test`, `cargo build/test`, `pytest`, `npm test`: display a
  fixed one-line note that the command executes workspace-defined build or
  test logic (MSBuild targets, build.rs, conftest, package.json test
  script), so approval is informed rather than nominal.
- The displayed script body is read at approval time; if package.json
  changed since the request, the user sees the current truth.

Acceptance criteria:

- npm run approval shows the exact script text from package.json.
- dotnet/cargo/pytest approvals show the fixed provenance note.
- Script body display is inert text (no markdown/control rendering).

## 3.3 Lesson review moment

Security-review follow-up: a poisoned lesson should be seen once by a
human before it influences future prompts.

- When a task reaches a terminal state and captured new lessons, the task
  summary area shows a "New lessons" strip: each lesson's claim, guidance,
  confidence, and two actions, Keep (default, no-op) and Retire (existing
  lifecycle call).
- Purely a surfacing change: no new approval gate, no blocking. Lessons
  remain active unless retired, exactly as today; the strip just makes
  their existence visible at the moment of creation.
- Lessons updated (evidence count bumped) rather than newly created do not
  appear in the strip.

Acceptance criteria:

- Task capturing 2 new lessons shows both; retiring one calls the store
  and removes it from future injection.
- Task capturing none shows no strip.

## 3.4 Remote voice disclosure

Security-review row: utterance text goes to the active TTS provider, and
non-Chat channels can speak app-generated text (toast messages, Doctor
findings) that may include local paths.

- In Settings > Voice, when the active provider is remote (OpenAI voice),
  each enabled channel row shows an inline note: "Spoken text is sent to
  the remote voice provider." Local providers (Kokoro native, XTTS on
  loopback) show nothing.
- Privacy Audit gains one item when (remote provider && any channel
  enabled): "Voice: <n> channels send spoken text to <provider>."

Acceptance criteria:

- Note visible per enabled channel with OpenAI voice; absent with Kokoro.
- Privacy Audit item appears/disappears with the same conditions (unit
  test on the audit provider logic).
