# Review Round 13 (r13)

Theme: **Model library and hardware truth**. The owner's verdict is
"usability is what is going to decide whether this app sinks or swims",
and every item in this round is a first-five-minutes usability lever:
the System page must tell the truth about the machine (it currently
shows "Windows 10" on Windows 11, "unavailable" RAM, and no GPU at
all on non-NVIDIA-CLI setups), and the Models page must grow from a
metadata editor into an actual model library: see what you have, see
what fits your hardware, tune it, keep it up to date from Hugging
Face, and stop living inside `hub\models--org--repo\snapshots\<sha>`
folder mazes.

Origin: owner field report on 0.17.0-alpha with four screenshots, plus
owner feature asks (HF update checks, flat model folder, fits-on-my-
hardware guidance a la Unsloth), plus a code audit of
`SystemInfoService`, `ModelManagementViewModel`/`ModelManagementView`,
`ServicesViewModel`'s auto-tune path, `ModelDownloadService`,
`StarterModelCatalog`, and `LocalAiAssetLocator`.

Headline findings, all verified in code:

- **Windows telemetry is a stub.** `SystemInfoService` returns 0 for
  available RAM on Windows (`GetAvailableMemoryBytes` only reads
  `/proc/meminfo`), reports the raw kernel string as the OS (Windows
  11 self-describes as `Microsoft Windows 10.0.26xxx`), returns the
  process architecture ("X64") as the CPU *name*, and probes GPUs via
  `nvidia-smi` or Linux DRM only, so a Windows machine without the
  NVIDIA CLI shows no GPU and no VRAM. Everything downstream that
  wants hardware awareness (starter-model recommendation, the new
  fits-check) is starved by this.
- **Auto-tune exists but lives in the wrong place for per-model use.**
  `ServerProcessManager.AutoTuneAsync` + `LlamaTuneProfile`
  persistence work well, but are reachable only through a managed
  server row on the Services page. The Models page, which lists every
  GGUF on disk, has no way to tune any of them.
- **The Models page does not scroll reliably** (owner cannot reach the
  bottom of a 32-model list regardless of window size) and each model
  renders a full 5-row editor grid unconditionally, which is both the
  probable scroll culprit (wheel capture by 8 spinners per card) and a
  usability wall in its own right.
- **The chat header temperature spinner is an orphan control**: chat
  already holds the full local sampling state (temp, top-p, top-k,
  min-p, penalties, max tokens) and sends it per request, but only
  temperature is editable from chat.
- **Nothing records where a downloaded model came from**, so update
  checks are impossible today even though the HF hub cache path the
  owner hates (`models--unsloth--...`) literally encodes the repo id
  we would need.

## Documents

- `01-system-truth.md` - real RAM/OS/CPU/GPU facts on Windows, and a
  reusable hardware snapshot for the fits-check.
- `02-model-library.md` - Models page rework: compact cards, the
  scroll fix, per-model and tune-all auto-tune, fits-on-your-hardware
  chips, and the flat `Models\LLM` folder migration.
- `03-hugging-face.md` - model provenance manifest, manual update
  checks against the HF API, verified update downloads, and an
  in-app "Get models" browser.
- `04-chat-sampling.md` - replace the orphan Temp spinner with a
  sampling flyout.
- `05-roadmap.md` - version, sequencing, test expectations, security
  review touch, explicit rejections.

## How to work this pack

Same conventions as r1-r12 (see `docs/review/archived/`): every item
has acceptance criteria; check archived rounds before re-proposing
anything explicitly rejected; zero-warning builds
(`TreatWarningsAsErrors` solution-wide); tests run via
`dotnet test src/Aether.Tests/Aether.Tests.csproj` (see the
`build-and-verify` skill); no em dashes anywhere in code, comments, or
docs; the approval-gated agent security posture is non-negotiable.
Two items in this pack move or replace user files (2.6 migration, 3.3
update); both are preview-and-confirm gated and neither ever deletes a
model file without an explicit user confirmation.
