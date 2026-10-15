# 01. Current state and evidence

## Baseline receipt

**VE, 2026-10-16:** `git rev-parse --show-toplevel` returned the Hermaeus
checkout at `<repository-root>`; branch was `r34/round`,
tracking `origin/r34/round`. HEAD was
`23cec510c86a65d71c0dbffeaebe31b33973c8f5`. Initial tracked and untracked status
was clean. The only remote was `origin`, fetch/push
`https://github.com/MortisDei/hermaeus.git`. No fetch or remote mutation was
needed to establish this local baseline. Git printed an inaccessible global
ignore-file warning in the restricted runner; repository status still returned.

The annotated tag object is `dbd921ade09f1a4a763ef06d38c39fc1fa159ae2`;
`git rev-parse 'v0.41.0-beta^{commit}'` resolves to HEAD. The merge subject is
`R33: Shared Lifecycle, Agent Authority & Runtime Evidence (#20)`.
`Directory.Build.props` defines `0.41.0-beta` and warnings-as-errors.
Read-only `gh release view v0.41.0-beta --repo MortisDei/hermaeus --json
tagName,targetCommitish,publishedAt,isPrerelease,url` confirmed a
[published prerelease](https://github.com/MortisDei/hermaeus/releases/tag/v0.41.0-beta),
published `2026-10-08T12:36:54Z`, target `main`. No release asset was downloaded
or revalidated in this pass.

There are **13 tracked `.csproj` files**, all targeting `net10.0`; see [03](03-dependencies-and-native-assets.md).
There is no root `ROADMAP.md` or `docs/roadmap.md`. The current inherited round
roadmap is [R33 document 11](../r33/11-dependency-roadmap.md), interpreted with
the later R33 closeout and release receipt, not its historical progress table.
R34 document 11 becomes the planning roadmap for this round.

## Inherited guarantees and their evidence

| Contract to preserve | Current source | Historical evidence, not rerun here |
| --- | --- | --- |
| Central non-UI DI, shared startup/recovery and bounded shutdown | S01-S03 | [R33 authority](../r33/02-application-authority-and-lifecycle.md), [closeout](../r33/14-owner-dogfood-closeout.md) |
| Prepared mutations, exact approval identity, preimages, readback and crash reconciliation | S07-S10 | [R33 mutations](../r33/03-agent-workspace-and-mutations.md), closeout |
| Settings are the persistence authority; Apply/restart are explicit | S04-S06 | R33 closeout and [features](../../features.md) |
| Requested/resolved/launched/effective/telemetry evidence stays separate | S11-S14 | [R33 Lab/Benchmark audit](../r33/13-lab-benchmark-authority-audit.md) |
| Atomic RAG publication and revision-bound citations; Memory revisions remain separate from Recall | S15-S19 | [RAG](../../rag.md), [Recall](../../recall.md), [features](../../features.md) |
| Native editor owner saves differ from Agent mutations | S20 | R33 editor decision, as superseded by closeout |
| Restore containment covers final/staging/rollback paths and expansion budgets | S21 | [R33 release readiness](../r33/16-release-readiness.md) |
| Process telemetry preserves Unknown and native lifetimes | S13 | [R33 native investigation](../r33/15-windows-native-crash-investigation.md) |
| Local API is separate-process, authenticated and has no Agent execution routes | S22-S23 | [Agent API](../../agent-api.md), [Local API](../../local-api.md) |

The R33 release receipt records Windows Debug/Release zero-warning builds,
2,871 passing Windows Debug tests, 66.69% coverage and a preceding Linux run
with 2,853 passes and 18 expected platform skips. It records native COSMIC
tray/restore/Quit checks and owner Windows Chat/tray/RAG-navigation smoke.
The Windows smoke package predates the final restore-only follow-up. Packaged
minimise-to-tray and disable/re-enable preference checks were not exercised.
These are **historical VE/OO**, not R34 platform passes or current test counts.
The native investigation proves an NVML ABI overwrite mechanism and repairs;
it explicitly does not prove attribution of every historical crash.

## Current findings and confidence

| Finding | Evidence class | Disposition |
| --- | --- | --- |
| `RunCommandAsync` and `InspectGitDiffAsync` only tree-kill in timeout catch branches excluding caller cancellation; output is read in full before a tail is selected | RF S09; actual surviving child not reproduced | P0 S1: reproduce process lifetime and memory bounds, then repair the proven class |
| Web ingest uses headers-only completion then unbounded `ReadAsByteArrayAsync`; local warning follows full text/PDF loading | RF S15 | P0 S2: enforce real read/extraction budgets |
| Fallback vault writes `v2:` AES-CBC without an authentication tag | RF S24; accepted security-roadmap gap | P1 S3: versioned AEAD migration, old values readable |
| `--host` conflicts already fail in the canonical launch builder | RF S12 | Correct warning-only documentation; audit aliases/environment separately under S4 |
| Recall message/task semantic search scans eligible BLOBs and sorts dense scores | RF S16 | P1/P2 measurement before storage or algorithm changes |
| Callbacks reference visual instances; Services unwires on Unloaded without a Loaded hook | RF S25-S28 | IN L1: retention/reattach behaviour needs reproduction, not a confirmed leak |
| Settings appearance handler is static; removing it from the new Ui VM does not remove it from a previous Ui VM | RF S26 | IN L1: possible stale delivery, not proof the handler retains a view |
| Task/target semaphore dictionaries have no removal path | RF S10 | IN D2: retained per-identity coordination entries; measure growth and safe reclamation, never naïvely remove live locks |
| Inter is registered by `.WithInterFont()` despite native typography defaults | RF S29 | D1 candidate: remove hook and package together only after font/fallback tests |
| Desktop DBus direct reference has no C#/AXAML usage; Avalonia.FreeDesktop resolves the same version transitively | RF + local assets [03](03-dependencies-and-native-assets.md) | D1 candidate: remove redundant direct pin with Linux native acceptance; no runtime-library size saving assumed |
| Toolkit generated dependent notifications can replace navigation notification boilerplate | RF S30 + UF F02 in [10](10-technology-decisions.md) | D2; preserve navigation cancellation side effects |
| Upstream tooltip issue is closed and the deferred-close logic exists at Avalonia 12.1.3 | UF F03 | D3 bounded native comparison; no removal from source inspection alone |

## Source anchor index

Anchors refer to the unchanged R33 source baseline. Follow symbols when later
implementation shifts lines. Paths are repository-relative through the links.

| ID | Location and symbol |
| --- | --- |
| S01 | [HermaeusServiceRegistration.cs:26](../../../src/Hermaeus.Composition/HermaeusServiceRegistration.cs#L26), `AddHermaeusCoreServices` |
| S02 | [ApplicationLifecycleCoordinator.cs:42](../../../src/Hermaeus.Services/ApplicationLifecycleCoordinator.cs#L42), `StartAsync`, `RegisterShutdownOwner`, `ShutdownCoreAsync` |
| S03 | [App.axaml.cs:81](../../../src/Hermaeus.Desktop/App.axaml.cs#L81), host shutdown owners; [LocalApi Program.cs:11](../../../src/Hermaeus.LocalApi/Program.cs#L11), separate host composition |
| S04 | [SettingsService.cs:113](../../../src/Hermaeus.Services/SettingsService.cs#L113), `ResolveDataRoot`; `SaveAsync` at 271 |
| S05 | [RuntimeProfileService.cs:21](../../../src/Hermaeus.Services/RuntimeProfileService.cs#L21), `Profiles`; [ModelProfileService.cs:15](../../../src/Hermaeus.Services/ModelProfileService.cs#L15), model preferences |
| S06 | [RecommendationApplicationService.cs](../../../src/Hermaeus.Services/RecommendationApplicationService.cs), Apply/reconciliation authority |
| S07 | [AgentMutationPreparation.cs:33](../../../src/Hermaeus.Agent/Services/AgentMutationPreparation.cs#L33), preparation and policy fingerprint |
| S08 | [AgentPatchReviewService.cs:85](../../../src/Hermaeus.Agent/Services/AgentPatchReviewService.cs#L85), prepared queue; `PreparePatchAsync` at 268 |
| S09 | [AgentToolExecutor.cs:367](../../../src/Hermaeus.Agent/Services/AgentToolExecutor.cs#L367), `RunCommandAsync`; `InspectGitDiffAsync` at 425 |
| S10 | [AgentTaskCommandOwner.cs:10](../../../src/Hermaeus.Agent/Services/AgentTaskCommandOwner.cs#L10), in-process task/target locks |
| S11 | [ResourceCoordinator.cs:79](../../../src/Hermaeus.Services/ResourceCoordinator.cs#L79), `AcquireAsync`; [ManagedRuntimeRegistry.cs:22](../../../src/Hermaeus.Services/ManagedRuntimeRegistry.cs#L22), manager ownership |
| S12 | [ServerProcessManager.cs:757](../../../src/Hermaeus.Services/ProcessManagement/ServerProcessManager.cs#L757), `BuildLaunchArguments`; `CanonicalizeCoreExtraArguments` at 1057 |
| S13 | [ProcessRuntimeTelemetrySource.cs:11](../../../src/Hermaeus.Services/ProcessRuntimeTelemetrySource.cs#L11), probe bounds; [LiveModelTelemetrySampler.cs:7](../../../src/Hermaeus.Services/LiveModelTelemetrySampler.cs#L7), session ownership |
| S14 | [RuntimeEvidenceEvaluator.cs](../../../src/Hermaeus.Services/RuntimeEvidenceEvaluator.cs), fail-closed envelope evaluation |
| S15 | [RagPipeline.cs:535](../../../src/Hermaeus.Rag/Pipeline/RagPipeline.cs#L535), `FetchWebDocumentAsync`; `LoadLocalDocumentAsync` at 858; [PdfTextExtractor.cs:13](../../../src/Hermaeus.Rag/Pipeline/PdfTextExtractor.cs#L13) |
| S16 | [RecallIndexStore.cs:410](../../../src/Hermaeus.Services/Recall/RecallIndexStore.cs#L410), `SearchAsync`; `FromBlob` at 840; cosine at 847 |
| S17 | [RagQueryService.cs:43](../../../src/Hermaeus.Rag/RagQueryService.cs#L43), eight-dataset/128 MiB cache and candidate hydration |
| S18 | [RagScanIndex.cs:15](../../../src/Hermaeus.Rag/Models/RagScanIndex.cs#L15), contiguous vectors; [HybridRetriever.cs:52](../../../src/Hermaeus.Rag/Retrieval/HybridRetriever.cs#L52), `CosineScan` |
| S19 | [MemoryStore.cs](../../../src/Hermaeus.Services/MemoryStore.cs), revision, search and embedding authority |
| S20 | [WorkspaceEditorView.axaml.cs:125](../../../src/Hermaeus.Desktop/Views/WorkspaceEditorView.axaml.cs#L125), native editor attachment/detachment |
| S21 | [BackupService.cs](../../../src/Hermaeus.Services/BackupService.cs), `RestoreAsync` and canonical restore path resolver |
| S22 | [LocalApiTokenAuth.cs:37](../../../src/Hermaeus.LocalApi/LocalApiTokenAuth.cs#L37), unauthenticated health exception then token checks |
| S23 | [LocalApiEndpoints.cs:36](../../../src/Hermaeus.LocalApi/LocalApiEndpoints.cs#L36), actual route map; [LocalApiModels.cs:66](../../../src/Hermaeus.LocalApi/LocalApiModels.cs#L66), execution disabled |
| S24 | [SecretStore.cs:317](../../../src/Hermaeus.Services/SecretStore.cs#L317), fallback encryption and legacy decryption |
| S25 | [ServicesView.axaml.cs:36](../../../src/Hermaeus.Desktop/Views/ServicesView.axaml.cs#L36), wiring; `OnUnloaded` at 75 |
| S26 | [SettingsView.axaml.cs:24](../../../src/Hermaeus.Desktop/Views/SettingsView.axaml.cs#L24), static appearance handler; callback wiring at 42 |
| S27 | [BenchmarkView.axaml.cs:19](../../../src/Hermaeus.Desktop/Views/BenchmarkView.axaml.cs#L19), view load; [BenchmarkViewModel.cs:157](../../../src/Hermaeus.ViewModels/BenchmarkViewModel.cs#L157), in-flight load reuse |
| S28 | [MainWindow.axaml.cs:36](../../../src/Hermaeus.Desktop/Views/MainWindow.axaml.cs#L36), window callbacks and Palette subscription |
| S29 | [Program.cs:155](../../../src/Hermaeus.Desktop/Program.cs#L155), Inter registration; [AppFontService.cs:15](../../../src/Hermaeus.Desktop/AppFontService.cs#L15), native role defaults |
| S30 | [MainWindowViewModel.cs:1021](../../../src/Hermaeus.ViewModels/MainWindowViewModel.cs#L1021), `OnActivePanelChanged`; persistent feature VMs and `ActiveViewModel` near 30/90 |
| S31 | [LocalModelCapabilityService.cs:58](../../../src/Hermaeus.Services/LocalModelCapabilityService.cs#L58), help/props capability evidence and cache |
| S32 | [LlamaServerSetupService.cs:62](../../../src/Hermaeus.Services/LlamaServerSetupService.cs#L62), pinned b10034; b-number release parsing near 637/688 |
| S33 | [DoctorService.cs:24](../../../src/Hermaeus.Services/DoctorService.cs#L24), pinned Qwen embedding artifact |

## Documentation review and bounded reconciliation

Read: AGENTS, the review/security/storage/build skills, R33 README/roadmap,
authority/mutation/runtime/evidence/closeout/native-crash/release documents,
CHANGELOG, features, Agent, RAG, Recall, Benchmarks, Lab, security review and
roadmap, Avalonia playbook, testing, branding/Moss, packaging, PR workflow,
documentation index and deferred ledger. Historical statements stay historical.

This planning change also corrects current prose where source proves drift:
second-launch activation, prepared versus legacy patch preimages, supported
ingest formats, core host-argument rejection, upstream tooltip closure, and
third-party framework/editor notices. No runtime behaviour changes. Packaging
notice-file delivery, residual flag aliases and broad privacy wording remain
explicit audit work, not retrospectively passed guarantees.

No R34 benchmark, hostile fixture, native view-retention test, build, full
suite, coverage or package was run. Planning-only validation is in [12](12-planning-validation-and-release-gates.md).
