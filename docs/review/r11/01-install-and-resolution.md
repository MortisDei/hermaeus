# 01 - Install and executable resolution

The onboarding-critical path: getting a llama-server binary onto disk
and resolving it at launch. Most items here are "has never worked"
class, not regressions.

## 1.1 Pinned llama-server download URLs point at assets that do not exist

`LlamaServerSetupService.DownloadDefinitions`
(src/Aether.Services/LlamaServerSetupService.cs:14-34) names assets like
`llama-server-b4341-win-avx2.exe`. Verified against the live GitHub API
(`releases/tags/b4341`, checked 2026-07-16): the release contains 21
assets, all zip archives named `llama-b4341-bin-<platform>.zip` (plus
cudart bundles); none contain the string "llama-server" and no bare
`.exe` assets exist. Every pinned-install attempt
(`InstallAsync` -> `ModelDownloadService.DownloadAsync`) downloads a
GitHub 404 response or fails; the wizard/setup path that offers
"Download llama-server" (LocalAiSetupService.cs:157-165,
DownloadLlamaServerAsync:294-314) has never produced a working binary.

Fix direction: download the real zip asset for the current platform,
extract it (`System.IO.Compression`, already referenced by
BackupService; no new NuGet), locate `llama-server(.exe)` inside the
extracted tree, and keep the sibling DLLs the Windows builds require
next to it. Pin a current build tag, not b4341 (Dec 2024). Add a SHA256
step: GitHub does not publish per-asset hashes in the release API, so
either pin the zip hash alongside the tag, or document that the pinned
path is tag+size verified only and rely on HTTPS+GitHub provenance
(decide in implementation; the roadmap's security section requires the
decision be written down in docs/security-review.md).

Acceptance criteria:

- `GetDownloadInfo()`/`GetSupportedReleaseInfo()` URLs match real asset
  names for the pinned tag (unit test against a fixture of the actual
  release JSON captured into the test data, not a live call).
- Install produces an executable that `ServerProcessManager` can start
  (integration-style test may fake the download with a zip fixture
  containing a stub file; asserts extraction + placement + Unix
  exec-bit path).
- Zip extraction rejects entries that escape the install directory
  (zip-slip guard, tested with a malicious fixture entry
  `..\evil.exe`).

## 1.2 Install-latest can never match an asset, and would install a zip as an exe

`SelectDownloadAsset` (LlamaServerSetupService.cs:275-304) first filters
to assets whose name contains "llama-server"; per 1.1 no real asset
ever has, so `GetLatestDownloadInfoAsync` always throws "No llama-server
asset matched this platform". If the filter matched, `InstallLatestAsync`
(198-256) would `File.Move` the downloaded zip to `llama-server.exe`
unextracted. `DoctorService.InstallLlamaServerUpdateAsync`
(DoctorService.Runtime.cs:270-296) and the Doctor "Download llama.cpp"
fix button ride on this path, so the recovery action Doctor advertises
for a missing/outdated binary is also broken. Additionally,
`GetLatestDownloadInfoAsync` calls
`_http.DefaultRequestHeaders.UserAgent.ParseAdd` on every invocation,
appending duplicate UA products.

Fix direction: filter on the real naming convention
(`llama-<tag>-bin-<os>-...zip`), prefer cpu/avx2 variants for a
first-install default (document that CUDA/Vulkan users should point
Aether at their own build), share the zip-extraction path with 1.1, set
the UA once in the constructor.

Acceptance criteria:

- `SelectDownloadAsset` unit tests run against the captured real asset
  list for a current tag and select the expected zip on win-x64,
  win-arm64, linux-x64, linux-arm64, macos-arm64; returns null only for
  genuinely unsupported platforms.
- `InstallLatestAsync` extracts and places a runnable layout (zip
  fixture test as in 1.1).
- Repeated `GetLatestDownloadInfoAsync` calls do not grow the UA header.

## 1.3 Windows executable resolution never tries `.exe` (root cause of the r10 dead Embeddings server)

Four independent resolvers probe for a bare file name and therefore can
never resolve `llama-server` on Windows:

- `ServerProcessManager.ResolveExecutable`/`FindOnPath`
  (src/Aether.Services/ProcessManagement/ServerProcessManager.cs:397-430,
  471-484): the directory branch probes `Path.Combine(dir,
  "llama-server")` and enumerates files named exactly `llama-server`;
  the PATH branch combines the bare name.
- `DoctorService.ResolveExecutable` (DoctorService.Runtime.cs:312-330).
- `TrustService.FindOnPath` (TrustService.cs:208-221).
- `LocalAiSetupService.FindOnPath` (LocalAiSetupService.cs:524-537,
  used for nvidia-smi/rocminfo detection, same defect class).

`SettingsService.CreateDefaultServer` (SettingsService.cs:285-295)
ships `ExecutablePath = "llama-server"`, so on a fresh Windows install
both default managed servers are unstartable until the user browses to
the exact `.exe`, and Doctor reports "llama-server missing" even when
`llama-server.exe` is on PATH. This is precisely how the owner's
Embeddings server entry sat dead through r9/r10.
`VoiceProviderProcessRunner.FindOnPath`
(VoiceProviders/VoiceProviderProcessRunner.cs:99-122) already does this
correctly via PATHEXT.

Fix direction: one shared resolver (new
`ProcessManagement/ExecutableResolver` or promote the
VoiceProviderProcessRunner implementation) that on Windows tries the
name plus each PATHEXT extension for both directory probes and PATH
probes, used by all four call sites. `OrphanServerDetector.
IsSameExecutable` (OrphanServerDetector.cs:72-86) must compare against
the resolved path so orphan Stop still offers itself when the
configured value is a bare name or directory.

Acceptance criteria:

- On Windows (or with an injected PATHEXT + temp dir fixture, so the
  test runs everywhere): bare `llama-server` resolves when
  `llama-server.exe` exists in a PATH dir; a directory containing
  `llama-server.exe` resolves via the directory branch.
- All four call sites route through the shared resolver (grep-level
  test or ArchitectureTests rule banning private FindOnPath
  reimplementations in Aether.Services).
- Doctor's llama-server check goes Ready in the same fixture where
  ServerProcessManager can start (same resolver, same answer).

## 1.4 ExtraArgsParser mangles Windows paths (backslash treated as escape)

`ExtraArgsParser.Split`
(src/Aether.Services/ProcessManagement/ExtraArgsParser.cs:20-24)
consumes every `\` as an escape character, inside or outside quotes.
`--mmproj C:\models\proj.gguf` becomes `--mmproj C:modelsproj.gguf`;
every managed-server ExtraArgs value containing a Windows path is
silently corrupted before reaching llama-server, and
`TrustService.AnalyzeServerExtraArgs` (TrustService.cs:42-89) analyzes
the mangled tokens.

Fix direction: only treat `\` as an escape when it precedes a quote
(`\"`) or another backslash, or drop escape handling entirely and
support quoting only; pick one, document it in the Services settings
tooltip.

Acceptance criteria:

- `C:\models\a b\x.gguf` (quoted) and `C:\models\x.gguf` (bare) round
  trip intact; `\"` still yields a literal quote; existing tests stay
  green.

## 1.5 Auto-tune trusts /health on a port it never checked for prior occupancy

`AutoTuneAsync`/`TryProbeAsync` (ServerProcessManager.cs:151-269) start
probe processes and wait for `/health` on `cfg.Port` without the port
preflight that `StartAsync` performs (lines 54-64). If anything is
already listening on that port (the orphan scenario r9 was built
around, or an unrelated service), every candidate "reaches /health"
instantly against the wrong process: auto-tune reports the first
candidate (999 layers) as working, with a foreign server's log parse.

Acceptance criteria:

- Auto-tune fails fast with the named port owner when the port is
  occupied before the first probe (reuse `IPortOwnerLookup`; unit test
  with the fake lookup).
- Existing auto-tune tests unaffected.

## 1.6 Setup-wizard model download is never hash-verified

`LocalAiSetupService.ModelHashes` (LocalAiSetupService.cs:15) is an
empty dictionary; the "Verify hash for security if available" branch in
`DownloadGgufModelAsync` (233-240) is dead code, so the Phi-4 download
(147) lands unverified. This contradicts the security-posture skill and
the r8 StarterModelCatalog precedent, which pins SHA256 for every
starter model. The action description also claims "~9GB"
(LocalAiSetup/LocalAiSetupActionFactory.cs:36) for a Q5_K_M quant of a
3.8B model (~2.8 GB).

Acceptance criteria:

- The Phi-4 entry carries a pinned SHA256 (verify via HF LFS oid, the
  r8 method) and a corrected size; a failed verification deletes the
  file and reports failure (test with a wrong-hash fixture).
- Either `ModelHashes` becomes the populated source of truth or it is
  deleted in favor of hash fields on the action; no empty-map dead
  branch remains.

## 1.7 The XTTS Python version gate never gates

Three defects that together mean "requires Python 3.9-3.11" is never
enforced anywhere:

- `ResolveCompatiblePythonCommandAsync`
  (LocalAiSetupService.cs:855-886) validates `candidate.FileName`
  without `PrefixArgs`, so testing `py -3.11` actually validates plain
  `py` (the machine default, e.g. 3.13); the `testPython` local it
  builds is never used.
- `ValidatePythonForXttsAsync` (733-853) checks encodings/venv/prefix
  but never compares the reported version to
  `MinSupportedXttsPython`/`MaxSupportedXttsPythonExclusive`;
  `ReadPythonVersionAsync` (708-728), `IsXttsCompatibleVersion`
  (730-731), and `DefaultPythonCommand` (1017) are dead.
- `PythonHealthValidator.IsRequiredVersion`
  (PythonHealthValidator.cs:139-150) accepts any minor >= required, so
  Doctor's "Python 3.11 for XTTS v2" check passes on 3.13, which coqui
  TTS does not support; meanwhile the setup path claims max-exclusive
  3.12. The two validators disagree about the same requirement.

Fix direction: validation runs the full candidate command (FileName +
PrefixArgs); version compatibility becomes part of the XTTS validation
using the existing constants; `PythonHealthValidator` gains a
per-provider max (exact-minor or a (min, maxExclusive) range supplied
by the provider) so Doctor and setup agree; dead members deleted.

Acceptance criteria:

- A fake python that reports 3.13 fails XTTS validation and the Doctor
  XTTS python check; 3.11 passes both; Kokoro (3.12 floor) still
  accepts 3.12+.
- `py -3.11`-style candidates are exercised with their prefix args
  (process-runner seam or recorded-command fake).
- No dead members remain (ReadPythonVersionAsync et al. either used or
  removed).

## 1.8 Stale/invalid torch index URLs

`InstallTorchForBackendAsync` (LocalAiSetupService.cs:620-646) pins
`whl/cu118` and `whl/rocm5.8`. `rocm5.8` has never been a published
PyTorch index (5.7 and 6.x exist), so the ROCm branch always fails pip
resolution; cu118 is years old. Verify the current index names at
implementation time and record them; prefer failing with a clear
message naming the attempted index over silently retrying.

Acceptance criteria:

- Index URLs verified against download.pytorch.org at implementation
  time (note the date in the commit); a unit test asserts the argument
  construction per backend so future edits are visible in review.
