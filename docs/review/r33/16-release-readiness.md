# R33 release-readiness verification

Date: 2026-10-05 (Australia/Brisbane).
Baseline: clean `r33/round` at
`8e2c72c2a222dbd8399dfa6a9f5d5c261caf2068`.
Version in `Directory.Build.props`: `0.41.0-beta`.

Status at the original VS Code receipt: **Windows builds, suites, driver and
package checks passed; desktop dogfooding awaited a working Computer Use
session**. The desktop continuation below supersedes that access limitation.
This receipt does not declare R33 ready for release.
The implementation, native repair, and earlier owner evidence remain in
[14-owner-dogfood-closeout.md](14-owner-dogfood-closeout.md) and
[15-windows-native-crash-investigation.md](15-windows-native-crash-investigation.md).

## Automated evidence

Commands run sequentially on the approved Windows host. Test output is outside
the checkout under `%TEMP%/hermaeus-r33-readiness-tests`.

| Check | Command | Result |
| --- | --- | --- |
| Debug build | `dotnet build Hermaeus.sln` | Passed, zero warnings/errors |
| Debug suite | `dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj --no-restore --logger 'trx;LogFileName=r33-readiness-debug.trx' --results-directory "$env:TEMP\hermaeus-r33-readiness-tests"` | 2,822 passed, zero failed/skipped |
| Release build | `dotnet build Hermaeus.sln -c Release` | Passed, zero warnings/errors |
| Release suite | `dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj -c Release --no-restore --logger 'trx;LogFileName=r33-readiness-release.trx' --results-directory "$env:TEMP\hermaeus-r33-readiness-tests"` | 2,822 passed, zero failed/skipped |
| Release production-composition driver | `dotnet run --project src/Tools/R33Driver/R33Driver.csproj -c Release --no-build -- --settings-path <scratch>/settings/settings.json --data-root <scratch>/data --workspace <scratch>/workspace` | Exit 0, `ok:true` |
| Windows RID restore | `dotnet restore Hermaeus.sln -r win-x64` | Passed |
| Windows package | `pwsh ./build.ps1 -SkipRestore -Runtime win-x64` | Passed |

The driver verified an Applied, changed, readback-verified file mutation;
persisted Benchmark cancellation with zero results; RAG generation/retrieval
and Chat context; the scripted voice boundary; Lab failure cleanup; and Lab
Apply followed by settings reopen at context 512. Settings, data and workspace
used a fresh external scratch directory, which was removed after the run.
The output receipt remains at `r33-readiness-driver.txt` in the external test
results directory. Scripted model, embedding, voice and Lab runtime boundaries
make this production-composition evidence, not live model, audio-device or GPU
evidence.

No production behaviour, configuration, persistence format, dependency, version,
or security policy is changed by this pass. No feature, user-guide, workflow,
or changelog update is required for the review-record correction alone.
Coverage was not rerun. It remains the final precommit gate; no commit is
authorised in this pass. No Linux build or native desktop test was performed.

## Rebuilt Windows package

Generated package: `dist/hermaeus-0.41.0-beta-win-x64/`.
Archive: `dist/hermaeus-0.41.0-beta-win-x64.zip`.

- Package and ZIP each contain 208 files, with zero PDB files.
- The root launcher, Desktop apphost and LocalApi apphost exist in both the
  package directory and ZIP. The build script's layout validation passed.
- The ZIP SHA-256 matches its sidecar:
  `b2f4b6bc7fe99ac5528b86f0884489796a8454620dbf7b153b1160d7aaaa14c5`.
- Packaged `app/Hermaeus.Services.dll` matches the Release build, SHA-256
  `1642bb53a38242b44d1538d9810bdf6af3f9ec5578972d6c99537b47ee01e477`.
  Its NVML v2 record is 24 bytes.
- The package was rebuilt from the baseline production source. Only review
  documentation changed in this pass. Generated package files remain ignored;
  TRX and driver receipts remain outside the repository.

The package was not launched in this extension session. To continue, open this
repository in the ChatGPT desktop app, read this receipt, verify that native
`list_apps` succeeds, and dogfood the package's `Hermaeus.exe`. Use scratch
Workspace files for edits and Agent mutations. Keep the owner's settings and
installed model identities in place unless a specific workflow requires an
explicit change. Record actual clicks, output, process lifecycle, screenshots
and playback separately. The current build supports opt-in
`HERMAEUS_NATIVE_PROBE_TRACE=1` diagnostics described in doc 15.

## Desktop evidence boundary

The Windows Computer Use API imported successfully, but the first real
`list_apps` call failed with:

```text
Computer Use native pipe is unavailable: failed to connect native pipe:
The system cannot find the file specified. (os error 2)
```

A delayed retry and a fresh JavaScript runtime/import produced the same failure.
The owner confirmed this conversation is running in the VS Code extension,
with Computer Use enabled separately in the ChatGPT desktop app. The current
extension session has no working native Computer Use connection. Resume the
GUI walkthrough from the ChatGPT desktop app with this repository open.
Loading API methods is not proof of a working desktop connection. No screenshot,
click, keyboard interaction, model execution, audio playback, or unattended
stability pass is established by those attempts. This is a session access limit,
not evidence of a Hermaeus defect.

The pre-existing Windows package was inspected without launching it. Its
`NvidiaProcessMemoryProbe.NvmlProcessInfo` is 24 bytes and contains `Pid`,
`UsedGpuMemory`, `GpuInstanceId`, and `ComputeInstanceId`. That confirms the
specific v2 ABI correction is present, not historical crash attribution or
general native stability. The owner's settings were inspected without saving;
setup is complete and there is no pending Data Root migration.

## Remaining release gates

- Packaged Windows desktop walkthrough: Workspace edit/Save/Ctrl+S and external
  conflicts; per-patch approval and parent/child owner interactions; task
  isolation, convergence, and cancellation with a real provider.
- Installed-model walkthrough: model switch and recovery, Lab discovery/run/
  cancel/restore/Apply, exact AutoTune confirmation, Benchmark cancellation and
  evidence, RAG query scope/citations, and runtime PID/RAM/VRAM attribution.
- Device and visual checks: audio cue/TTS playback, Doctor target navigation,
  Hugging Face selection/cancellation, Moss blink, narrow layout, focus and DPI,
  telemetry flyout lifecycle, clean exit and long unattended stability.
- Linux/COSMIC native validation, including the unresolved AvaloniaEdit flick.
  Windows automated results cannot close Linux desktop gates.
- Historical Windows crash attribution still requires the evidence described in
  doc 15 if it recurs. A passing suite does not establish that attribution.
- Owner-controlled PR/merge-context CI, version decision, merge, tag and release.
  No publication action is authorised or performed here.

Earlier scoped owner passes are retained. These open rows do not revoke them
or silently expand them to other platforms, models, controls, or workflows.

## Windows desktop continuation, 2026-10-06

Native Computer Use works in the approved desktop session. Only Hermaeus and
its owned surfaces were operated. The branch remains `r33/round` at the same
HEAD. The inherited README edit and this untracked receipt were preserved.
No commit, push, branch, release, or publication action was performed.

The initial walkthrough used the original package above, with the owner's
configured `C:\Hermaeus\` Data Root and `C:\AI\` assets. New Agent mutations
used a dedicated scratch workspace outside the repository under the approved
visualisation root. Existing pending owner tasks were left untouched.

### Observed workflows on the original package

- Real local Gemme E4B Chat answered `17 × 23` as `391`. Telemetry showed
  decode 57.1 tokens/s, prompt 911.8 tokens/s and server RAM 2,510.4 MiB.
  Process VRAM remained explicitly Unknown with an unavailable-process-GPU
  reason. These are one host/run's observations, not performance promises.
- AvaloniaEdit keyboard entry and Ctrl+S changed the scratch file on disk.
  A later external edit caused Ctrl+S to refuse the stale draft with
  `Conflict. Reload before saving.`; the external content remained intact.
- A real-provider Agent run prepared a Medium-risk `create_file` action for
  `receipt.txt`. The file did not exist before approval. The exact synthetic
  action was approved, applied and read back as `R33 agent approval verified`.
  The task completed in five steps with one approval and no commands. Its
  unfinished plan entries remained visible as reservations; completion is not
  proof that the model marked every plan entry complete.
- New Task cleared the synthetic task projection while retaining persisted
  task history. No old owner approval was answered.
- A Lab experiment launched an owned loopback runtime and cancellation
  restored the selected Chat source. The baseline evidence was not auditable,
  so no comparison, recommendation or Apply pass is claimed.
- A Benchmark workload completed with explicit unverified-runtime evidence and
  was excluded from rankings. A separate longer run was cancelled and its
  partial result saved. The first, fast run is not cancellation evidence.
- RAG questions used only the selected Skyrim dataset despite the manager
  dropdown showing Hermaeus. Sources exposed revisions and hashes. The model
  refused to invent answers from unrelated passages. Diagnostics disclosed a
  Nomic/Qwen embedding-model mismatch and keyword fallback; the owner's
  dataset was not reindexed or deleted.
- Normal window shutdown recorded `CleanExit:true`. Close to tray was
  temporarily disabled through Hermaeus for the package rebuild and must be
  restored after reopening. A host process audit found no remaining Hermaeus,
  llama-server or test host after shutdown. Idle build workers were preserved.

### Defects and bounded repairs

1. Successful Workspace reads left a previous read failure in the global
   header. Listings and selected-file reads now clear only their own earlier
   error, preserving newer task or save failures.
2. Detaching a shared MarkdownViewer permanently unsubscribed its rendering
   timer. Reused tabs could retain `No summary yet` despite a persisted Agent
   response. Detachment now pauses rendering, reattachment resumes it, and
   abandoned asynchronous renders cannot suppress a later refresh.
3. Lab availability refreshes rebuilt the selected server snapshot and reset
   the candidate during source suspension. Same-server refreshes preserve the
   draft; manual and guided runs capture source and inputs before awaiting
   suspension. Selecting a different server still resets the baseline draft.
4. FTS keyword candidates were capped in storage order before relevance
   ranking. A real SQLite regression reproduced loss of a rare relevant match
   after earlier common-term documents. FTS relevance now precedes the cap.
   The shared path covers RAG and document Recall; both have regression proof
   with more than 400 matching documents.
5. Effective context parsing accepted `params.n_ctx` as runtime capacity and
   could overwrite stronger capacity evidence with `512`. Per-slot startup
   lines also overwrote total context. Parser v3 uses total capacity or derives
   it from reported per-slot capacity and slot counts, preserves total startup
   capacity, and leaves incomplete or non-capacity observations Unknown.
   Existing receipts retain their original parser identity.

The read-error audit covered both listing and selected-file recovery. The Lab
audit covered manual and guided suspension paths. The retrieval audit covered
both shared candidate callers and the phrase-only LIKE fallback. The context
audit covered root/nested properties, startup fallback, multi-slot capacity,
missing/invalid counts and overflow. No approval, mutation, privacy or runtime
association gate was weakened. No dependency, version or owner persistence
format was changed. Features, user guide, Agent, Lab, RAG, Benchmark and
changelog documentation were synchronised.

Focused workspace/Lab tests passed 47/47. The broader affected group passed
169/169, and the context/RAG/launch group passed 201/201 before the additional
incomplete-count boundary cases. One new workspace test initially raced its
automatic refresh during the full suite; it now awaits the root-change
completion instead of launching a competing refresh. FTS and context
regressions were observed failing before their production repairs.

Final Debug and Release solution builds passed with zero warnings/errors.
The final Debug suite passed 2,838 tests, with zero failed/skipped, and retained
`r33-dogfood-debug.trx` under `%TEMP%\hermaeus-r33-desktop-tests`.
The final Release suite also passed 2,838 tests, with zero failed/skipped, and
retained `r33-dogfood-release.trx` in the same external directory. Debug took
2m16s; Release took 3m23s. No test host remained after completion. Coverage was
not rerun because no commit is authorised.
Rebuilt-package interaction checks remain in progress at this point in the
receipt. Markdown's native detach/reattach behaviour requires the packaged
interaction check; pure parser tests do not close that claim.

### Audio and Doctor continuation, 2026-10-06

The owner resumed with the rebuilt Windows package (archive SHA256
`09a56baa94740d40b26735542694847b76de63229c37938b75953af62a25e2f5`).
That package contained the five repairs above, 208 files and no PDBs. It does
not contain the later audio and Doctor changes described below.

The owner reported that semantic cues still sounded like one Windows beep.
A direct native Windows probe used the actual source WAV generator with
default-sound substitution disabled. Windows returned success, but the owner
still heard one beep. A second probe changed only note duration and spacing
to 400/300 ms; Windows returned success and the owner heard three separate
rising notes. This establishes a perceptibility problem with the original
110/45 ms cue, not proof that Windows substituted its default sound.

Semantic cues now use 250 to 300 ms notes separated by 150 ms pauses, with
three-note cues bounded to 1.05 seconds. Recording start/stop intentionally
retain their short single notes. Windows playback now disables default-sound
substitution on the shared cue/TTS path and reports an explicit playback
failure if the WAV cannot be played. It does not attribute an unsupported
native last-error code from a different continuation thread. The generated
PCM regression checks actual note frequencies, silent gaps and total duration;
a Windows-only native regression rejects a missing WAV without playing a
default sound. Final production timings still need packaged owner audibility
verification; the 400/300 ms diagnostic probe is not that verification.

The running Doctor screen left the latest llama.cpp release and comparison
Unknown, while the Hermaeus release check reported an anonymous GitHub request
was rate-limited or rejected. No failed install was recorded in the runtime
log inspected during this pass. The screen does not establish a GitHub outage
or the exact cause of the owner's earlier update attempt.

Doctor previously discarded the llama.cpp lookup exception. It now retains
that reason in the check detail, copied diagnostics and cached failed lookup.
Both release-check paths report confirmed limits only for HTTP 429 or an
exhausted quota header; an unqualified HTTP 403 remains an uncertain rejection.
Owner cancellation propagates instead of becoming unavailable metadata.
Regression coverage includes qualified/unqualified 403, 429, 503 and cancellation.
SHA256 verification, compatible asset selection, accelerated-backend safety,
installation ownership and the installed runtime were not changed.

The owner linked upstream `v0.6.0`. Its release page links binary downloads to
nightly `b11429`, which has Windows CPU, CUDA and Vulkan assets. The existing
selector intentionally uses compatible b-numbered binary releases and already
has semver-skip regression coverage. The versioned release itself does not
explain an HTTP rejection before release selection. Sources inspected:
https://github.com/ggml-org/llama.cpp/releases/tag/v0.6.0 and
https://github.com/ggml-org/llama.cpp/releases/tag/b11429.

Debug and Release solution builds passed with zero warnings/errors. The full
Debug suite passed 2,851/2,851, zero failed/skipped, in 3m39s. Results remain
outside the checkout in `r33-audio-doctor-debug.trx` under the same external
results directory. The affected Release group, including release selection,
passed 113/113. The earlier focused Debug group passed 53/53 before the final
two diagnostic cases; the full suite covers those final additions. Coverage
was not run because no commit is authorised. Authoritative voice, feature,
user-guide and changelog documentation was synchronised.

Automatic approval review blocked the native Agent-history recheck because
that screen includes unrelated historical calculator-workspace metadata.
Permission for that limited read was requested; older tasks remain untouched.
Package replacement/reopening and final audio/Doctor native checks remain
pending at this receipt point. Close to tray remains temporarily off and must
be restored through Hermaeus after the package checks.

### Dismissal repair and local commit verification, 2026-10-06

The owner confirmed that the Agent history is dogfood output and reported that
repeated Dismiss clicks leave a paused parent in the queue. The service refused
parents with unfinished children, while direct child dismissal was also refused.
Stopping the parent left those children resumable, so it did not resolve the
dead end.

Dismiss now validates ownership before changing any child, cancels owned
unfinished children under their command gates, discards pending actions and
questions without approving or executing them, and skips unstarted work.
Completed child evidence and earlier approvals remain intact. Running parents
still require Stop first; direct child dismissal remains refused. Cancelled
children reconcile as skipped after restart or a partial dismissal, so they
are not replayed. Child and parent state files retain their existing atomic
save boundary; cancellation across multiple task files is not one transaction.

Regression coverage exercises paused parents, child questions and pending
writes, restart recovery, foreign-child protection, preserved completed work
and partially cancelled orchestration. The focused Debug group passed 35/35.
Final Debug and Release solution builds passed with zero warnings/errors;
the full Debug suite passed 2,855/2,855 with zero failed/skipped in 3m31s.
Results remain outside the checkout in `r33-final-commit-debug.trx` under
`%TEMP%\hermaeus-r33-desktop-tests`.
The final Release Agent review-queue, Workspace and sub-task orchestration
group passed 47/47 with zero failed/skipped, retained in
`r33-final-dismiss-release.trx` in the same external directory.

The owner authorised meaningful local commits of the reviewed changes and
explicitly prohibited pushing. This supersedes the earlier no-commit state
recorded above. The running package predates this dismissal repair and the
audio/Doctor source changes. Packaged dismissal, Markdown navigation, final
cue audibility and Doctor diagnostic checks remain owner-live release gates.
The temporary Close to tray change was restored through Hermaeus Settings;
both Close to tray and Show tray icon are enabled again, confirmed in the
visible checkbox and the persisted preference fields. No task state was edited
outside Hermaeus during this pass.
The final pre-commit coverage run passed 2,855/2,855 with zero failed/skipped
and measured 66.31% line coverage (54,258/81,830), above the 60% floor.
The existing coverage script used an external GUID-named temporary results
directory and removed it after checking the Cobertura counts. Source and tests
were unchanged between this verification and the local commit pass.
