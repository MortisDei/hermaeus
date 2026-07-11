# Code Audit (r2)

Concrete findings in code that landed since r1. Each item has: severity,
the defect, why it matters, and acceptance criteria. File references are
to the tree at 0.9.40-alpha (commit 85f4333). An implementing agent should
work top to bottom; items are independent unless noted.

Severity scale: **P1** user-visible wrongness or a trust/docs-truth
violation; **P2** real defect with a plausible trigger; **P3** hardening
or hygiene worth doing while in the file.

---

## P1-1. `LocalApiSettings.Enabled` is a phantom toggle

`src/Aether.Core/Models/LocalApiSettings.cs` documents: "The desktop app
only launches the Aether.LocalApi child process when this is explicitly
turned on." **No code anywhere launches that child process.** A repo-wide
search for `Aether.LocalApi` in `Aether.Desktop`/`Aether.Services` finds
only the settings view; `Aether.LocalApi/Program.cs` never checks
`Enabled` either, so running the exe manually serves requests even with
the toggle off. Today the checkbox in Settings > Local API does nothing.

This is exactly the class of defect r1 flagged as most corrosive to trust
(the memory-encryption phantom toggle, removed in 0.9.x): a security-shaped
setting that does not do what it says.

Fix (pick one, the first is recommended):

- Make it real: a `LocalApiProcessManager` in `Aether.Services` (modeled on
  `XttsProcessManager`/`KokoroProcessManager`) that starts the
  `Aether.LocalApi` executable as a child process when `Enabled` is true at
  startup or when the setting flips on, stops it when it flips off, and stops
  it on shutdown (register in `SettingsViewModel.Shutdown()` alongside the
  other two, see the 0.9.38 fix). Additionally, `Program.cs` in
  `Aether.LocalApi` should refuse to serve (log and exit nonzero) when
  `Enabled` is false, so a stray manual launch cannot bypass the setting.
- Or make it honest: remove the toggle, document the host as manually run,
  and have `Program.cs` state that posture.

Acceptance: toggling the setting on gives a live host on the configured
port; toggling off (or app exit) terminates it; a manual launch with
`Enabled: false` refuses to serve; docs (`docs/features.md`) match.

## P1-2. Local API trace `DetailJson` is built by string interpolation of caller-controlled input

`src/Aether.LocalApi/LocalApiEndpoints.cs:159`:

```csharp
DetailJson = $$"""{"client":"{{client}}","datasetId":"{{datasetId ?? string.Empty}}"}"""
```

`client` comes verbatim from the `X-Aether-Client` request header. Any
quote, backslash, or control character produces malformed JSON in the trace
store, and a crafted header can inject arbitrary JSON fields into a record
that Privacy Audit later parses and displays. The same endpoint also stores
the raw header as `SourceId` with no length cap.

Fix: build `DetailJson` with `JsonSerializer.Serialize(new { client, datasetId })`;
in `CallerName`, trim, strip control characters, and cap length (64 chars is
plenty for an app name). Acceptance: a header containing `"},{"x":"y` round-trips
as data, not structure; a 10 KB header is truncated; existing LocalApiTests extended
to cover both.

## P2-1. `McpClient.CallToolAsync` stringifies every argument

`src/Aether.Mcp/McpClient.cs:94`:

```csharp
argsNode[key] = value is null ? null : JsonValue.Create(value.ToString());
```

Numbers, booleans, and nested objects all become JSON strings. Any MCP
server whose tool schema declares `"type": "integer"` or `"type": "boolean"`
receives the wrong JSON type and will (correctly) reject or misbehave. A
`JsonElement` value becomes its `ToString()` text, so nested objects arrive
double-encoded.

Fix: map by runtime type: pass through `JsonElement`/`JsonNode` as nodes,
`bool`/numeric types via the typed `JsonValue.Create` overloads, strings as
strings, and fall back to `JsonSerializer.SerializeToNode(value)` for the
rest. Acceptance: a fake stdio server (the existing `McpTests` in-memory
harness) asserts an int argument arrives as a JSON number and a dictionary
argument arrives as a JSON object.

## P2-2. `McpClient` never drains the server's stderr

`McpClient.Start` sets `RedirectStandardError = true` but nothing ever reads
`process.StandardError`. A server that logs more than the OS pipe buffer
(~4 KB) to stderr blocks on its next stderr write and hangs; every
outstanding `tools/call` then dies on the 30-second timeout with no clue why.
Chatty logging on stderr is normal for Node/Python MCP servers.

Fix: after `Process.Start`, kick off a background read of `StandardError`
(discard, or better: retain the last N KB in a ring buffer and include it in
the timeout exception message). Acceptance: a fake server that writes 1 MB
to stderr before responding still completes a `tools/call`.

## P2-3. `McpClient` pending requests hang for the full timeout when the server dies

If the child process exits (crash, bad command), the read loop sees EOF and
returns, but in-flight and future `SendRequestAsync` calls sit until the
30-second timeout. Fix: when the read loop exits (EOF or exception), fault all
`_pending` entries with "MCP server closed the connection" and mark the client
dead so subsequent sends fail fast. Acceptance: killing the fake server
fails a pending call in well under a second with a message naming the server exit,
not a generic timeout.

## P2-4. `InspectGitDiff` can deadlock-then-timeout on large output

`src/Aether.Agent/Services/AgentToolExecutor.cs:133-166` starts `git status
--short` with redirected stdout, calls `WaitForExit(3000)`, and only reads
the streams afterwards. If the status output exceeds the pipe buffer, git
blocks writing, the wait expires, and the tool falsely reports "git status
timed out". Large working trees hit this. Fix: start `ReadToEndAsync` on both
streams before waiting (the pattern `RunCommandAsync` in the same file
already uses). Acceptance: a repo with thousands of modified files returns
its status rather than a timeout.

## P2-5. `ApplyDraftPatch` writes user files non-atomically

`src/Aether.Agent/Services/AgentWorkspaceTools.cs:113` uses
`File.WriteAllText` to write an approved patch into the user's workspace.
A crash or disk-full mid-write leaves the user's file truncated. The project
principle is atomic replacement for state files, and `AtomicFileWriter`
already exists in this very project (`src/Aether.Agent/Services/AtomicFileWriter.cs`).
This is the one write path in the app that touches files the user did not
back up (their own source code); it deserves the strongest write discipline
in the codebase, not the weakest. Fix: route through `AtomicFileWriter`
(temp in same directory + `File.Move(overwrite: true)`). Acceptance: covered
by a test that the target is either old content or new content, never partial.

## P2-6. Native Kokoro model download buffers the whole file in memory

`src/Aether.Voice/KokoroOnnxModel.cs:210`: `_http.GetAsync(url, ct)` without
`HttpCompletionOption.ResponseHeadersRead` buffers the entire response
(the model is a few hundred MB) into memory before `CopyToAsync` streams it
to disk. On low-RAM machines this can OOM the very machines most likely to
want the quantized model. Fix: pass `HttpCompletionOption.ResponseHeadersRead`.
One line. Acceptance: install still works; peak memory during download stays
flat (manual observation is fine).

## P2-7. Native Kokoro synthesis runs ONNX inference on the caller's thread

`NativeKokoroVoiceProvider.RenderToFileAsync`
(`src/Aether.Voice/NativeKokoroVoiceProvider.cs:166-201`) awaits the gate,
then runs phonemize + `_model.Synthesize(...)` chunk loop synchronously.
`ITtsService.SpeakAsync` is invoked from ViewModel commands; on the UI
thread, a long paragraph freezes the app for the duration of inference
(seconds, single-threaded by design after the 0.9.40 crash mitigation,
which makes it slower still). Fix: wrap the phonemize/synthesize/WAV-write
block in `Task.Run`. Keep the `SemaphoreSlim` gate outside so ordering is
preserved. Acceptance: UI remains responsive while a long text is spoken.

## P2-8. Local secret-store key file is written non-atomically and is briefly world-readable

`src/Aether.Services/SecretStore.cs:169-172` (`GetOrCreateLocalKeyMaterial`):

```csharp
var key = RandomNumberGenerator.GetBytes(32);
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
File.WriteAllText(path, Convert.ToBase64String(key));
TryRestrictPermissions(path);
```

Two problems with the single most sensitive file in the data root
(`secrets.local.key`, the AES key material protecting every fallback-stored
secret):

- On Linux the file is created with default umask permissions (typically
  world-readable) and only restricted to 600 *afterwards*; there is a window
  where another local user can read the key. The file's own
  `WriteTextAtomicAsync` (line 126) already does this correctly for
  `secrets.local.json` (restrict the temp file, then move); the key write
  just does not use it.
- The write is non-atomic. A crash mid-write leaves a truncated key file;
  every previously encrypted secret becomes silently unrecoverable (see
  P3-7 for why silently).

Fix: write the key through the same `WriteTextAtomicAsync` +
`TryRestrictPermissions`-before-move path used for the store itself.
Acceptance: on Linux, at no observable point does `secrets.local.key`
exist with permissions wider than 600; the write is temp + move.

## P3-1. Local API chat endpoint silently ignores the new sampling parameters

0.9.39 added top P/top K/min P/repeat/frequency/presence penalties across
global, per-model, and per-conversation surfaces, but
`LocalApiEndpoints` (`src/Aether.LocalApi/LocalApiEndpoints.cs:41-45`) maps only
`Temperature` and `MaxTokens`, and does not apply per-model profile defaults
either. An API caller gets different output than the desktop app for the
same model. Fix: accept the six optional fields on `ChatCompletionRequest`
and pass them through; apply `ModelProfileService` per-model defaults the
same way `ChatSendOrchestrator` does. Acceptance: parity test comparing the
options record built by the endpoint vs the orchestrator for the same inputs.

## P3-2. Local API responses do not stream

`/v1/chat/completions` buffers the full completion before responding. For
the "editors and scripts reuse Aether" use case, time-to-first-token is the
difference between usable and not. Fix in the roadmap pass (03, item H1):
add `"stream": true` support producing SSE in the OpenAI wire shape, since
callers already speak it. Not a defect; noted here for completeness.

## P3-3. `run_command` recipe matching is case-sensitive in the executor, case-insensitive in the gate

`AgentSafetyGate.EvaluateCommand` matches the workspace-declared recipe
with `OrdinalIgnoreCase` (`src/Aether.Agent/Services/AgentSafetyGate.cs:69`),
while `AgentToolExecutor.RunCommandAsync` does an exact
`WorkspaceCommandRecipes.Executable.TryGetValue(trimmed, ...)` lookup
(`src/Aether.Agent/Services/AgentToolExecutor.cs:97`). A command that
differs only in case passes the gate and then throws in the executor. The
failure is safe (fail-closed) but the error surfaces as an execution fault
rather than a policy decision. Fix: normalize once (both consult
`WorkspaceCommandRecipes.Executable` with the same comparer). Acceptance:
`"DOTNET TEST"` produces the same decision at both layers.

## P3-4. Empty catch sweep

45 `catch { }` blocks exist under `src/`. Most guard genuinely best-effort
paths (trace writes, temp-file deletes, process kills) and are fine. Sweep
them once and (a) add the standard `// best-effort: <why>` comment where
intentional, (b) route through `IRuntimeLogService` where a user would want
to know (the 0.9.37 changelog already fixed one such case where RAG
service-restore errors were silently discarded; there are likely one or two
more of that shape). Acceptance: every empty catch either has a
justification comment or logs.

## P3-5. `ParseSources` regex without a timeout

`src/Aether.LocalApi/LocalApiEndpoints.cs:172` runs an unanchored
`(.+)` regex over a stream token. Input is produced by our own
`RagQueryService`, so risk is low; still, prefer `IndexOf`-based slicing
(the sentinel format is fixed), which also removes the
`System.Text.RegularExpressions` import from this file. Note
`Aether.Rag.RagStreamProtocol` already exists and was built exactly to own
this parsing (0.9.32); the local API should call it instead of keeping a
third private copy of the sentinel protocol.

## P3-6. macOS keychain backend passes the secret on the command line

`SecretStore.MacOsKeychainBackend.StoreAsync`
(`src/Aether.Services/SecretStore.cs:334-341`) invokes
`security add-generic-password ... -w <secret>`. Process arguments are
visible to other local processes (`ps`) for the lifetime of the command, so
the secret leaks to anyone who can list processes at the right moment. The
Linux backend already does this correctly (secret via stdin to
`secret-tool store`). Note AGENTS.md scopes the product to Windows and
Linux, so this is latent rather than shipping-critical, but the backend
exists and runs if anyone builds on macOS. Fix: use the `-w` prompt-free
stdin form (`security add-generic-password` reads the password from stdin
when `-w` is given without a value via `security -i` interactive mode), or
document the limitation and prefer the encrypted-file fallback on macOS.

## P3-7. Decryption failure silently degrades to garbage-or-empty

`SecretStore.DecryptSecret` (`src/Aether.Services/SecretStore.cs:183-210`):
any decryption failure (corrupt key file, wrong key, tampered payload)
falls through to "interpret the ciphertext as plain Base64 UTF-8" and, if
that also fails, returns `string.Empty`. The legacy-migration intent is
fine, but the effect is that a corrupted `secrets.local.key` manifests as
providers mysteriously failing auth with no error anywhere. Fix: keep the
legacy fallback, but when *both* paths fail, log one redacted warning
through `IRuntimeLogService` ("stored secret '<name>' could not be
decrypted; the local key file may be corrupt") instead of returning empty
silently. Pairs with P2-8; also a natural place for a Doctor check
(key file exists, is valid Base64, decrypts a self-test sentinel secret).

---

## Explicitly checked, no action needed

- `LocalApiTokenAuth`: fails closed with 503 when unconfigured, constant-time
  comparison including the length-mismatch path, loopback-only binding with
  port validation. Good.
- `AgentWorkspaceTools.ResolveSafePath`: rooted-path rejection, prefix check
  with platform-correct comparison, ignored-directory filtering, symlink
  ancestor walk. Good. (TOCTOU between check and use is accepted; a local
  attacker who can race symlinks already owns the account.)
- `run_command` policy layering: fixed hardcoded allowlist AND
  workspace-declared recipe AND always-approval. Matches the roadmap's
  stated design.
- `KokoroOnnxModel` asset posture: SHA256-pinned, verify-before-load,
  download only from explicit install, temp + move. Matches the reranker
  pattern as claimed.
- Test coverage for the new subsystems exists (`LocalApiTests`, `McpTests`,
  `VoiceTests`, `TtsTests`, architecture tests extended for the MCP bridge
  seam). The audit items above should each land with a test in the matching
  file.
