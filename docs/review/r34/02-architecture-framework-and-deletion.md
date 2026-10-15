# 02. Architecture, framework capability and deletion

Source IDs refer to [01](01-current-state-and-evidence.md). All changes below
are **P** unless explicitly RF. Implementation packages are defined in [11](11-dependency-roadmap.md).

## Authority inventory

| Domain | Authority and representation now (RF) | R34 decision and bounded audit |
| --- | --- | --- |
| Settings | `ISettingsService`/`SettingsService`, `AppSettings`, one save flow, bootstrap/effective root distinction (S04) | Retain. Unsaved editor drafts, configured state and effective runtime state are necessary distinctions, not duplicate truth. Test stale Apply/save/restart rather than unifying them. |
| Model and runtime profiles | `ModelProfileService` and `RuntimeProfileService` edit different sections of settings (S05) | Retain. Model sampling/preferences are not managed-process launch configuration. Audit translations only where a fingerprint or persisted field disagrees. |
| Model inventory | `ModelInventoryService` owns bounded root/metadata caches; registered in Desktop at `App.axaml.cs:365` | Retain Services implementation. Desktop registration does not imply another inventory exists in Composition. Do not move registration or eagerly load it in Local API without a consumer. |
| Managed child processes | `ManagedRuntimeRegistry`, `ManagedRuntimeProcessFactory`, `ServerProcessManager`; separate owned Lab, Local API and voice children (S01/S11/S12) | Retain capability owners. Audit caller cancellation, late callbacks, restart and abandoned-child identity in S1/S4/L2. Multiple managers are legitimate when process identities differ. |
| Resource admission | `IResourceCoordinator`, consumer registry/adapters, expiring reservations and release receipts (S11) | Retain one admission authority per composed host. Reservations are coordination, not reserved physical VRAM. No new scheduler or eviction authority. Separate Local API process is not cluster-wide admission. |
| GPU Fit and telemetry | Analytical Core models; shared `IRuntimeTelemetrySource`; bounded process probes and live sampler (S13) | Retain prediction/observation separation. P1 measures polling overlap and cost before changing cadence. Unknown process VRAM cannot become device-total attribution. |
| Runtime evidence/capabilities | `LocalModelCapabilityService` help/props and identity cache; `RuntimeEvidenceEvaluator`; immutable experience records (S14/S31) | Retain advertised/effective/observed layers. Collapse only duplicate parsing proven to use identical semantics. No new capability cache. |
| Agent tasks/approvals | `AgentService`, prepared mutations, `AgentTaskCommandOwner`; JSON task truth, rebuildable SQLite index, durable receipts (S07-S10) | Retain. Locks are process-local; they do not earn Agent HTTP execution. Audit entry growth under D2; never delete locked semaphores opportunistically. |
| Workspace policy | Manifest/policy evaluator narrows file tools and command paths after containment | Retain. Approval authority never comes from model prose, lessons, scenario manifests or an OS sandbox. S4 tests filesystem races below string checks. |
| RAG and Recall | RAG current-generation pointer and bounded scan cache; Recall derived message/task index plus federated source searches (S15-S18) | Retain distinct user meanings. P2 may reuse a proven vector primitive, not merge stores or promise one universal index. |
| Persistent Memories | `MemoryStore` current assertions/revisions, embeddings and sources (S19) | Retain revision authority. Search caches/indexes are disposable projections; memory content/history is not. |
| Lab and Benchmarks | `LabExperimentService`, isolated runtime host/recipe runner versus `BenchmarkService`, shared runtime evidence | Retain distinct experiment and reusable-suite jobs. Instrument existing operations. No second performance database or automatic winner selection. |
| Startup/shutdown | `ApplicationLifecycleCoordinator`; Desktop registers VM/voice/warmup owners; subsystem participants initialise stores (S01-S03) | Retain coordinator and local owners. L2 audits cancellation/drain at actual allocation boundaries, not a second lifecycle framework. |
| Desktop/Local API | Shared registration method, different service providers/processes, Local API token auth and unmapped Agent routes (S22/S23) | Retain composition. Do not claim shared singletons cross process or expose Agent to resolve a cosmetic architecture diagram. |

Useful representations must survive simplification: requested/configured/
effective/observed settings; predictions/measurements; source records/derived
indexes; current revisions/history; pending proposals/execution receipts.
The governing rule means one authority **for a fact**, not one DTO for every
stage in its lifecycle.

## Framework and custom-infrastructure audit

Pinned framework is Avalonia 12.1.3, AvaloniaEdit 12.0.0, .NET 10 and
CommunityToolkit.Mvvm 8.3.2. F01-F04 in [10](10-technology-decisions.md) record
primary-source checks; a newer upstream release is not permission to upgrade.

| Candidate and exact implementation | Disposition | Compatibility and validation |
| --- | --- | --- |
| Desktop `.csproj:11` disables compiled bindings despite typed views | **Simplify incrementally**, D2 | Opt in per view/data template using `x:CompileBindings`; inventory ancestor, ItemsControl, nullable, dictionary and converter bindings. Compile a low-risk typed view first, retain explicit ReflectionBinding at justified dynamic boundaries. Check binding diagnostics and real row commands. No global switch or promised speed gain. |
| `Controls/OverlayToolTip.cs` (234 lines); installation in App and disabled built-in tooltip style | **Replace conditionally**, D3 | Upstream fix exists at pinned framework source, but our native overlap, text, timing, disabled controls, theme and DPI behaviour must match. Test Windows and COSMIC including XWayland/native Wayland identity. Keep workaround until both pass. |
| `Converters/WheelScrollHelper.cs` (83 lines), shell/page tunnel handlers | **Retain**, D3 audit only | Implements nested edge bubbling and horizontal movement, beyond merely disabling NumericUpDown spinning. Compare framework options only against that whole contract; preserve number values under wheel. Existing `WheelScrollHelperTests` plus pointer checks. |
| `Views/ModalWindowPlacement.cs` (54 lines), posted client-size/DPI placement | **Retain**, D3 audit only | `CenterOwner` alone does not establish equivalent owner timing, mixed DPI or working-area clamping. Test modal owner, focus, Escape, long content, 100/150/200% scaling and multi-monitor. |
| `Controls/MarkdownViewer.cs` (857 lines), Markdig/AvaloniaEdit | **Retain**, P1 | Async parsing, streaming scheduling, prefix block reuse, cross-block selection, clipboard and detach/reattach are mature behaviour. Measure parse/layout separately. A generic Markdown control is not an equivalent replacement. Extend existing Markdown lifecycle/control tests for measured changes. |
| `Views/WorkspaceEditorView.axaml.cs`, sole AvaloniaEdit host | **Retain** | Preserve selection, syntax, focus, Ctrl+S, revision conflicts and atomic owner writes. No WebView fallback or replacement editor. |
| `MainWindowViewModel.OnActivePanelChanged` | **Simplify**, D2 | Toolkit `NotifyPropertyChangedFor` can declare dependent scalar notifications. Preserve `Models.CancelHuggingFaceSelection`, cross-VM notifications, command registration and ordering-sensitive side effects. Counts/ordering tested through behaviour, not source-string equivalence. Attribute declarations may save little net text; do not sell them as a large deletion. |
| `AppFontService` and role resources versus Inter startup registration | **Simplify**, D1 | Defaults and overrides already exist. Trial removing `.WithInterFont()` and its package together. System-installed Inter remains selectable; a saved bundled-font URI needs honest fallback rather than settings loss. Measure package delta and verify fallback/code glyphs on Linux/Windows. |
| `Converters/BoolToTextConverter`, `UppercaseConverter`, `ProvenanceConverters`, `BenchmarkConverters`, `StatusPalette`; inline view converters | **Retain; simplify only proven equivalents**, D2 | Their labels, nulls, colours and domain states have meaning. Search XAML/static resources and code consumers before removing any converter. Native Boolean converters cannot replace domain status mapping. No unused converter is established by this pass. |
| Artwork/data-URI bitmap converters and `PetSprite` | **Retain** | Image ownership, bounded decode/cache and data-only optional pet are distinct. Native Image alone is not evidence of redundant validation. No animation or mascot expansion. |
| `MossEmptyState`, `MossIcon`, `DiffView`, `MessageControl`, `MicButton`, `IconBarCursor` | **Retain** | Reuse shared empty states, source controls and proven pointer region fix. Check accessibility/focus. The nav cursor gap defect and tooltip overlap defect are distinct. |
| `Styles/AppStyles.axaml`, confirmation/preview/info dialogs | **Retain; targeted simplify**, U1 | Audit semantic resources and existing Fluent templates, focus, overflow and owner placement. Consolidate identical setters only after matching theme/state behaviour; no theme framework, card redesign or new dialog service. |
| .NET primitives | **Retain platform facilities**, S1-S3 | Use existing `ArgumentList`, bounded Stream reads, cancellation, `AesGcm`, atomic writes and additive migrations. No package needed for the identified hardening. |

## Deletion register

Expected reductions are candidates, not measured results. Count the final diff
and RID package bytes in each implementation receipt. Every row is a decision,
including retained items; there is no deletion quota.

| ID / package | Location, why it exists | Existing replacement / proposed deletion | Security and behavioural gate | Expected reduction / rollback |
| --- | --- | --- | --- | --- |
| DEL-01 / D1 | `Program.cs:155`, Desktop Inter ref; template-era bundled font registration | Existing AppFontService/native font roles; remove registration and package together | Font override/fallback/native glyph tests, no new rendering dependency; update upgrade/notices docs after actual removal | One direct package plus hook; package bytes Unknown until RID publish. Restore both lines and notices if native gate fails. |
| DEL-02 / D1 | Desktop DI ref; host builds provider and resolves services | Composition already directly references same DI 9.0.4; remove redundant Desktop direct reference only if restore/build still resolves compile API | Keep explicit Composition ownership and one container; production graph resolution and shutdown tests | One direct ref, no dependency or container count reduction promised. Restore reference only. |
| DEL-03 / D1 | Desktop Tmds ref; historical Linux integration pin | Avalonia.FreeDesktop 12.1.3 depends on identical 0.94.1, no direct source use found | Restore graph must retain DBus; native Linux tray, accessibility and portal checks. Reassess deliberate pin rationale before removal | One direct ref, no native/runtime byte saving expected. Restore exact pin. |
| DEL-04 / D2 | S30 manual dependent notifications | Toolkit generated notifications; retain side-effect hook | All nav highlight/title/Agent-wait/Setup states plus model-inspection cancellation | About 18 manual calls replaced by declarative attributes; net line deletion may be near zero. Restore hook/field attributes together. |
| DEL-05 / D3 | OverlayToolTip 234 lines, installation and style opt-out | Built-in ToolTipService with upstream deferred-close fix | Whole tooltip/native/pointer regression matrix; do not delete IconBarCursor | Up to 234 implementation lines plus wiring, conditional. Restore helper, installation and service opt-out atomically. |
| DEL-06 / P2 | S16 collects all dense scores, sorts and builds a full score dictionary | Existing bounded top-k pattern in `HybridRetriever.CosineScan` if relevance/tie semantics match | Compare exact float32 top-k, scope, archived behaviour and fallback; no lexical-only pruning | O(N) score storage/sort can be bounded; bytes/lines Unknown until measurement. Keep old path for comparison, then remove it before ship; revert code if no benefit. |
| DEL-07 / D2 | S10 per-task/target dictionaries retain coordination entries | Investigate measured growth; reuse platform locks, no new general lock manager | Atomic acquire/release/waiter protocol and shared-target races; no `TryRemove` after release while waiters exist | Retained entries may reduce, code may increase. Defer if benefit is negligible; revert entire reclamation change, never authority checks. |
| DEL-08 / P1 | Inventory/capability/telemetry/RAG caches and periodic work | Existing bounded identity-aware caches | Retain absent a measured redundant query/poll; audit invalidation on restart, clear, model change and ingest | No deletion currently justified. A future exact duplicate needs its own equivalence/rollback receipt. |

Rejected deletions: patched SQLite pins; Voice/RAG ONNX references treated as
duplicate sessions; shared lifecycle/composition; MarkdownViewer; wheel/modal
helpers without native equivalence; JSON compatibility merely because old
records exist. Keep each healthy abstraction unless evidence proves redundancy.
