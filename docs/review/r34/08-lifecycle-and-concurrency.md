# 08. Lifecycle and concurrency

The source observations and line references below describe the released planning
baseline. Later reproductions, bounded repairs and native walkthrough evidence
are in [13](13-execution-and-evidence.md) and
[15](15-native-and-vault-evidence.md). The baseline hypotheses are not the current
repair status or proof of an unbounded memory leak.

## What source proves

**RF:** MainWindowViewModel holds persistent feature VMs and selects one through
`ActiveViewModel`; MainWindow's ContentControl uses feature data templates.
Services, Settings and several siblings install delegates that capture their
visual instance. Source does not establish whether navigation recreates or
reuses every view under every template path. A single overwritten delegate can
retain one obsolete view temporarily without producing unbounded growth.
**No view memory leak is confirmed by this planning pass.**

| Surface / source in `src/Hermaeus.Desktop/Views/` | Inspected ownership fact (RF) | Reproduction target (IN), L1 |
| --- | --- | --- |
| `SettingsView.axaml.cs:42` | Assigns Data child pickers/confirmations and Ui/setup callbacks on DataContextChanged. Appearance handler at 24 is static; removes/readds on the new Ui VM only | Replace A with B then change A.Ui; count stale appearance delivery separately from visual retention. Null DC, unload/reload same DC, nested child replacement |
| `ServicesView.axaml.cs:36/75/121` | Wires server collection/property events, per-server file picker, TTS/STT callbacks; Unloaded clears subscriptions/fields, constructor has no Loaded rewire hook | Reattach same instance/DC then add a server or navigate to Doctor target. Do callbacks/focus return exactly once? Track removed child VMs and surviving TTS/STT callbacks |
| `AgentView.axaml.cs:16` | Workspace picker and delete confirmation capture view; folder-open callback is a separate interaction | Recreate during pending review, attempt picker/confirm on active view only, preserve task/approval identity |
| `ModelManagementView.axaml.cs:22` | Organise/delete/companion/repository dialogs capture owner view | Navigate during inspection and confirmation, ensure stale request never opens on detached owner or retargets model |
| `LogsView.axaml.cs:16` | Clipboard callback captures view; folder opening has its own delegate | Context replacement and copy after revisit; no duplicate delivery |
| `DoctorView.axaml.cs:20` | Clipboard and confirmation delegates installed per DC | Revisit during async scan/install, active owner only; no repeated install/confirmation |
| `BenchmarkView.axaml.cs:19/30` | Per-view `_loadRequested` plus callbacks; VM `LoadAsync:157` shares in-flight task | Recreate while load is delayed, load fails, same view receives new VM. Preserve selection/history; do not infer duplicate loads are absent just from the view flag |
| `ActivityView.axaml.cs:14` | Clear confirmation installed per DC | Recreate/unload/reload and cancel confirmation, active view only |
| `MemoriesView.axaml.cs:11` | Delete confirmation assigned from DC handler | Revisit while query/revision action pending, correct current item/revision |
| `SetupWizardView.axaml.cs:15` | Three folder pickers capture view | Skip/finish/reopen, DC replacement, no callback to abandoned wizard |
| `SettingsMemorySectionView.axaml.cs:14` | Recall clear confirmation installed on persistent MemorySettings VM | Parent settings unload and nested section recreation; one confirmation, cancellation leaves index unchanged |
| `ChatView.axaml.cs:38/54` | Detach stops telemetry; DC replacement removes named scroll/mic handlers and several callbacks | Useful ownership reference, **not proof all callbacks detach on unload**. Preserve active streaming, input focus, scroll pin, attachments, clipboard and telemetry cancellation |
| `RagView.axaml.cs:34` | DC replacement unwires prior scroll callbacks and confirmation/picker delegates | Same caveat; navigate while Ask/ingest runs, reconnect rendering and correct source/citation context |
| `MainWindow.axaml.cs:36/106` | OnOpened installs window-owned callbacks and adds Palette PropertyChanged; no matching lifetime removal observed | Test actual hide/show, close, reopen/recreate and VM replacement. One application-window lifetime may legitimately retain its subscriptions; do not call that a leak |
| `WorkspaceEditorView.axaml.cs:125`, `Controls/MarkdownViewer.cs:252` | Explicit attach/detach handling; Markdown detach pauses rendering, Dispose ends ownership | Regression references for reconnect versus final disposal; keep correct owner-save, Markdown selection and async-generation invalidation |

## Reproducible L1 protocol before a repair

Use existing `AvaloniaTestHost`/Native Avalonia collection with isolated VMs and
controlled service boundaries. This exercises actual attachment/binding without
owner settings. Pure VM tests cannot prove ContentControl view ownership.

1. Construct the real shell/template path with instrumented callbacks and
   known weak references. Record visual creation, attach/detach, loaded/unloaded,
   DC A/null/B and callback assignments; do not keep test lists of strong views.
2. Exercise 20 navigation cycles across all surfaces, then repeat with
   deliberate same-instance unload/reload and recreated-instance/same-VM paths.
   Include nested child/server removal and Settings Ui replacement.
3. Invoke each presentation request with a fake picker/dialog boundary or
   controlled native owner. Assert exactly one active delivery, zero obsolete
   delivery and correct item/task/model context. Run delayed dispatcher work
   after navigation/replacement to expose stale updates.
4. Flush dispatcher work, close child windows, drop test-owned references, then
   use controlled GC and weak references to check obsolete views. A failed weak
   reference check needs a retained-root investigation; GC timing or a test
   closure is not product-leak proof. Distinguish latest detached view held by
   one delegate from accumulation across cycles.
5. Benchmark load: delayed/failing/successful service, concurrent startup/view
   loads, recreation and explicit refresh. Assert one in-flight operation,
   usable retry after failure and intentional Insights refresh, not a single
   lifetime load that prevents refresh.
6. Repeat important pointer/keyboard/dialog flows in real Windows and COSMIC
   packages. Native control tests are not pixels or full owner workflows.

## Repair contract if reproduction warrants it

Prefer small consistent attach/detach helpers inside each view, using existing
Avalonia lifecycle events. Track the previously wired VM and child VMs. Detach
from **that** owner, reattach on same-DC reload, and remove only delegates this
visual still owns. Delegate identity checks prevent an old view's late unload
from clearing the newer view's callback. Child replacement/removal must clean
the old child as well as the parent subscriptions. Test outstanding dialogs
and dispatcher posts after detach; do not close unrelated windows.

Appearance preview must have a clear lifetime owner. The current static handler
does not capture SettingsView; evaluate whether the persistent settings/host
already provides the right owner before moving it. Do not merely sprinkle null
assignments or weak subscriptions over callbacks and call the cause fixed.
No generic lifecycle framework, event bus, base-view hierarchy or changed VM
persistence is selected. Never dispose singleton feature VMs on navigation.

## Process/concurrency L2 matrix

Retain `ApplicationLifecycleCoordinator`, shutdown journal and capability owners.
Use explicit signals and existing `ApplicationLifecycleCoordinatorTests`,
`ShutdownDisposalTests`, `ServicesViewModelRestartGuardTests`,
`ServerProcessViewModelOrphanTests`, `OnnxNativeLifetimeTests`,
`AgentLifecycleRecoveryTests` and Lab ownership tests as seams. Extend only
the proven failure class, and keep tests sequential.

| Transition | Required observation |
| --- | --- |
| Start versus cancel/stop/restart | No late ready event resurrects stopped state; join actual allocation owner; new process has new PID/start/hash series |
| Two starts/admission requests | Fresh reservations and one owner per runtime id; Unknown headroom does not become zero |
| Agent command timeout/user cancellation/read failure | S1 termination and bounded output contract; explicit terminal reason and no unobserved reader task |
| Shutdown during inference/native session replacement | Correct session gate, cancellation request, bounded drain and honest incomplete result if native call cannot stop |
| Shutdown during RAG/Recall/warmup/watch | Background task stops or bounded failure is recorded; no store disposed while an owned writer silently continues |
| Lab failure/cancel/source change | Owned isolated child stops; original unchanged source restores, changed source stays stopped with explicit blocked restore |
| Restart handoff fails | No concurrent desktop data owner; retained prior state, bounded lock wait and recoverable failure |
| Crash then recovery | PID/start/hash checked, corrupt manifest remains Unknown, no reused/unrelated process killed; prepared Agent receipt reconciled without replay |
| Deadline exceeded or cancellation-ignoring owner | Deadline returns incomplete evidence, no false CleanExit marker. A bounded return does not prove the owner stopped |
| Clean normal close/tray Quit | No owned runtime/API/voice/Agent child survives; dispose stores and locks only after drain. Never use forced termination as clean shutdown proof |

Measure drain/callback/resource behaviour under [07](07-performance-baselines.md).
S1 can implement process safety without waiting for L1 GUI evidence. L1 view
tests can run while S2/S3 proceed. Final native acceptance is a gate after
implementation and automated verification, not a reason to leave them unfinished.
