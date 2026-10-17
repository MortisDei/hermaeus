# Media generation extension

Requested after the original R34 implementation on 2026-10-17. This is a feature
extension, not evidence that the outstanding original acceptance gates passed.

## Investigation

Chat accepts image inputs but `ILlmService` produces text, reasoning and tool
events. Voice providers already render speech. Neither contract represents
image/music/video generation jobs. Do not overload chat streaming or assume a
model listed by `/models` supports another endpoint.

Recorded hosts: GTX 1660 Super 6 GB on Pop!_OS and RTX 4060 laptop 8 GB on
Windows. The current laptop reports 16,869,351,424 bytes of system RAM. The
desktop's current availability, RAM and media runtime are not verified.

Primary model research, checked 2026-10-17:

| Candidate | Publisher evidence | Decision |
| --- | --- | --- |
| [ACE-Step 1.5](https://huggingface.co/ACE-Step/Ace-Step1.5) | Music generation; publisher claims less than 4 GB VRAM | Local evaluation candidate. That claim does not establish end-to-end fit with all components or another loaded model |
| [FLUX.2 klein 4B](https://huggingface.co/black-forest-labs/FLUX.2-klein-4B) | Image generation/editing, Apache 2.0; approximately 13 GB VRAM in the card | Full configuration exceeds both recorded GPUs. Quantisation/offload requires exact artifact, runtime and companion validation |
| [Wan 2.1 T2V 1.3B](https://huggingface.co/Wan-AI/Wan2.1-T2V-1.3B) | Publisher lists 8.19 GB VRAM | Not a proven fit on either GPU. Smaller resolutions/offload/quantisation require separate execution |
| [LTX-2](https://huggingface.co/Lightricks/LTX-2) | Joint audio/video; ComfyUI integration and multiple checkpoints | Research candidate, no fit or performance claim on these hosts |

Weight size alone is not working-set memory. Precision support, text encoders,
VAE/vocoder, attention buffers, resolution/duration, CPU offload and simultaneous
chat/voice/RAG allocations matter. No weights, custom nodes, Python packages or
new runtime are installed by this feature. Licences require exact artifact review
before any future download/redistribution integration.

## Implementation plan

1. Add a media-specific request/result/service boundary in Core. Keep chat,
   Agent tool authority, voice playback and existing persisted state unchanged.
2. Implement explicit ComfyUI API-workflow submission, per-job history polling
   and output retrieval. The user supplies a trusted API-format workflow using
   exact `{{prompt}}` string input placeholders; the workflow owns model and
   sampler settings. Never install custom nodes or interrupt a global queue.
3. Implement OpenAI-compatible image generations, audio-output chat completions
   and asynchronous video create/status/content. Use configured endpoint/key
   references, snapshot per operation, and per-request authentication. No model
   name-based capability promise or automatic paid probe.
4. Add a native Media workspace with explicit Generate/Cancel, job status,
   image preview and explicit export for images/audio/video. No autoplay or
   silent execution of generated files. Existing chat input remains chat.
5. Verify request shapes, polling terminal states, malformed/oversized outputs,
   credential isolation, cancellation and stale UI results with controlled HTTP
   fixtures. Run full build/suites and canonical coverage before local commits.
   Real model/provider and Windows/COSMIC interaction evidence stays separate.

## Safety and compatibility contract

- Only HTTP(S) configured endpoints, without embedded credentials, query or
  fragment. Disable automatic redirects. Never attach credentials to a returned
  arbitrary URL. Output URLs must stay at the configured origin; cross-origin
  URL-only image responses are refused rather than fetched speculatively.
- Bound control JSON, workflow complexity, output count and actual decoded/
  downloaded bytes. Initial client output policy: at most eight files and
  128 MiB combined per operation; JSON/base64 responses also have finite caps.
  These are client accumulation limits, not backend GPU/RAM/time guarantees.
- Cancellation stops client requests/polling and discards incomplete results.
  An accepted backend job may keep running. Preserve its job ID in visible
  status; do not resubmit automatically or claim server cancellation.
- Results are session-only byte payloads. Export writes ordinary selected media
  files through atomic replacement. No new database, conversation schema,
  credential format, runtime manager, dependency or Agent capability is needed.
- Only expose inert image preview and explicit save actions. Treat returned
  names/paths as untrusted; never use a backend filename as a local path.
- Do not log prompts, credentials, base64 data or raw provider error bodies.

## Protocol references

- [ComfyUI server routes](https://docs.comfy.org/development/comfyui-server/comms_routes)
- [ComfyUI official API example](https://github.com/Comfy-Org/ComfyUI/blob/master/script_examples/websockets_api_example.py)
- [Pinned saved media result serialisation](https://github.com/Comfy-Org/ComfyUI/blob/5cc4b870094c89090db54bd8d12649dcaf89f2ba/comfy_api/latest/_ui.py): saved audio uses `audio`, built-in video previews use `images`, and records include filename/subfolder/type. The corresponding pinned audio/video node sources were also inspected. This exposed the saved Opus filename sibling, now accepted as Ogg output. Direct browser raw fetch was unavailable and the unauthenticated metadata API hit its rate limit; read-only raw-source retrieval on the approved host supplied the pinned source.
- [OpenAI image generation](https://developers.openai.com/api/docs/guides/image-generation)
- [OpenAI audio](https://developers.openai.com/api/docs/guides/audio)
- [OpenAI video generation](https://developers.openai.com/api/docs/guides/video-generation)

## Execution evidence

The protocol client and native Media workspace are implemented. Provider settings
use additive fields in the existing LLM settings section and the existing secret
store/save flow, not a new persisted format. Both archive scripts and bundled
help include the user-facing media guide. No new package or inference engine is
installed or managed.

Initial compilation found a test discard binding error, a Windows platform
analyser error for UnixCreateMode assignment, and obsolete Avalonia Watermark
properties. These were corrected without suppression. The first integrated run
passed 65/66 tests; its failure was a new fixture looking for `settings.json`
instead of the existing harness's `settings/settings.json`. Correcting that path
preserved the persisted-value and raw-secret absence assertions. The next
selection passed 72 cases, and the expanded selection passed 80 in 9 seconds.
Receipts are outside the checkout under `%TEMP%/hermaeus-media-focused/`, named
`media-integrated-second.trx`, `media-integrated-third.trx` and
`media-integrated-fourth.trx`. Their zero-warning Debug builds were observed.

The source audit also bounded repeated prompt substitution before serialising
the expanded workflow: a small template containing many placeholders can
otherwise amplify a bounded prompt into an enormous request. Escaped UTF-8
length deltas are counted before replacement, with an 8 MiB request limit and
reserved envelope allowance. Exact/combined output budget, false/missing length,
actual audio/video output-group, deadline and native preview/ownership fixtures
exercise the corresponding client contracts.

Final focused/full/platform/package/coverage receipts follow only after their
execution. No real model generation, hardware fit, paid-provider access, native
owner workflow acceptance or production completion is claimed by HTTP fixtures
and native control tests.

The first full tree passed 3,188 Windows tests in 4m42s and 3,149 Linux tests
with 39 Windows-only skips in 7m42s. Both platforms built actual development
archives from a 1,363-file snapshot, SHA256
`efa619b223fa78d00a992c079498e22d78c1393e02d06d0d775579f3fb6d2b9f`.
Those first archives passed byte-equal checks for 75 notice files, two copies
of the media guide, two unchanged native SQLite modules, archive checksums,
no PDBs and Linux ownership/modes. The R33 Windows control remained unchanged.
The archive verifier initially used incorrect CMU source-path and ZIP-root
assumptions; corrected checks passed without a product change. Raw failed and
passing logs remain under
`%TEMP%/hermaeus-media-gate-a827fc3e83c545c5ab8486fa82ecbabb/`.

Final navigation audit found Media missing from the shared command palette.
The real registry regression failed once (`media-palette-red.trx`), then passed
after adding the existing navigation registration. The 82-case selection passed
in 10s. Pinned upstream schema review subsequently added the saved Opus sibling
and removed unproven MOV handling. Final suites and refreshed archives must
cover these changes before the commit gate; the earlier receipts do not.

The palette/Opus tree subsequently passed 3,190 Windows tests in 4m54s and
3,151 Linux tests with 39 Windows-only skips in 7m21s. Its refreshed development
archives also passed the same notice/help/native-library/checksum/layout checks.
Those receipts precede the later shared-save repair described below.

The new provider configuration exposed two related shared-save defects:
section validation ran outside the settings save error handler, and Services
unconditionally confirmed a completed save delegate even when persistence had
reported failure. A real invalid-endpoint regression failed with an escaping
ArgumentException (`services-media-save-red.trx`). Validation now participates in
the existing failure flow; the shared gated save returns an explicit outcome
and Services confirms only success. The failed edit does not enter committed
settings, remains correctable, and a subsequent valid save succeeds. A sibling
regression checks that a failed save clears an earlier confirmation. No second
save authority or persistence format was introduced.

The combined Media, Services save, settings lifecycle and startup/navigation
selection passed 118 tests in 15s (`media-save-focused.trx`). The full solution
built Debug and Release with zero warnings/errors (1.27s and 8.80s). Full suites,
updated archives and canonical coverage for this correction have separate
receipts below.

The final save-corrected tree passed 3,192 Windows tests, zero failed/skipped,
in 4m45s (`media-save-windows-full.trx`). Linux passed 3,153, zero failed,
with 39 Windows-only skips in 7m54s (`media-save-linux-full-x11.trx`). The exact
39 skipped case identities match the preceding Linux run; no new case was
hidden by a platform skip. Both runs reached terminal completion before
canonical coverage started. A restricted process inventory returned Access denied;
the approved-host inventory succeeded and found no remaining suite reader or
deferred native build before coverage.

Both packaging scripts rebuilt the corrected production code and updated help
from an isolated 1,364-file snapshot, transfer ZIP SHA256
`92fa6677864ea1a2389b26fabd00f1842e3d8a2727813e403f5f6f0ac4078f81`.
Later review/receipt edits are outside that snapshot; production code and bundled
help are covered. Archive verification again passed 75 byte-equal notice files,
two exact media-guide copies, two unchanged native SQLite modules, checksums,
no PDBs, Linux numeric-zero ownership and regular-file/directory write modes.
The original R33 Windows control archive remained unchanged.

| Development archive | Bytes | SHA256 |
| --- | --- | --- |
| Windows x64 ZIP | 41,081,031 | `cb5e047f77216dfaf264965ea97d8ef82e1182ca5a564bd9e0b8ca619517a822` |
| Linux x64 tar.gz | 42,931,332 | `5163c8660e249b80f58adc537be180bbe6bbac26f2ea632b501335fe1fe0ed76` |

Raw build/test/archive receipts remain in the temporary evidence root named
above; Linux TRX files are under `/var/tmp/hermaeus-r34-linux-tests/`.
The development archives remain `0.41.0-beta`, uninstalled and unpublished.
The final canonical `pwsh ./scripts/coverage.ps1` run passed all 3,192 tests in
6m47s and measured 72.89% line coverage (61,532/84,415), above the 60% floor.
Its stdout is retained as `media-final-coverage.log` in the evidence root; the
canonical script removed its temporary report. No production source changed
after this gate; subsequent edits record receipts and the owner deferral.
No real media model/provider generation, paid request, hardware fit, fresh
packaged Media interaction or owner COSMIC acceptance is established by these
checks. Original R34 native, vault activation and legal release gates remain open.

The owner's subsequent first-party local inference investigation is retained in
[19](19-local-image-inference-decision.md). The owner deferred that work
indefinitely on 2026-10-17, superseding the earlier implementation approval.
Retain the existing optional HTTP integrations; no native worker or inference
integration was added to Hermaeus.

The provider contracts/clients/export batch is `d79000b`; the native workspace,
shared save repair and user documentation batch is `5e2d00b`. Verification above
covers their combined implementation tree, not a separate full run for every
intermediate commit. Packaging and decision records follow as separate batches.
