# 03 - llama.cpp update hygiene

The versioned-directory update path (r11) works, but the owner's log
exposes three lifecycle gaps around it.

Log evidence:
- 2026-07-17 22:39: "llama.cpp updated successfully:
  C:\AI\llama.cpp\b10064\llama-server.EXE".
- 2026-07-18 04:23: "llama.cpp updated successfully:
  C:\AI\llama.cpp\b10064\b10066\llama-server.EXE". Each update nests
  one level deeper, forever.
- 2026-07-16 06:47: "Installation failed: ... ggml-base.dll ... being
  used by another process" (pre-versioned-dir binary; keep as the
  regression case the versioned scheme exists to prevent).
- After both successful updates the running chat/embeddings servers
  kept executing the old binary; nothing suggested a restart.

## 3.1 Stop nesting version directories

`ResolveLlamaServerInstallDirectory` (DoctorService.Runtime.cs:298-310)
returns the directory of the currently configured executable. After
one update that directory is already a version directory
(`...\llama.cpp\b10064`), so `InstallLatestAsync` appends the next tag
beneath it.

Fix: resolve the install root, not the executable's directory. Walk
up from the executable's directory while the leaf name matches the
release-tag pattern (`^b\d+$`, case-insensitive); the first
non-matching ancestor is the root that gets the new tag directory.
Pure static helper with tests.

Acceptance criteria:
- From `C:\AI\llama.cpp\b10064\b10066\llama-server.exe`, the next
  update installs to `C:\AI\llama.cpp\b10068\...`.
- From an unversioned layout (`C:\AI\llama.cpp\llama-server.exe`) the
  behavior is today's: new tag directory alongside the exe.
- Helper tests cover both plus a directory legitimately named like a
  tag containing no llama-server (no false walk-up past the root).

## 3.2 Prune superseded versions

Nothing ever deletes old version directories, and each install is a
multi-hundred-MB extraction. After a successful update and config
swap, list sibling tag directories under the install root that are
neither the new nor the previously configured version and offer
removal in the update flow (single confirm, showing reclaimable size).
Locked files (a server still running an old binary) skip that
directory gracefully; it is offered again next time.

These are app-managed binaries under the install root, never user
data, and nothing is deleted without the confirm.

Acceptance criteria:
- Candidate-selection is a pure function with tests (current and
  previous kept; non-tag directories ignored).
- A locked directory aborts only its own deletion, without failing
  the update.

## 3.3 Restart-to-apply after update

`InstallLlamaServerUpdateAsync` swaps the settings path and saves
(DoctorService.Runtime.cs:286-288) while running servers keep the old
build until someone manually restarts them. The success log even
implies the update is live.

Fix: after a successful update, when managed servers are running,
surface "restart servers to apply bXXXXX" with a one-click restart of
chat and embeddings servers via the existing stop/start machinery.
Never auto-restart while a generation is in flight; if the chat server
is mid-request, queue the prompt until it is idle. The success log
line states the running servers still use the old build until
restarted.

Acceptance criteria:
- Update with servers stopped: no prompt, next start uses the new
  path (today's behavior, now asserted).
- Update with servers running: prompt appears; accepting restarts
  both servers onto the new executable path.
- No restart is triggered while a request is active.

## 3.4 Updates preserve the runtime variant

Once 01/1.1 lands, `InstallLatestAsync` must download the same variant
that is installed or configured, never silently downgrading a
CUDA/Vulkan install to CPU. Ties into the variant-aware
`SelectDownloadAsset`; the update path passes the resolved variant
through.

Acceptance criteria:
- Test: with variant Vulkan configured, the latest-release asset
  chosen is the Vulkan one; Auto re-resolves against current hardware.
