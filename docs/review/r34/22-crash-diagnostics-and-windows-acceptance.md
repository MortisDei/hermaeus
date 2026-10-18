# 22. A06 diagnostics and continued Windows acceptance

2026-10-18, Australia. Continues [21](21-independent-branch-audit.md).
The starting checkout was clean on `r34/round`. Its implementation corresponds
to the rewritten audit commit `506a71b`; historical commit references in this
pack were updated after the authorised documentation privacy rewrite.
No release readiness, native COSMIC acceptance or production vault v3
activation is claimed.

## A06: capture verified, original abort not reproduced

The original failed Linux run remains failed. Twelve further sequential full
suites used the same isolated source and native renderer, Xvfb, `--blame`, and
process-scoped runtime diagnostics:

```text
DOTNET_DbgEnableMiniDump=1
DOTNET_DbgMiniDumpType=1
DOTNET_EnableCrashReport=1
DOTNET_CreateDumpDiagnostics=1
DOTNET_DbgMiniDumpName=<private-evidence-root>/testhost.%p.dmp
```

Each completed with 3,169 passed and 42 Windows-only skips, zero failures.
The twelve TRX files independently report Completed, with identical exact
skip sets. No application dump was produced. Repeating the suite is additional
stability evidence, not twelve new kinds of coverage or proof of a repair.

Three further full suites used `MALLOC_PERTURB_=165` and
`GLIBC_TUNABLES=glibc.malloc.tcache_count=0`, with the same dump/report capture
and `--blame`. Each again completed with 3,169 passed and 42 skips. These change
allocator behaviour to seek an exposed corruption, not to close default-runtime
acceptance. Their receipts are in `a06-allocator-perturbation/`.

A separate minimal .NET process deliberately called `Environment.FailFast`
without loading Hermaeus or owner data. It exited on SIGABRT and produced a
7,471,104-byte minidump and a 29,213-byte crash-report JSON containing the
FailFast stack. This verifies that dump capture actually works in this WSL
environment; these synthetic files are not an A06 crash dump.

The registered WSL distribution is named `Ubuntu-24.04`, but the observed
distribution reports Ubuntu 26.04.1 LTS, kernel
`6.6.87.2-microsoft-standard-WSL2`, .NET runtime 10.0.12. The earlier registration
name must not be used as proof of the installed release. Test binaries include
Linux x64 SkiaSharp, HarfBuzzSharp, SQLite and ONNX native assets; their hashes
were inspected, which does not prove all were loaded in the failing process.
No native dump analyser was available on PATH.

The Linux `stat`/`statx` buffer layouts, Windows-only NVML guard, and shared
Avalonia test-runtime construction/disposal were inspected as possible native
boundaries. No causal instruction or reproducible corrupting call was
established. No production allocator, renderer, runtime pin, test assertion or
dependency was changed for A06. Its release-blocking disposition remains open.

Private diagnostic receipts remain under the existing Linux audit root in
`a06-diagnostics-20261018/`, including console logs, TRX hashes and
`summary.json`; the capture self-test is in `a06-dump-selftest/`.
Raw dumps and TRX files are not committed or published.

## Exact-package Windows observations

The original candidate was built with `build.ps1 -Runtime win-x64` from an
isolated `git archive` of the starting commit. The retained source TAR SHA256
is `c64ddabc89ac5e50801fee59f50c46bf842b83d373b72e05cccb244123e3a934`.
This private archive predates the documentation privacy rewrite; its hash is
the actual retained artefact, not a new archive of the rewritten Git tree.
The ZIP SHA256 is
`15f007ca274377dfbe4a9b9697e2ff49d47dad18533cfc553880784ec64b15b7`;
its sidecar matched and the package contains no PDBs. It still identifies as
the development `0.41.0-beta`, not a new release. Prior candidate/control
packages were not overwritten.

Native discovery selected the actual packaged desktop child after launcher
handoff. Windows reported version `10.0.26300`, build 26300; its product-name
property returned Windows 10 Home. That property alone does not establish the
Windows 11 acceptance label. The configured System theme rendered dark.
The observed window capture was 1282 by 852 pixels. Per-monitor DPI, actual
renderer/backend and the full font/scaling matrix were not established.

| Observed action | Evidence and limit |
| --- | --- |
| Chat startup | Real pixels and accessibility state; configured local Chat and Embeddings processes started. No Chat message was sent |
| Media navigation | Readable provider/privacy/cancellation guidance, empty outputs and disabled export; no backend generation submitted |
| Workflow picker | Owned Windows file dialog opened with JSON filter; Escape dismissed it and Media controls re-enabled. No workflow was imported |
| Media draft across navigation | Synthetic unsent prompt survived Media to Agent to Lab, palette lookup, and return to Media |
| Agent and Lab | Real empty-task and ready-to-start/effective-evidence states inspected. No task, approval, experiment or Apply action executed |
| Recall palette | Ctrl+K opened and focused search; plain `media` returned the navigation command and historical hits. Pointer selection returned to Media. The document source explicitly reported an incomplete timeout |
| Keyboard limits | Direct Return in the query did not invoke a result. In the resumed corrected package, Tab moved through Close to the first command and Return opened Media. Arrow traversal and focus restoration are not verified |
| Settings | Actual existing preferences page inspected without editing a control or deliberately saving |
| Resize attempt | Pointer drag and native menu attempt did not change the observed 1282 by 852 dimensions; minimum-size acceptance remains unverified |
| Clean exit | Owner selected tray Quit because automation exposed no tray target. Lifecycle journal reported CleanExit true, and desktop plus both observed owned server PIDs had exited |

The before/after settings file hashes differ. Its last-write time matches
startup, before Settings navigation. The captured theme, font selector, font
size and tray preferences were compared and remained equal. A full pre-launch
settings payload was not retained, so this receipt cannot establish byte-equal
settings preservation or identify every automatically changed field. No manual
settings restoration was attempted.

Captured pixels contain existing owner context and are not public screenshots.
No provider cost, model generation, RAG mutation, Agent execution, credential
change or privacy-setting change was initiated by the walkthrough. Startup
performed its existing managed-runtime and embedding warm-up workflow.

## A07. Transparent native palette and sibling surfaces

**Severity:** Medium, bounded R34 visual correctness defect, separate from A06.

The actual palette over Lab showed underlying labels through the search/results
surface. Its `SolidBackgroundFillColorBaseBrush` reference does not resolve in
the installed Fluent theme. Native regression tests on the unmodified XAML
failed in both light and dark themes because the palette Border background
was null. This proves a missing surface, not a GPU crash mechanism.

All seven uses were audited: the palette, setup root, four Doctor installation
overlays and the Services error banner. They now use Fluent's existing opaque
`SystemRegionBrush`, without a compatibility dictionary, new colour policy or
dependency. Native tests instantiate the real MainWindow, setup and four Doctor
surfaces, checking brush alpha and opacity under both theme variants.
The focused selection passed seven cases, including existing tray and Media
native lifecycle tests. No install/download was needed to exercise the Doctor
overlays in the test host.

A separate corrected Windows package built successfully, ZIP SHA256
`b80d2edda1b637238d6f449159eaf70bcd8cc284f6142886c1503b37ae4600de`,
with no PDBs. Its first desktop re-check was stopped by the owner's physical
Escape through Computer Use; input stopped at that point. After the owner
explicitly requested continuation, fresh native discovery selected the same
corrected package. Lab, Ctrl+K and plain `media` repeated the original state:
both empty and populated palette surfaces were visibly opaque, with no Lab
text showing through the results. Search received focus; Tab moved to Close,
then the Media command, and Return opened Media. The previous session's unsent
synthetic Media prompt was absent after restart.

For the A02 shutdown path, the workflow picker was opened and left pending.
The owner selected tray Quit. The matching lifecycle journal then reported
CleanExit true; the runtime log reported clean shutdown with no timeout and
all three registered owners complete. The observed desktop and both of its
managed server PIDs had exited. The desktop-view-model drain logged 195 ms.
No process was force-killed by the acceptance check. This is one real Windows
import-picker shutdown receipt, not native export-picker or every-descendant
containment proof; the cancellation-resistant picker delegates remain covered
separately by the automated regressions.

This establishes corrected-package dark-theme pixels and the observed keyboard
route. Light-theme packaged pixels, fonts, scaling and the complete Windows and
COSMIC matrices in [09](09-moss-native-acceptance.md) remain open; it is not a
general owner acceptance claim. The user-guide workflow is unchanged; features,
Recall reference and changelog describe the presentation correction.

## Automated verification of the correction

| Receipt | Result |
| --- | --- |
| Windows Debug and Release solution builds | Zero warnings/errors, 11.74s and 21.18s |
| `surface-red.trx` | Both theme cases failed on the original null palette brush |
| `surface-green.trx` | Seven focused native cases passed |
| `windows-full.trx` | 3,213 passed, zero failed/skipped, 3m55s |
| Corrected Linux Debug build | Zero warnings/errors, 23.01s |
| First corrected Linux suite | 3,169 passed, two release-notes cases failed, 42 skipped. The archive's CRLF shell script made Bash reject `pipefail` with a trailing carriage return; retained as a failed run |
| Linux `results-corrected/results.trx` | 3,171 passed, zero failed, the same 42 Windows-only skips, 1m14s. Includes both real surface-theme regressions |
| Canonical `pwsh ./scripts/coverage.ps1` | 3,213 passed, zero failed/skipped, 6m37s; 73.45% line coverage, 62,027/84,443, above the 60% floor. Temporary report cleanup and no remaining Windows testhost verified |

The corrected Linux snapshot TAR SHA256 is
`54140f3f41c48139e6f2a949dae4b1678519cf008e54d79c2b9fc246bc84b3f6`.
All 967 extracted source files matched the actual archive members. Against the
Windows source tree, 738 were byte-equal and 229 differed only in UTF-8 text
line endings; no other source differences were found. Eight isolated shell
scripts were then restored to byte-equal Git LF content, verified against
their blob hashes. No repository shell script, Git policy, assertion or parser
implementation was changed to make the Linux suite pass. Logs and both Linux
TRX files remain under the private corrected-snapshot root.

## Documentation privacy

Three R34 city-level timezone labels and an inherited R33 archive label were
reduced to country level. One Windows profile path was replaced with
`<repository-root>`. The owner explicitly authorised rewriting and pushing the
R34 branch. All 31 R34 commits were rewritten and checked for the known
identifiers across their review-document trees. Documentary commit references
were remapped to preserve the audit's usability. Per-commit tree comparisons
confirmed that only review documentation changed; code and commit metadata
were preserved. No backup ref was created.

The original `main` and release/tag refs are outside this R34 rewrite and were
left unchanged. The inherited R33 label remains in that older history. A branch
force-push does not guarantee removal from other refs, clones, reflogs or hosting
caches. No broader history purge or deletion was performed.

`AGENTS.md` now explicitly prohibits owner PII in published material and
requires review of staged content and history before publishing. The requested
cross-task reminder was also written as a private memory update note. Known
location/profile and machine-name strings were searched across current tracked
content. No raw diagnostic output or owner screenshot was added to the repository.
