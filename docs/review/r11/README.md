# Review Round 11 (r11)

Theme: **Services deep-dive**. Aether.Services is the largest project in
the repo (73 files, ~14k lines: providers, process management, stores,
setup/download, Doctor, voice glue) and had never had a dedicated audit
round; it has only ever been touched incidentally by other rounds. This
audit read every .cs file in the project, the way r10 read every file in
Aether.Rag, and found the same pattern r10 did: features that look done
and have never worked end to end, sitting next to real field-impacting
defects.

Headline findings, all verified in code (and where marked, against live
external state):

- **The built-in llama-server installer has never worked.** The pinned
  download URLs name release assets that do not exist (verified against
  the GitHub API for tag b4341: no asset contains "llama-server" at
  all), and the install-latest path filters assets by a substring no
  real asset name contains, so it always throws "no asset matched".
  Even with correct names, both paths move a raw zip archive into place
  as the executable. Doctor's "Download llama.cpp" fix action sits on
  the same code, so the advertised recovery path for a missing binary
  is equally broken.
- **Windows executable resolution never tries `.exe`.** Four separate
  copies of PATH/directory resolution probe for the bare name
  `llama-server`, which cannot resolve on Windows. The default settings
  ship `ExecutablePath = "llama-server"`, so a fresh Windows install's
  managed servers are unstartable out of the box. This is the root
  cause of the r10 field finding that the owner's Embeddings server
  never launched.
- **Ollama chat does not stream.** The response is buffered in full
  before the first token is yielded.
- **Moving the data root silently loses secrets.** The migration only
  moves three database families plus `agent/`; `secrets.local.json`,
  `secrets.local.key`, `traces.db`, `eval_runs.db`, logs, and the voice
  lexicon are stranded in the old root.
- **The benchmark LLM judge is a phantom feature.** `UseJudge` and
  `JudgeModelId` are editable in the UI and persisted with every suite
  and run, and no code anywhere executes a judge.

## Documents

- `01-install-and-resolution.md` - the broken llama-server installer,
  Windows executable resolution, ExtraArgs backslash mangling, the
  auto-tune port hole, unverified downloads, and the Python version
  gate that never gates.
- `02-provider-correctness.md` - Ollama buffering, OpenAI shared-client
  auth races, the gpt-prefix model filter, cold-cache misrouting, stale
  context-length cache, the phantom judge, rerun tag loss.
- `03-store-integrity.md` - data-root migration losses, memory save
  path embedding stalls, FTS rank ignored, DateTime kind loss, archive
  re-embedding, hot-database backups.
- `04-process-and-voice-runtime.md` - the LocalApi job-object gap,
  Windows audio playback, temp wav leaks, process-exit races.
- `05-roadmap.md` - version, sequencing, test expectations, security
  review touch, explicit rejections.

## How to work this pack

Same conventions as r1-r10 (see `docs/review/archived/`): every item has
acceptance criteria; check archived rounds before re-proposing anything
explicitly rejected; zero-warning builds (`TreatWarningsAsErrors`
solution-wide); tests run via
`dotnet test src/Aether.Tests/Aether.Tests.csproj` (see the
`build-and-verify` skill); no em dashes anywhere in code, comments, or
docs; the approval-gated agent security posture is non-negotiable.
Nothing in this pack deletes user data; the one migration change (3.1)
moves more data, never less.
