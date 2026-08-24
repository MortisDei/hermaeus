# 07. R31 roadmap

Branch: `r31/round` from `fd63b3e` on current `main`. One round PR, unless the
owner explicitly changes that workflow. Do not push after every commit. Push
only a reviewed, meaningful batch worth running through public CI.

No version/tag/release is assigned by this planning pass. The owner controls
push, PR merge, tags, releases, and repository settings.

## 7.1 Dependency spine

```text
Evidence taxonomy + raw-preserving normalized outcomes
                         |
                         v
       Experience store + runtime/config identities
                         |
             +-----------+------------+
             |                        |
             v                        v
  Analytical GPU Fit           Project/Agent reuse
  + direct observation         with explicit review
             |
             v
      Lab protocol + correctness
             |
     +-------+--------+
     |                |
     v                v
 measured tuning   live telemetry
 and speculation   and health policy
```

No batch may invert this. In particular, do not build a settings sweeper before
experience identity/correctness exists, or an Agent reuse path before outcomes
and evidence origins are reliable.

## 7.2 Bounded implementation batches

| Batch | Deliverable | Depends on | Classification | Landed |
| ---: | --- | --- | --- | --- |
| 0 | Re-verify current selected Linux/Windows runtime facts, re-run coverage baseline, freeze exact accepted schemas/terms from docs 01-06 | planning pack | mandatory foundation | Yes (`evidence/r31-batch-0.md`) |
| 1 | Five evidence origins, compatibility converter, normalized outcome records and deterministic normalizers for every built-in/MCP/approval path, raw evidence preserved | 0 | mandatory spine | Yes |
| 2 | `experience.db`, typed experience codecs/query, provenance, correction/removal/export, Lab Evidence inspection tab | 1 | mandatory spine | Yes |
| 3 | Runtime capability registry, v2 runtime/model/hardware/config identities, capability-cache migration, stricter model-specific MTP evidence | 1 | mandatory spine | Yes |
| 4 | Structured GPU Fit breakdown, telemetry source, prediction/observation experience and compatible-discrepancy display | 2, 3 | mandatory spine | No |
| 5 | Lab navigation/shell, isolated temporary runtime lifecycle, immutable definition/run/observation/comparison, correctness/equivalence core, explicit Apply review | 2, 3, 4 | mandatory spine | No |
| 6 | Engine-profile recipes plus context/KV/Flash/CPU-MoE experiments; prediction-versus-observation comparison | 5 | mandatory measured optimization | No |
| 7 | General external draft and EAGLE-3 adapters, speculative parameter recipes, acceptance/TTFT/memory/correctness/equivalence; unsupported mechanisms stay Unknown | 5, 6 | mandatory conditional runtime feature | No |
| 8 | Prompt/shared-prefix evidence adapter and optional build-scoped diagnostics; direct counters only when observed | 5 | independently shippable, evidence-gated | No |
| 9 | Project State persistence/editor/proposals/context receipt | 1, 2 | mandatory independent | No |
| 10 | Per-subtask model selection through approved plan, task/transcript/report/synthesis identity and no-fallback behavior | 1, 3 | mandatory independent | No |
| 11 | Agent Local API policy/contracts and single-owner decision; implement safe scoped endpoints only if the ownership gate is satisfied | 1, 9, 10 | mandatory design, conditional execution | No |
| 12 | Live telemetry pop-out and deterministic deduplicated health conditions using the shared telemetry/experience foundations | 4 | mandatory independent | No |
| 13 | Chat scroll regression seam/tests and audio-feedback service/settings/assets/safe playback arbitration | none for scroll; telemetry notification service may be reused for cue events | mandatory independent | No |
| 14 | Targeted measured tests/hardening from the refreshed coverage report; docs/features/user guide/workflow docs/changelog synchronization | all changed batches | mandatory close-out | No |
| 15 | Dated upstream/watch audit: update channels, GLM MTP, reconfiguration, public speculative API, DFlash, MoE prefetch/streaming, reconstructable KV | 3, 5 | mandatory research deliverable | No |
| 16 | Full Windows/Linux automated gates, Linux/COSMIC batch verification, Windows audio/package-sensitive verification, public diff/security/PII audit | all | mandatory release gate | No |

Batch 0 is not permission to redesign the pack casually. It resolves facts that
can drift between this planning commit and implementation, especially installed
runtime help/version, upstream channel state, exact metrics, and coverage.

## 7.3 Commit and review boundaries

- Planning pack is one local documentation commit.
- Batches 1-5 each deserve at least one coherent commit and a local diff review.
  Split migrations, core contract, UI, and docs only when each commit remains
  buildable and truthful.
- Batches 6-8 split by experiment family. Do not mix an EAGLE adapter, KV sweep,
  and prefix parser in one commit because they happen to share Lab.
- Batches 9-11 split Project State, subtask model identity, API contracts, and
  any API implementation. The API contract must be reviewable before HTTP
  execution exists.
- Batches 12-13 split telemetry, health policy, scroll regression, audio policy,
  and playback hardening where useful.
- Do not push each small commit. Before a push-sized batch, run its focused
  tests, solution build, full suite at the boundaries below, docs review,
  secret/PII/local-path scan, and Linux-verification checklist review.

## 7.4 Automated test budget

Expected new or materially expanded tests: **170-235**, chosen for behavior and
risk rather than a percentage.

| Area | Expected tests |
| --- | ---: |
| Evidence origins, compatibility, normalized outcomes, safety pins | 20-25 |
| Experience store, migration, correction/removal, query/export/UI | 25-30 |
| Capability registry and v2 fingerprints | 15-20 |
| GPU Fit breakdown, telemetry source, empirical comparison | 25-30 |
| Lab lifecycle/definition/observation/comparison/Apply | 20-25 |
| Lab recipe families, speculation, prefix, KV/MoE, correctness | 35-45 |
| Project State | 15-20 |
| Subtask model selection | 15-20 |
| Agent Local API policy/contracts and optional endpoints | 20-30 |
| Live telemetry, notifications, scroll, audio | 30-40 |
| Targeted measured quality beyond feature tests | 20-30 |
| Research/registry/doc guards | 5-10 |

The ranges overlap where one test protects several contracts. Do not force the
sum by writing redundant tests. Re-estimate after Batch 0's current coverage
measurement.

Focused tests run with project/class filters while implementing. Full sequential
suite and zero-warning solution build run after Batches 2, 5, 8, 11, 13, and
every push-sized batch. All results go outside the checkout:

```powershell
dotnet build Hermaeus.sln
dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj `
  --results-directory "$env:TEMP\hermaeus-r31-tests"
```

Run `pwsh ./scripts/coverage.ps1` after Batch 14 with its output outside the
checkout according to the script's contract. Keep tests sequential. Register
new harness-style methods. Never use an early return to fake a platform pass.

## 7.5 Linux/COSMIC live-verification schedule

Substantial batches are not finished until suitable for these checks. Record
runtime/model/config identities and whether each result is automated, live, or
unverified.

| Boundary | Required live evidence |
| --- | --- |
| After 2 | Agent outcome matrix and Evidence inspect/correct/remove/restart/export privacy from doc 01 |
| After 4 | Two GPU Fit configurations against observed RAM/VRAM evidence; explicit Unknown if per-process VRAM is unavailable |
| After 5 | Lab baseline/cancel/restart cleanup on isolated loopback process while ordinary Chat remains usable |
| After 6 | context/KV/Flash or CPU-MoE comparison with memory, speed, and correctness |
| After 7 | one runtime-proven speculative pair/mechanism, or a documented Unknown gate when assets/runtime are unavailable |
| After 9 | Project State proposal/edit/accept/restart and bounded Chat/Agent context |
| After 10 | two-model approved orchestration and stopped/missing-model no-fallback case |
| After 11 | scoped Agent client reaches desktop approval boundary and cannot approve it, only if endpoints ship |
| After 12 | telemetry left open during Chat plus one synthetic health threshold/recovery with no spam |
| After 13 | long-stream scroll anchoring and every audio cue/mute/TTS arbitration on Linux/COSMIC |
| Close-out | clean installed/package launch, ordinary Chat/Agent/RAG/Models/Services/Benchmarks/Lab smoke, no orphan processes or private artifacts |

Windows remains supported. Automated CI must stay green. Live Windows is
specifically required for audio playback hardening, packaged native/runtime
behavior affected by a batch, and any Windows telemetry source. Unrun variants
are reported as unverified, not implied by the Linux result.

## 7.6 Runtime/manual gates

Some acceptance cannot be honestly automated without a model/runtime/hardware:

- per-process VRAM and backend-specific memory accounting;
- external draft/EAGLE/MTP/DFlash actual engagement and benefit;
- deterministic-equivalence behavior of a specific runtime build;
- prompt-reuse direct counters/diagnostics;
- MoE cache/prefetch behavior;
- semantic update-channel asset stability;
- audio audibility/coexistence;
- COSMIC pop-out, scrolling, and notification feel.

The feature remains Unknown, unavailable, conditional, or research when the
gate cannot run. Do not weaken acceptance into "the control appeared" or "the
model loaded."

## 7.7 Mandatory, independently shippable, and research

### Minimum mandatory release spine

Batches 1-6. Without them, the round has not achieved its title. Lab may have a
smaller first recipe set, but outcome, evidence, identity, experience,
analytical/observed GPU Fit, correctness, and explicit Apply all ship together.

### Mandatory independent deliverables

Batches 9, 10, 12, 13, and 14. They can be implemented and reviewed separately
after their dependencies. Scope pressure must be handled through the explicit
descope order, not by claiming they are part of Lab and omitting them.

### Mandatory design with evidence-gated implementation

- Batch 7 mechanisms appear only on a proving runtime/asset pair.
- Batch 8 direct reused-token counts appear only with a trustworthy counter.
- Batch 11 always ships the approval/ownership/API contract; execution routes
  ship only with single-owner mutation and explicit per-token scope.
- Expert caching, GLM MTP, low-bit future KV, and update-channel UI follow their
  observed capability/upstream gates.

### Research/watch

Batch 15. Reconfiguration, public speculative API, DFlash production use, MoE
prefetch/streaming, reconstructable KV, and a Stable semantic channel remain
research unless the documented gate becomes true. A dated honest Unknown/watch
result satisfies the R31 contract; invented integration does not.

## 7.8 Descope order

If R31 exceeds a sensible round, move rows to the next round in this order,
updating this pack and `deferred.md` explicitly. Nothing disappears.

1. Production DFlash adapter if its research gate happens to mature; keep
   registry representation and dated findings.
2. MoE selective expert-cache recipe; keep CPU-MoE placement experiment and
   research/watch findings.
3. Optional build-scoped prompt-diff diagnostics; keep controlled timing-effect
   experiment and never fabricate reuse counts.
4. Agent Local API execution endpoints; keep the complete policy/contracts,
   pure-policy tests, capabilities reason, and single-owner decision.
5. General external draft/EAGLE convenience UI beyond selecting a known local
   pair; keep capability registry, validator, Lab adapter contracts, and one
   runtime-proven path where available.
6. Live performance-collapse notification; keep the telemetry flyout and
   memory/context/runtime-health conditions. Comparable baseline collapse is
   the easiest alert to get noisy.
7. Per-event audio customization; keep global enable/mute/volume, explicit event
   policy, safe playback, TTS arbitration, and visual equivalents.
8. Project State model-proposal command; keep direct user-editable state,
   persistence, provenance, and bounded Chat/Agent context.
9. Planner-proposed per-child model ids; keep explicit user selection in plan
   review and complete identity persistence/no-fallback behavior.

--- do not descope below this line ---

10. Five evidence categories and deterministic normalized outcomes.
11. Raw evidence preservation, experience store inspection/correction/removal,
    and safety isolation.
12. Runtime/config identity and capability Unknown semantics.
13. Structured analytical GPU Fit plus direct observation kept separate.
14. Lab isolation, correctness, evidence preservation, and explicit Apply.
15. Chat scroll regression coverage, because behavior already ships and must not
    regress while Chat/telemetry UI changes.
16. Dated research/watch decisions. Removing the rows would repeat the exact
    silent-scope-loss this contract exists to prevent.

## 7.9 Architectural collisions and resolutions

- **Three-way provenance versus required five-way evidence:** compatibility
  converter first; no experience persistence before it.
- **Benchmark fingerprint versus experience store:** migrate/extend identity and
  reference benchmark evidence; do not copy benchmark history into Lab.
- **Services owns processes, Lab needs temporary configs:** add a narrow isolated
  runtime-session service; do not orchestrate through `ServicesViewModel` or
  mutate saved servers.
- **Live telemetry and GPU Fit need the same measurements:** one telemetry
  source with evidence/trust, two projections. No duplicate platform probes.
- **Project State versus Memory/Recall/RAG:** separate Project-owned schema and
  review workflow, with provenance links only.
- **Agent subtask model selection versus ephemeral `AgentWorkspaceOptions`:**
  persist model identity on task/spec/transcript before changing execution.
- **Local API separate process versus file-backed Agent state:** no endpoints
  until one process/service owns per-task mutation.
- **Audio reuse versus unsafe Windows PowerShell interpolation:** harden shared
  playback before increasing its call surface.
- **Chat anchoring already implemented:** extract/test state transitions; avoid a
  replacement that regresses existing re-pin behavior.
- **R30 fixed capability shape versus future mechanisms:** registry plus typed
  adapters, not a larger `LocalModelCapabilities` record.

## 7.10 Public-repository security gate per push-sized batch

Before any push is considered:

1. `git status --untracked-files=all`; identify every file intentionally.
2. Inspect the complete staged diff and commit range for credentials, tokens,
   private/local paths, user/host names, logs, TRX/coverage, model data,
   generated secrets, dumps, and temporary Lab outputs.
3. Confirm process launches use `ArgumentList`, loopback binding, ownership-
   scoped cleanup, and existing trust/path/symlink checks.
4. Confirm persistence is additive/atomic/parameterized and exports are
   redacted.
5. Confirm approval, workspace policy, risk classification, fingerprinting,
   SHA256, secret storage, and destructive confirmations are unchanged or more
   constrained.
6. Run focused tests, zero-warning build, required full suite boundary, and
   `git diff --check`.
7. Update authoritative docs and `CHANGELOG.md` only for behavior that has
   actually landed.
8. State the Linux live gate performed or still required.

## 7.11 Explicit rejections

- No autonomous fine-tuning, LoRA/adapters, self-modifying policy, or continuous
  training from experience.
- No opaque automatic model routing, specialist mapping, profile selection, or
  universal winner score.
- No model-authored normalized outcomes or capability claims.
- No empirical observation silently rewriting GPU Fit math or defaults.
- No experience input to safety, approval, workspace, or API authority.
- No raw JSON editor for experience or Project State.
- No automatic Project truth extraction from ordinary Chat/Agent prose.
- No whole-workspace knowledge graph or multi-hop expansion in R31.
- No Lab mutation of the active Chat server or settings during an experiment.
- No speed-only Apply when correctness is required or Unknown.
- No reused-token count inferred from timing.
- No filename/build-number capability guessing.
- No Stable llama.cpp channel while upstream calls semantic versioning work in
  progress.
- No GLM MTP success from NextN metadata plus generic help alone.
- No Agent approval endpoint authenticated only by an API token/fingerprint.
- No two independent Agent services mutating the same task state.
- No alert for merely high GPU utilization and no noisy per-token notifications.
- No unrelated beeps, downloaded sound pack, shell-string playback, or cue that
  lacks a visual equivalent.
- No new NuGet dependency for convenience.
- No test parallelization, test artifacts in the checkout, fake platform passes,
  coverage padding, broad process kills, permanent `develop` branch, push per
  small commit, tag, release, merge, or repository-settings change.

## 7.12 Documentation obligations

As behavior lands, update at minimum:

- `docs/features.md` and `docs/user-guide.md` for every user-visible surface;
- `docs/agent.md` for normalized outcomes, Project State context, model
  selection, and any Local API Agent behavior;
- `docs/projects.md` for Project State;
- `docs/benchmarks.md` and a new or clearly separated Lab workflow document for
  shared measurement versus experiment semantics;
- `docs/llama-cpp-features.md` for capability registry and dated watch results;
- `docs/voice.md` for feedback versus TTS/audio capture;
- `docs/security-review.md` and privacy claims for experience, Lab exports,
  scoped Agent API, telemetry, and temporary runtime ownership;
- `CHANGELOG.md` only when behavior has shipped.

Do not document the plan as current product behavior.
