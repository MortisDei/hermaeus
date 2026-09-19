# R33 owner-dogfood closeout

Status: current R33 closeout record for `r33/round`, carried from inherited
`HEAD` `b620944` and updated through 2026-09-19. It records the owner-authorized
local consolidation before publication; exact commit, push, and Branch CI state
are handoff facts, not historical evidence claims in this ledger.

The documents numbered 01-13 remain historical planning and evidence records.
This document is the current disposition of the pasted R33 A-T brief. It does
not turn source inspection into GUI, native-driver, playback, or live-runtime
proof, and it makes no screenshot claim.

## Disposition vocabulary

- **FIXED:** the bounded implementation is present and representative automated
  or static evidence passed.
- **VERIFIED EXISTING:** the requirement was already satisfied and was checked
  without expanding scope.
- **NEEDS OWNER VALIDATION:** source and automated evidence are present, but the
  remaining claim requires a real desktop, platform, device, or installed
  runtime walkthrough.
- **UNRESOLVED:** the reported issue has no proven causal repair in this pass.
- **DEFERRED:** deliberately outside this round, with no claim of completion.

## A-T matrix

| Cell | Disposition | Current evidence and remaining boundary |
| --- | --- | --- |
| A. Workspace editor | **FIXED; NEEDS OWNER VALIDATION** | Monaco, WebView, the embedded bundle, fallback wording, and 154 Monaco assets were removed. `WorkspaceEditorView` uses AvaloniaEdit only, with editable text, line numbers, highlighting, and direct save wiring. Static/source guards and `R33UxAndPetTests` passed. Owner must still check native rendering, focus, keyboard/IME, DPI, resize, large files, and teardown on Linux and Windows. |
| B. Owner save | **FIXED; NEEDS OWNER VALIDATION** | Direct Workspace Save/Ctrl+S performs bounded UTF-8/NUL/extension checks, rereads the expected revision, writes through atomic replacement, returns the new SHA-256, and refuses an external conflict without overwriting. `WorkspaceOwnerSaveTests` covers direct save, hash, conflict, unsafe text, and queued Agent-patch conflict. Agent patches remain prepared, reviewable, and approval-gated. An external edit after the final reread is still an unavoidable filesystem race, not claimed as a perfect CAS. |
| C. Agent convergence | **FIXED; NEEDS OWNER VALIDATION** | `AgentConvergencePolicy` fingerprints equivalent tool outcomes and repeated ask-user questions, bounds non-progress at three observations, resets on changed read results, consumes an answer, and stops truthfully. Read-only tools bypass model approval flags; create actions remain policy-gated and are not blocked by missing command recipes. Focused convergence tests and the 31-test R33 focused set passed. Owner must exercise real-provider delayed, blocked, read-only, ask, approval, and create flows. |
| D. Refusal evaluator | **FIXED; NEEDS OWNER VALIDATION** | Core `RefusalEvaluator` v2 is provider-neutral and shared by Benchmark and RAG answer evaluation. Direct, indirect, hedged, and clarification refusals are valid when refusal is expected; mixed, hallucinated, and empty answers are invalid. Suite/run evaluator versions and rerun lineage are persisted and inspectable. Sixteen evaluator-focused tests passed. A provider/runtime answer corpus walkthrough remains owner validation. |
| E. Runtime evidence | **FIXED; NEEDS OWNER VALIDATION** | Lab and Benchmark use the fail-closed runtime evidence envelope with requested, resolved, launched, effective, and telemetry identity kept distinct. Unsupported or untrustworthy values stay Unknown/ineligible. The driver and runtime evidence regressions passed. Actual managed runtime launch and evidence collection with installed models remain owner gates. |
| F. Lab recipes and V-cache | **FIXED; NEEDS OWNER VALIDATION** | Recipe admission, empty-path handling, restore containment, runtime identity, and the Q8 V-cache requirement for explicit Flash Attention `on` are covered by existing and focused tests/docs. Real model assets, native backend behavior, and Linux/Windows Lab walkthroughs remain owner validation. |
| G. Lab/Benchmark result order | **FIXED; NEEDS OWNER VALIDATION** | Results-first and progressive disclosure are represented in the ViewModels, views, and layout regressions, including terminal outcomes and evidence visibility. Owner must confirm usable hierarchy, artifact width, scrolling, and narrow-window behavior. |
| H. Empty Lab path | **FIXED; NEEDS OWNER VALIDATION** | No eligible server or recipe produces an explicit unavailable state and disabled action path rather than a misleading runnable control. Static and Lab ViewModel coverage passed. Owner must check the empty state against real installed configurations. |
| I. AutoTune | **FIXED; NEEDS OWNER VALIDATION** | A successful transient probe is followed by a second confirmation probe reconstructed from the exact candidate configuration and observed identity. Unlaunchable or mismatched results cannot be persisted, and the blanket 16K cap is absent. AutoTune lifecycle, profile validation, and full-suite tests passed. Real unlaunchable configurations and installed-model confirmation remain owner validation. |
| J. Audio cues | **FIXED; NEEDS OWNER VALIDATION** | Supplementary/system cue selection is separate from TTS. The bounded generated asset and playback trace record event selection, resource, backend, and result; playback/asset failures are diagnosable and non-fatal. Audio tests passed with deterministic playback gating. Linux and Windows device/backend playback remain owner validation. |
| K. Windows RAM/VRAM | **FIXED; NEEDS OWNER VALIDATION** | Unsupported RAM/VRAM stays Unknown. Process telemetry is associated with exact PID, process start, and runtime executable identity; NVIDIA process VRAM is used only when trustworthy, with bounded polling/cache behavior. Runtime telemetry tests and the full suites passed. An NVIDIA-capable Windows runtime is still required for live proof. |
| L. Abrupt Windows crash | **UNRESOLVED; NEEDS OWNER VALIDATION** | Existing lifecycle/crash markers and Doctor startup inspection were reviewed. No causal product repair is claimed from this pass. The separate testhost heap-corruption abort was not treated as product evidence because a blame-crash rerun completed cleanly. If the owner reproduces the crash, retain the exact lifecycle journal, crash marker, WER/native evidence, and process identity before assigning causality. |
| M. Doctor update action | **FIXED; NEEDS OWNER VALIDATION** | The llama update action is the primary remediation button; Services navigation is secondary and remains available for configuration. Doctor advisory/navigation tests passed. Owner should verify the actual navigation and installed updater flow. |
| N. Hugging Face details | **FIXED; NEEDS OWNER VALIDATION** | Selecting a repository brings its details and quantizations into view through the existing workspace projection and explicit view navigation. Structural tests passed. Owner should check the real selected-repository interaction and narrow layout. |
| O. Moss blink and pet security | **FIXED; NEEDS OWNER VALIDATION** | Blink changes only an eye-band overlay. The complete pet bitmap/package remains present, and import/security boundaries are unchanged. Pet package and structural tests passed. Desktop companion rendering and timing remain owner validation; broader desktop-companion work is deferred. |
| P. UX simplification | **FIXED; NEEDS OWNER VALIDATION** | Changed workflows expose the primary job, current state, next action, and terminal evidence before secondary detail. Narrow layout bindings and progressive disclosure are covered by source/layout tests. Owner must validate visual hierarchy, contrast, accessibility, focus, DPI, and responsive behavior. |
| Q. Performance optimization | **VERIFIED EXISTING; DEFERRED** | No R34 performance optimization was added. Existing instrumentation and evidence semantics were preserved; no new performance claim is made. |
| R. Documentation | **FIXED** | Current-facing features, user guide, Agent, Benchmark, Voice, Lab, changelog, and this closeout record describe the current `r33/round` behavior. Older planning records retain their evidence with explicit historical notes. Platform/live versus automated evidence is separated, and no screenshot is claimed. |
| S. Verification gates | **FIXED** | Debug and Release builds, full sequential tests, the focused R33 set, Release driver, Windows RID restore/package, archive checksum/layout/no-PDB checks, static source checks, and final coverage are recorded below. A transient Debug testhost crash was rerun with crash diagnostics and did not reproduce. |
| T. Handoff | **FIXED; NEEDS OWNER VALIDATION** | This matrix and the staged owner checklist below are the handoff. Owner validation and any publication/PR checks remain outside this local working-tree closeout. |

## Automated and static evidence

- Focused R33 set: `31/31` passed, `0` skipped, `0` failed. This included the
  workspace owner-save, convergence, refusal-evaluator, Doctor, audio, editor,
  Hugging Face, layout, and pet regressions.
- Focused refusal-evaluator set: `16/16` passed.
- Full Debug harness rerun with `--blame-crash --blame-crash-dump-type mini`:
  `2,777` passed, `0` skipped, `0` failed.
- Full Release harness: `2,778` passed, `0` skipped, `0` failed.
- Debug solution build: passed with `0` warnings and `0` errors.
- Release solution build: passed with `0` warnings and `0` errors.
- Release R33 driver: exited `0`, returned `ok:true`, and verified an Applied,
  changed, readback-verified mutation plus benchmark cancellation persistence,
  RAG/Chat/voice, Lab failure cleanup, and Lab Apply/reopen evidence.
- Windows package: `pwsh ./build.ps1 -SkipRestore -Runtime win-x64` passed after
  `dotnet restore Hermaeus.sln -r win-x64`. The package directory, root
  `Hermaeus.exe`, desktop apphost, LocalApi apphost, ZIP, and SHA-256 sidecar
  exist; the package contains 208 files, 0 PDB files, and the recorded SHA-256
  matches `dfb6bd3843b9acc93f557b8b6307f3c78f85b63edccc8c666a58b5c505859f27`.
- Static source audit found no Monaco, WebView, or fallback-editor references in
  production source. Remaining matches are negative regression assertions or
  explicitly historical review text.
- `git diff --check` passed. Generated `bin`, `obj`, `dist`, and test-result
  state are not part of the intended source change.

- Final coverage gate: `64.04%` line coverage (`37,160/58,024`) and `58.14%`
  branch coverage, above the 60% line-coverage ratchet. The coverage report
  and results remained under the Windows user temp root.

## Staged owner checklist

1. On Linux and Windows, launch the packaged app and verify workspace selection,
   edit, Ctrl+S/direct Save, revision status, external-conflict reload, and the
   distinction between direct owner saves and Agent prepared patches.
2. Exercise Agent ask-user, read-only, approval, blocked, delayed, failure,
   cancel, and create flows with a real provider. Confirm repeated equivalent
   non-progress ends truthfully and an answer is consumed.
3. Walk Benchmark and RAG refusal cases, inspect evaluator version/classification,
   rerun lineage, citations, and retrieval-only refusal behavior.
4. With real installed models, inspect Lab empty paths, every eligible recipe,
   Q8 V-cache with explicit Flash Attention `on`, restore containment, and
   AutoTune confirmation/persistence for both launchable and unlaunchable cases.
5. Verify Linux/Windows supplementary audio cues and playback diagnostics, plus
   Windows process RAM/VRAM identity with the available driver/GPU state.
6. Verify Doctor's primary update action, secondary Services navigation, HF
   repository detail reveal, Moss eye-only blink, and narrow/DPI/focus/accessibility
   behavior. Do not treat a source-level check as a screenshot or GUI pass.
7. If the abrupt Windows crash recurs, capture lifecycle/crash/WER/native
   evidence and process identity before changing the causal classification.
8. Owner controls the eventual PR, CI/CodeQL policy, merge, release, and tag.

## 2026-09-19 owner-validation repair addendum

This addendum records the bounded repairs made after the owner reported the
R33 validation failures. The historical 01-13 records and the inherited R33
changes were not rewritten or discarded.

- **Lab empty-path failure:** production recipe inspection now validates the
  Services-selected model and llama-server executable before capability, GGUF,
  speculative-companion, or runtime-identity probing. An empty, missing, or
  unresolvable source returns actionable `Unavailable` placeholder plans. A
  production-facing regression covers the unconfigured server path.
- **Agent model and convergence failure:** parent and child model references
  resolve stable visible model ids, with provider-aware display-label
  compatibility only when unambiguous. The persisted parent identity is
  canonicalized before inference, inherited and explicit child identities are
  resolved before materialization, and unavailable models still block without
  fallback. The convergence bound is task-level, so mixed no-effect and blocked
  outcomes cannot reset the loop; changed read results reset the sequence.
  Prompt guidance and regressions cover a simple writable calculator goal on
  the parent path and legacy display-model resolution.
- **Supplementary audio partial failure:** cues no longer disappear merely
  because TTS is speaking. They wait behind TTS, recheck settings, then use the
  existing generated-WAV and platform playback path. The policy, resource,
  backend, and result stages remain diagnostic, with deterministic deferred-cue
  coverage. Windows and Linux device playback remain owner live gates.
- **RAM and VRAM:** the existing RAM live PASS is retained. Process VRAM stays
  `Unknown` when no trustworthy counter exists, and the Chat telemetry flyout
  now exposes the bounded evidence code and source detail explaining that
  state. NVIDIA-capable Windows live verification remains open.
- **Runtime compatibility:** fit and resource admission remain separate from
  native launch. Unsupported or invalid GGML tensor-type parser errors are
  classified as configuration failures rather than VRAM exhaustion, while the
  native error text remains in the launch evidence. This is verified existing
  behavior with an additional classification regression.
- **Shutdown and crash:** the owner shutdown PASS remains closed. The abrupt
  crash remains unresolved and no cause is assigned from the unrelated
  testhost crash evidence.

The focused repair set passed `76/76`, with `0` skipped and `0` failed. The
full Debug and Release suites each passed `2,784/2,784`, with `0` skipped and
`0` failed. The Release R33 driver returned `ok:true`, and `git diff --check`
passed. The final `scripts/coverage.ps1` run passed the repository's `60%`
line-coverage ratchet and removed its temporary report from the user temp root.
The working tree remains intentionally uncommitted; owner GUI, native runtime,
audio-device, GPU, and publication checks remain outside this local pass.

## 2026-09-19 owner-validation repair continuation addendum

This is an additional disposition for the next owner-validation failures. The
historical records and the preceding addendum above remain unchanged. The
attachment's A-M items are classified below using the same vocabulary as this
record.

| Item | Disposition | Evidence and remaining boundary |
| --- | --- | --- |
| A. Agent authority chain | **FIXED; NEEDS OWNER VALIDATION** | Prepared mutations now retain the Proposed, Approved, Applied, and Verified authority chain, with owner saves remaining atomic and separate from Agent approval. Focused and full suites passed. A real-provider owner walkthrough remains required. |
| B. Per-patch Changes authority | **FIXED; NEEDS OWNER VALIDATION** | Per-patch Approve, Reject, and Block use the authoritative prepared proposal and validate identity, revision, preimage, content, fingerprint, policy, and prepared time before transition. Production-facing ViewModel regressions passed. Owner must click each control in the packaged Changes surface. |
| C. Parent-owned owner interaction | **FIXED; NEEDS OWNER VALIDATION** | Parent mirrors child questions, approvals, and blocked actions with source identity. Parent decisions route to the exact child interaction, child results remain retained, and prompt, proposal, fingerprint, revision, and step checks reject stale answers. Parent and child interaction regressions passed. Owner must exercise delayed and interleaved provider flows. |
| D. Lab empty-path action | **FIXED; NEEDS OWNER VALIDATION** | Production recipe inspection validates the selected source and executable before capability, GGUF, companion, or runtime-identity probing. Empty or unresolved paths produce actionable Unavailable plans instead of reaching a path operation. Focused production-recipe coverage passed. Owner must check configured, missing, and unavailable runtime states with installed assets. |
| E. Supplementary audio cues | **FIXED; NEEDS OWNER VALIDATION** | Event kinds now use distinct generated multi-tone cues. Resource identity, pattern, every backend attempt, fallback reason, policy, and result are logged; playback failure remains nonfatal and TTS remains separate. Asset and fallback-seam tests passed. Windows and Linux device playback and cue audibility remain owner gates. |
| F. Hugging Face cancellation | **FIXED; NEEDS OWNER VALIDATION** | Owned cancellation and stale generations complete normally, navigation cancels active inspection, stale artwork/details cannot publish, and unrelated cancellation still propagates. Focused model, artwork, and startup-navigation regressions passed. Owner must verify rapid repository replacement and panel navigation in the packaged UI. |
| G. COSMIC/Wayland AvaloniaEdit flick | **UNRESOLVED; NEEDS OWNER VALIDATION** | Source inspection confirms a native AvaloniaEdit surface and found no causal platform workaround or reproducible source defect. No repair or screenshot claim is made. Owner must reproduce on COSMIC/Wayland with compositor, DPI, focus, resize, and input details before assigning causality. |
| H. Telemetry Unknown/detail | **VERIFIED EXISTING; NEEDS OWNER VALIDATION** | Existing telemetry preserves Unknown for unsupported process VRAM and exposes bounded evidence detail without substituting zero or whole-device totals. Automated evidence remains green. An NVIDIA-capable Windows live runtime is still required. |
| I. Llama logs/parser semantics | **VERIFIED EXISTING; NEEDS OWNER VALIDATION** | Existing runtime evidence keeps native parser text distinct from fit or VRAM classification and preserves bounded launch diagnostics. Automated evidence remains green. Installed native runtime launch and log inspection remain owner gates. |
| J. Shutdown and abrupt crash | **SHUTDOWN VERIFIED EXISTING; CRASH UNRESOLVED; NEEDS OWNER VALIDATION** | Shared shutdown passed in the driver and full suites. No supplied owner crash log or reproducible causal product evidence was available in the inspected workspace, local log, or attachment locations, so no crash cause or repair is claimed. If it recurs, capture lifecycle, WER/native, and process identity evidence first. |
| K. Documentation and ledger | **FIXED** | Agent, Features, User Guide, Voice, Changelog, and this current ledger now describe the additional behavior without rewriting historical records. Live proof remains explicitly separated from automated evidence. |
| L. Production seams and regressions | **FIXED; NEEDS OWNER VALIDATION** | Lab, audio, per-patch, parent interaction, Hugging Face cancellation, and navigation seams are covered by production-facing tests. The focused repair filter passed `191/191`, with `0` skipped and `0` failed. Owner desktop, device, provider, and native-runtime validation remains open. |
| M. Full verification and accounting | **FIXED; NEEDS OWNER VALIDATION** | Debug and Release solution builds passed with `0` warnings and `0` errors. Full sequential Debug and Release suites each passed `2,797/2,797`, with `0` skipped and `0` failed. The Release R33 driver returned `ok:true`. Windows packaging passed with 208 files, 0 PDBs, and a matching SHA-256 sidecar. The final coverage script passed the 60% line ratchet and cleaned its temporary report. Static Monaco/WebView audit and `git diff --check` passed. |

## Session-local accounting

- **Inherited R33 baseline:** the session began from `HEAD b620944` plus the
  intentionally dirty R33 working tree. The initial recorded snapshot was 220
  tracked status paths, 7 untracked paths, and a combined diff of
  `+1,381/-140,658`. The inherited untracked paths include this closeout
  ledger, convergence/model-identity/refusal services and tests, and the
  workspace owner-save tests.
- **Final combined snapshot:** the working tree has 232 tracked status paths,
  8 untracked paths, and a combined tracked diff of `+2,330/-140,703`. The
  numerical change from the initial snapshot is therefore 12 additional
  tracked status paths, 1 additional untracked path, and `+949/-45` in the
  combined tracked diff. Because this pass edited files that were already
  dirty, those numbers are accounting deltas, not a standalone patch size.
- **Additional changes in this repair pass:** Lab empty-model identity and
  recipe guards; distinct audio cue generation and backend diagnostics; Agent
  per-patch authority and parent interaction routing; Hugging Face owned
  cancellation and navigation handling; focused production regressions; and
  current-facing documentation. The additionally touched files are
  `RuntimeIdentityFactory.cs`, `RuntimeIdentityAndCapabilityTests.cs`,
  `LabRecipeTests.cs`, `AudioFeedbackAssets.cs`, `AudioFeedbackService.cs`,
  `AudioPlayback.cs`, `AudioFeedbackServiceTests.cs`, `AudioPlaybackTests.cs`,
  `AgentInterfaces.cs`, `AgentService.cs`, `AgentModels.cs`,
  `AgentViewModel.cs`, `AgentViewModelWorkspaceTests.cs`,
  `AgentOwnerInteractionTests.cs`, `ModelManagementViewModel.cs`,
  `MainWindowViewModel.cs`, `HuggingFaceClient.cs`,
  `HuggingFaceArtworkTests.cs`, `ModelManagementViewModelTests.cs`,
  `MainWindowViewModelStartupTests.cs`, `Program.cs`, `CHANGELOG.md`,
  `docs/agent.md`, `docs/features.md`, `docs/user-guide.md`, and
  `docs/voice.md`. Several were already dirty from inherited R33 work; this
  list identifies files additionally touched, not whole-file ownership.
- **Generated and transient artifacts:** Debug/Release build output and the
  Windows package remain generated or ignored state. Test result directories
  were outside the checkout, the R33 driver used an external scratch root,
  and the coverage script removed its external temporary report. No inherited
  or new source change was committed, reset, discarded, or published, and no
  destructive Git cleanup was used.

That inherited accounting described an intentionally uncommitted tree at the
time it was written. It was not a claim that owner GUI, COSMIC/Wayland
rendering, audio audibility, native model launch, NVIDIA process VRAM, or the
abrupt Windows crash had been closed.

## 2026-09-19 final consolidation addendum

This addendum records the final bounded consolidation performed after the
preceding repair continuation. The historical records and earlier addenda stay
unchanged; this section supersedes their older local test counts where they
describe the same working tree.

- **Agent persistence and convergence:** startup now rebuilds parent-owned
  interaction mirrors from authoritative child state, keeps valid child queue
  rows routable, and terminalizes orphaned children as `Interrupted` with the
  pending interaction cleared. Mutation receipts persist requested-action
  fingerprints, and equivalent verified post-images are blocked as task-level
  non-progress without language-specific heuristics. Legacy queue rows retain
  their indexed fallback when a full JSON state is unavailable.
- **Workspace editor:** directory identity and nullable modification times are
  preserved through listing and display. A missing trustworthy timestamp is
  shown as unavailable. Dirty owner edits survive refresh, while selection
  generation and revision checks prevent a slow prior load from replacing the
  active document or overwriting an external edit.
- **Shared runtime and result UX:** Benchmark ranking is scoped to the selected
  suite and explains missing eligible models or unverified evidence. Lab result
  rows lead with human outcome summaries and keep technical evidence behind
  disclosure. The shared runtime evidence path remains fail closed and does
  not infer effective configuration from process argv alone.
- **RAG and llama probe:** manual ingest and reindex are single-flight, so a
  duplicate start cannot create a competing generation. The llama `--version`
  probe has a bounded 15-second budget and reports elapsed time in timeout
  diagnostics.
- **Owner boundaries:** the COSMIC/Wayland AvaloniaEdit flick remains
  **UNRESOLVED; NEEDS OWNER VALIDATION** because this pass found no causal
  source defect or safe workaround. The abrupt Windows crash remains a
  separate **UNRESOLVED; NEEDS OWNER VALIDATION** item. Neither has a screenshot,
  native-runtime, or causal-repair claim here.

Final automated evidence for this consolidation:

- Debug and Release solution builds passed with `0` warnings and `0` errors.
- The affected Agent regression filter passed `148/148`, with `0` skipped and
  `0` failed. The documentation guard passed `13/13`.
- Full sequential Debug and Release suites each passed `2,810/2,810`, with
  `0` skipped and `0` failed.
- The Release R33 driver exited `0` and returned `ok:true` on an external
  scratch root. Its cancelled benchmark and failed Lab workload are deliberate
  failure-path receipts; Lab Apply/reopen and shared shutdown completed in the
  same driver result.
- The final coverage script passed the repository's `60%` line ratchet. Its
  instrumented suite passed `2,810/2,810` in `5 m 58 s`, and the temporary
  report was removed from the user temp root.
- `git diff --check` passed. Production source has no Monaco/WebView matches;
  remaining source matches are the negative regression assertions. The eight
  untracked paths are the expected R33 ledger, services, model, and test files.

This remains automated and source-level evidence. Owner validation is still
required for the packaged desktop walkthrough, real-provider Agent flows,
installed native runtime and model evidence, NVIDIA process VRAM, Linux and
Windows audio audibility, COSMIC/Wayland rendering, and the separate abrupt
Windows crash classification.
