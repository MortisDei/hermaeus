# 05. Chat attachments and artifacts

Owner: chat should accept Word docs / PDFs / images, and a chat model has no way to
produce files - "no workspace/canvas for it to do that in. I feel this is a massive
feature gap."

Current state, verified: `ChatContextAttachment`
(`src/Aether.ViewModels/ChatContextAttachment.cs`) supports text/code extensions only
(:17-24), rejects binary content outright (`LooksBinary`, :152-158), 512 KB per file /
1 MB total, injected as fenced text into the prompt (`BuildPrompt`, :59-84). Chat has
no tool-calling and no output channel other than the message text. The dependency rule
(minimal NuGet, prefer small internal components) shapes everything below; precedent:
`GgufMetadataReader`, `ArchiveExtractor`, the embedded CMUdict.

## 5.1 .docx attachments (no new package)

A .docx is a zip containing `word/document.xml`. Extraction is
`System.IO.Compression.ZipArchive` + `System.Xml` - both already in the BCL.

- New `DocxTextExtractor` in Aether.Services (static, pure): open the zip, read
  `word/document.xml`, walk `w:p` paragraphs / `w:t` text runs / `w:tab` / `w:br`,
  emit plain text with paragraph breaks. Tables: emit rows as tab-separated lines.
  Ignore everything else (headers, footnotes, images) in v1.
  Guard rails: reject archives whose `document.xml` entry reports over 8 MB
  uncompressed (decompression bomb), catch malformed XML/zip into a Skipped status.
- Wire into `ChatContextAttachment.LoadOneAsync`: add `.docx` to a new
  "extract-then-attach" branch that bypasses `LooksBinary` (which would always reject
  it) and stores the EXTRACTED text as `Content` with `SizeBytes` = extracted-text
  byte count for budget math (the raw file size is meaningless to the prompt budget).
  Raise nothing else; the existing per-file/total caps then apply to the text.
- Add `*.docx` to the file picker filter in `ChatView.axaml.cs:75-84` and to drag/drop.

Acceptance: unit tests build a minimal docx in-memory (ZipArchive + hand-written
document.xml, no fixture binaries in the repo) covering paragraphs, a table, and a
malformed zip -> Skipped with reason.

## 5.2 .pdf attachments (bounded internal extractor, honest about limits)

PDF text extraction without a library is legitimately hard; ship a best-effort
extractor that handles the common machine-generated case and REFUSES clearly
otherwise, rather than pretending full support.

- New `PdfTextExtractor` in Aether.Services: parse the xref-less way (scan for stream
  objects), inflate FlateDecode streams via `System.IO.Compression.DeflateStream`
  (skip the 2-byte zlib header), tokenize content streams for `Tj`/`TJ`/`'`/`"` text
  operators, decode literal `(...)` strings with standard escapes and hex `<...>`
  strings as Latin-1. No font/ToUnicode CMap handling in v1: when the extracted text
  ratio of printable characters is below a threshold or empty, return a structured
  "could not extract text (likely scanned or uses embedded font encodings)" result.
- Same wiring as 5.1: Skipped status carries that message so the user learns WHY a
  given PDF did not attach. Cap input at 20 MB file / 2 MB extracted text.
- Do NOT try OCR, images, or encrypted PDFs; say so in `docs/features.md`.

Acceptance: unit tests with a tiny programmatically-written PDF (uncompressed content
stream with `BT (Hello) Tj ET`, buildable as a string), a Flate-compressed variant
(compress in the test), and a garbage file -> Skipped. The threshold logic gets a
direct test.

## 5.3 Image attachments for vision models (mmproj)

llama-server supports multimodal chat when launched with a `--mmproj` projector file,
accepting OpenAI-style `image_url` content parts with data URIs. Aether already knows
these files exist (r18 excluded `mmproj-*.gguf` from the model list); the owner's
models folder contains them. Scope: local llama.cpp managed servers only.

- `ServerConfig.MmprojPath` (additive, default empty). Services card: an optional
  "Vision projector (mmproj)" picker beside the model row, auto-suggesting an
  `mmproj-*.gguf` sitting in the same directory as the selected model.
  `BuildLaunchArguments` appends `--mmproj <path>` when set.
- `ChatMessage` grows an optional `Images` list (record of file path + media type;
  additive, JSON round-trip safe for persistence). `ChatContextAttachment` accepts
  `.png .jpg .jpeg .webp` (up to 8 MB each, max 4 per send) as a new Image status kind
  that does NOT count against the text budget and renders a thumbnail chip.
- `OpenAiCompatibleToolWire.BuildMessages` (shared payload builder): when a message
  has images, emit `content` as an array of `{type:"text"}` + `{type:"image_url",
  image_url:{url:"data:<media>;base64,<...>"}}` parts; unchanged plain-string content
  otherwise, so non-vision paths are byte-identical.
- Honesty gate: if the active model's server has no `MmprojPath`, attaching an image
  marks it Skipped with "This server has no vision projector configured (Services >
  Vision projector)". No silent text-only degradation.
- Privacy: images ride the same localhost/remote path as text; the existing remote
  disclosure covers them, but add images to the Privacy Audit wording for remote chat
  providers. OpenAI provider support may ride the same content-part builder for free;
  verify, and if it works, allow it; if not, keep the gate local-only this round.

Acceptance: payload test asserting the exact content-part JSON for one text + one
image message and plain string without images; gate test for the Skipped path;
launch-args test for `--mmproj`.

## 5.4 Chat artifacts: let output become files

The other half of the gap: the model writes code/documents and the user has no way to
get a file out except copy-paste. Full chat-side tool-calling stays out (that is the
Agent's job and its approval gates exist for a reason); what chat needs is a
low-ceremony artifacts surface:

- Per-conversation artifacts folder: `{DataRoot}/chat-artifacts/{conversationId}/`,
  created lazily. Managed through a small `ChatArtifactService` (Aether.Services):
  `SaveAsync(conversationId, suggestedFileName, content)` with filename sanitization
  (strip path separators, reject traversal, dedupe with ` (2)` suffixes), atomic
  write, and a `ListAsync(conversationId)`.
- Every fenced code block in a rendered assistant message gets a small "Save" button
  in the existing code-block header row in `MarkdownViewer` (language label is already
  rendered there, `CodeBorder`, `MarkdownViewer.cs:396-409`). Default filename:
  derived from the fence language (`.cs`, `.py`, `.md`, `.txt` fallback) plus a stem
  from the first heading or the conversation title. Saving drops it into the
  conversation's artifacts folder and toasts the path; a modifier (or a second
  "Save as...") opens the OS save dialog instead.
- An "Artifacts" strip above the input bar (collapsed pill like the memory pill,
  "Artifacts: N") listing saved files with Open and Reveal-in-folder actions, plus an
  Open-folder button. Strip populates from `ListAsync` on conversation switch.
- Explicitly rejected this round: model-initiated file writes from chat (no chat tool
  loop), arbitrary destination writes without a dialog, and image/binary artifact
  generation (a local LLM cannot emit binaries through text anyway).

Acceptance: service tests for sanitization (traversal attempt rejected, duplicate
names deduped, atomic write leaves no temp file), and a VM test that saving a block
adds it to the strip for the right conversation only. Security review note required
(new user-controlled filename -> fixed sandbox folder).
