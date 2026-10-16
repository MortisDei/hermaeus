# 13. Execution and evidence

## Starting state and approved refinement

2026-10-16: `r34/round`, clean at
`1984384e680dbb3219ee29d7a8807d5002ec21f6`. The additional owner commit archives
R33 without changing source. Released source baseline remains
`23cec510c86a65d71c0dbffeaebe31b33973c8f5`. Active links now follow the archive;
historical packs are not rewritten.

The approved plan now separates B0 minimum reproducible source/build/security
evidence from B1 per-host performance/native/package evidence. It explicitly
investigates Job Object/group/session ownership, gates new ingestion ceilings
on legitimate workload evidence and owner approval, isolates owner-gated vault
migration, and permits evidence-backed conditional Retain/Defer dispositions.
Notice delivery and unresolved asset licensing remain release gates.

## B0 minimum baseline: VERIFIED EXISTING

All commands below ran on the approved Windows host before production repairs:

| Evidence | Actual observation |
| --- | --- |
| Source/graph | Clean start above; 13 projects; Agent depends on Core, not Services. No dependency/configuration changes |
| Host | Windows `10.0.26300`, `win-x64`; .NET SDK `10.0.113`, runtime `10.0.13`, MSBuild `18.0.36` |
| `dotnet build Hermaeus.sln` | Exit 0, zero warnings/errors |
| `dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj --no-build --no-restore --results-directory "$env:TEMP\hermaeus-r34-baseline" --logger "trx;LogFileName=baseline.trx"` | Exit 0; 2,871 passed, zero failed/skipped; duration 3m52s |
| `dotnet list Hermaeus.sln package --vulnerable --include-transitive --no-restore --format json` | Exit 0; NuGet source named, all 13 projects, no vulnerable-package entries/errors. Advisory observation, not complete native/external-runtime security proof |

Baseline suite includes existing Agent, RAG, containment and native-control
regressions. No owner app, settings editor, model or sandbox was launched.
B0 unlocks S1/S2/L1. It does not close B1, any repair, Linux execution, performance
comparability or native owner acceptance.

## Implementation status

The first S1/S2/L1 integration and S4 owner-save repair passed full verification
and canonical coverage, then landed in separate local commits below. The
watched-source text-hashing bypass was included in the bounded reader repair.
`FIXED` applies only to the exercised defects, not complete containment, native
acceptance or release readiness. S3 proceeds separately with activation disabled.

## Reproduction and focused evidence

External TRX files are under `%TEMP%/hermaeus-r34-focused` unless noted.
These runs are sequential Windows native-control/process evidence, not Linux,
physical microphone, pixels, owner workflow or release acceptance.

| Run | Actual result and interpretation |
| --- | --- |
| `%TEMP%/hermaeus-r34-red/red.trx` | 20 cases: 17 failed, three passed. Reproduced caller-cancelled recipe descendant survival, probe root survival, stderr backpressure, actual web over-read and Services/Settings ownership failures |
| `focused-1.trx` | All 23 passed after the initial repairs |
| `focused-2.trx` | All 111 passed, including telemetry/native-probe and RAG lineage siblings |
| `siblings-and-benchmark-red.trx` | 28 passed, two failed. Benchmark replacement did not start loading. The Activity weak-reference case still held its template host, so that failure did not prove a product leak |
| `focused-3.trx` | 44 passed, one failed. Correctly releasing the test host resolved the Activity fixture failure. The Benchmark test accidentally requested an extra completed load; replaced with a controlled in-flight signal/coalescing check |
| `focused-4.trx` | All 56 passed, including corrected Benchmark replacement, partial failure/retry, callback siblings and bounded process streams |
| `chat-rag-contract-red` build | Compile failed on a missing test namespace import; no TRX or behavioural evidence |
| `chat-rag-contract-red-2.trx` | Eight failed: three RAG ownership assertions and two new-helper pre-cancel contracts reproduced; three Chat cases failed before assertions because the plain test app lacked its production UppercaseConverter resource |
| `chat-and-page-red.trx` | Five passed, four failed. RAG and probe contract repairs passed. Valid Chat reproduction showed obsolete callback clearing, retained detached callbacks and doubled controlled dictation; a generated 2,001-page PDF bypassed the proposed page ceiling |
| `setup-secret-red.trx` | Three passed, five failed. Existing cancellation root cases passed. Reproduced unbounded voice/setup logs and setup UI accumulation, oversized secret-prefix acceptance, and stderr-before-stdin backpressure |
| `integrated-1` build | Compile failed because Chat's XAML still referenced a removed telemetry flyout Closed handler. Restored the owner-guarded handler; no tests ran |
| `integrated-2.trx` | All 150 passed, including 30 ingestion-limit cases, Chat/RAG ownership, process/secret/setup bounds and outcome semantics |
| `%TEMP%/hermaeus-r34-integration/full.trx` | 2,966 passed, one failed, zero skipped. Existing large Git-status test exposed changed output ordering from tail selection; retain bounded beginning for status, while command diagnostics remain tails. This is a failed integration gate, not a full pass |
| `owner-save-red-and-siblings` builds | Two compile attempts failed on a missing/wrong RagQueryService namespace; no behavioural evidence |
| `owner-save-red-and-siblings-3.trx` | 24 passed, one failed. Controlled Agent target ownership did not block the real editor Save command, proving the composed owner-save gate bypass. Watched-source bounds/identity/cancellation siblings passed |
| `owner-save-and-siblings-green.trx` | All 159 passed, including existing Agent harness/Git ordering, the real editor target-gate conflict and watched-source cases |
| `integration-final-focused.trx` | All 68 passed, including newer queued editor text/selection changes, view ownership and ingestion limits |
| `%TEMP%/hermaeus-r34-integration/full-final.trx` | All 2,975 passed, zero failed/skipped, 4m03s. Both full Debug and Release solution builds passed with zero warnings/errors |
| `pwsh ./scripts/coverage.ps1` | All 2,975 passed; 60,166/83,034 lines, 72.46%, floor 60%. Tests took 7m55s after instrumentation. Canonical script kept results outside checkout and removed its temporary report |

No case is marked fixed solely because it was written or because its original
failure was expected. Later S3 code/tests are outside this integration receipt
and require their own verification; these counts do not claim S3 passed.

### Local implementation batches

| Commit | Bounded change |
| --- | --- |
| `0ac5b6b` | Agent command/Git capture and cancellation |
| `7023270` | Native diagnostic probes |
| `a921432` | Voice/setup/OS-secret process output |
| `479b7c4` | Owner-approved ingestion ceilings and reader siblings |
| `408f93e` | Chat/RAG visual callback ownership |
| `344ba75` | Sibling pages and Benchmark retry/load ownership |
| `1629c92` | Owner editor/Agent target coordination |
| `b26fad5` | Package notice delivery |

No branch publication, PR, merge, tag, release, CI-policy or repository-setting
change accompanied these commits. Broad native and release gates remain open.

## Approved S2 limits and legitimate PDF workload

The owner supplied three local PDFs for compatibility measurements, then
explicitly approved **512 MiB source bytes per local document, 16 Mi extracted
characters per document, 2,000 PDF pages, and 256 MiB accounted retained staging
per operation**. The previous 50 MiB proposal would reject all three legitimate
examples and is superseded. No private title, path, hash or extracted text is
committed. A scratch .NET 10 console used the existing PdfTextExtractor and
already restored PdfPig binaries; it did not ingest into an owner dataset.

| Source bytes | Pages | Extracted .NET characters | Elapsed ms | Allocated bytes | Peak working-set bytes |
| --- | --- | --- | --- | --- | --- |
| 89,923,985 | 320 | 1,155,867 | 1,682 | 980,905,528 | 263,299,072 |
| 100,473,882 | 322 | 1,019,866 | 2,256 | 905,986,712 | 321,826,816 |
| 330,720,040 | 390 | 999,948 | 1,673 | 1,294,132,152 | 582,643,712 |

Each ran in a fresh process; SHA256 before/after confirmed unchanged input.
The pre-read hash warmed filesystem data. These are single compatibility and
allocation observations, not cold-cache benchmarks or speedup claims. Allocation
totals include reclaimed objects and are not retained memory or leak evidence.
Retained staging counts candidate text/vector payload once, including retained
chunks. Parser/provider temporaries, old-generation loading and managed-object
overhead are not a hard RSS bound. Page extraction can allocate before an
application text check; no hard parser timeout or sandbox is implied.

After the revised extractor was built, the same three inputs produced exactly
the same page/character counts and all remained hash-identical. Separate-process
observations in source-size order were 2,131/2,303/2,262 ms,
898,502,480/812,869,872/979,699,176 allocated bytes and
86,884,352/167,211,008/78,168,064 peak working-set bytes. These remain single,
hash-warmed compatibility observations, not controlled speed/memory comparisons.
The extractor now reads the seekable file rather than asking PdfPig to open a
filename; no hard parser allocation claim follows from the lower observations.

## B1 host observation and conditional dispositions

Read-only Windows observation: build `10.0.26300`, Intel Core i7-13620H,
10 cores/16 logical processors, 16,869,351,424 RAM bytes. `nvidia-smi` reported
RTX 4060 Laptop GPU, 8,188 MiB, driver 617.14. No Hermaeus Desktop or llama-server
process was observed. This does not identify an installed model/runtime,
power mode, thermal state, R33 comparison workload or Linux backend.

- **D1:** retain explicit Desktop DI ownership for now: Desktop consumes DI
  APIs directly, the repeated declaration creates no second container or
  runtime-package saving. Defer DBus pin and Inter removal until native Linux
  integration and saved-font fallback evidence are available. No deletion quota.
- **D2:** retain navigation notifications and task/target coordination entries
  for now. Generated attributes offer little net deletion; safe lock reclamation
  is not justified by an observed memory problem. No raced semaphore removal.
- **D3:** retain tooltip/wheel/modal helpers pending whole-contract native
  equivalence, particularly COSMIC and mixed DPI. Upstream fix presence is
  research evidence, not a reason to delete working native behaviour.
- **P2/P3:** retain reference retrieval/runtime paths while per-host baseline,
  quality and effective-capability evidence are missing. No optimisation or
  default-runtime/model change is selected by these compatibility measurements.

These are evidence-backed Retain/Defer dispositions for conditional changes,
not waivers of safety, native, package or licensing release gates.

## S1 native ownership investigation

The existing Services `ProcessJobObject.Default` is one process-wide Windows
job with kill-on-close, assigned after process start. Closing it to cancel one
command could terminate unrelated managed servers/voice work; it is not reused
as a per-command boundary. Assignment after start also leaves a launch race.
Windows supports creation-time job assignment through
[PROC_THREAD_ATTRIBUTE_JOB_LIST](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute),
but adopting a native launcher requires a separate reviewed design and native
proof. No such boundary was introduced.

On Linux, parent-side [setpgid](https://man7.org/linux/man-pages/man2/setpgid.2.html)
can fail after the child executes. A child-side group/session launch must be
established before exec; an external `setsid` command adds executable/fork/PID
ownership assumptions. Descendants can create another group/session. Group
termination acknowledgement and root exit do not prove all descendants exited.
[.NET tree kill](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill?view=net-10.0)
can omit inaccessible descendants. Current repairs therefore report best-effort
termination and uncertain cleanup, supported by controlled Windows fixtures,
without claiming containment or actual Linux acceptance.

## S4 and L2 bounded acceptance audit

Existing tests are evidence only for their exercised contracts. Static harness
methods run through `AgentHarnessTests`, `ServiceHarnessTests`,
`BackupHarnessTests`, `McpHarnessTests` and `LocalApiHarnessTests`; a filter naming
only the static implementation class can accidentally select no cases.

| Cases | Existing evidence and actual remaining limit |
| --- | --- |
| A01/A02 | Workspace traversal/ignored-root and archive parent/absolute/drive/UNC/mixed/reparse fixtures exist. They do not prove live ancestor replacement between final validation and filesystem use, or every direct/queued/Rewind symlink combination |
| A03/A05 | Policy drift, changed prepared target, missing preparation, fingerprints and durable reopen/reconciliation fixtures exist. Seeded recovery state is not physical crash-at-write injection; the complete independent tamper matrix remains open |
| A04 | New controlled editor/Agent race reproduced a bypass. The editor now uses the same existing target owner; late results cannot overwrite another editor or clear newer text. External writers remain outside this in-process coordination |
| A06/A07 | Command family/model-risk/MCP approval regressions preserve deterministic gates. A controlled approved MSBuild descendant demonstrates transitive code execution; no outside-canary/network-exfiltration acceptance was run |
| A10/A11 | Corrupt ownership stays Unknown; fake PID/executable mismatch fixtures refuse termination. These do not prove real PID reuse or final runtime listeners across aliases/environment/presets. Core host conflict checks remain; no expanded launch policy was invented |
| A14 | Restore declared-size/duplicate/traversal/pre-cancel/reparse and rollback fixtures exist. Actual forged expansion, disk-full interruption and live filesystem swaps remain unverified |
| A16/A17 | Token rejection/revocation and real Local API child reload, pure redaction and export redaction fixtures exist. Health-prefix/method/route cross-products and persisted synthetic-child-output/export/exception redaction remain incomplete |
| A20 | Sandbox integration remains Defer. No adapter or second approval authority was added, and no sandbox containment pass is claimed |
| L2 | Existing coordinator caches shutdown, reverses owners, records incomplete asynchronous deadlines and observes abandoned task faults. A synchronously blocking owner before its first await is outside that deadline proof. Actual active-runtime drain, tray/restart and Linux desktop shutdown remain native acceptance gates |

No broad shutdown `Task.Run` conversion, sandbox, handle-based filesystem
authority or runtime manager is introduced to paper over those limits.

## Windows package observation

`pwsh ./build.ps1 -Runtime win-x64` completed RID restore, Release publish,
native launcher compilation and ZIP creation. The local development archive
contains `docs/THIRD-PARTY-NOTICES.md` (12,726 bytes) and
`docs/CMUDICT-LICENSE.txt` (1,754 bytes), each SHA256-identical to its source.
The candidate ZIP is 40,465,086 bytes, SHA256
`7cecd6a7c020e12e4aeaca3e80185e50aa7ab88c80694d5638cb1f432aed583f`.

Generated candidate artefacts are retained under ignored
`dist/r34-candidate-win-x64/`. Package metadata still says `0.41.0-beta`;
this is an unreleased development build, not a new version or published release.
The existing R33 package paths were preserved/restored; their archive remains
SHA256 `2d4858e7f9e3f297285b194d3cacc7ec2cf79d64dcc12a7280d143f24cd28476`.
No installation, shortcut registration or publication was performed. Full
upstream licence text delivery, Linux archive proof and unresolved asset terms
remain release gates; the two verified notices do not close those obligations.

## Explicit pending decisions and limits

- **S2 text workload:** tracked supported text/source fixtures total 1,227 files,
  11,980,577 bytes and 11,968,165 decoded .NET characters; largest file is
  `AgentService.cs`, 228,907 bytes. Docs plus root README/CHANGELOG are 263 files,
  2,920,917 bytes. There are no tracked PDFs. These legitimate workloads do not
  validate 50 MiB local/16 Mi-character/2,000-page/256 MiB staging proposals.
  The later owner PDF workload and approval above supersede the pending decision;
  implementation and verification of approved ceilings remain in progress.
- **S3:** conditionally approved for isolated implementation and tests. Before
  implementation, specify exact strict-UTF8/UInt32LE name AAD and validated
  protected recovery discovery after Data Root relocation. See
  [14](14-vault-format-decision.md). Production activation remains gated on
  cross-process/native/recovery evidence and explicit owner acceptance; current
  production format remains active meanwhile.
- **S1 ownership:** the shared Services Windows job is post-start, process-wide
  and unsuitable for per-command cancellation. Linux has no current group/
  session launcher. Best-effort tree termination is not containment, and waiting
  for a root process is not proof every descendant exited. Native launch design
  changes need explicit review before a new boundary is introduced.
- **B1/G1:** both target-host baselines/native workflows, actual RID archives,
  delivered notices and unresolved asset redistribution terms remain open.
