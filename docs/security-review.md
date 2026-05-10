# Aether Security Review

## Current Hardening

- Managed `llama-server` binds to `127.0.0.1` by default.
- Process arguments use `ProcessStartInfo.ArgumentList`, avoiding shell command
  interpolation for normal launch options.
- Extra args are split locally and passed as arguments, not executed through a
  shell.
- TTS audio is kept in memory and piped to an audio player; Aether does not keep
  generated WAV files.
- Alternate data roots migrate the SQLite data files instead of leaving old chat
  databases behind.

## Required Next Checks

| Area | Risk | Action |
| --- | --- | --- |
| Data root selection | Accidental overwrite or unsafe path | Refuse existing target DB unless import/merge is explicit |
| API keys | Secrets stored in JSON settings | Move secrets to OS credential store or encrypted local vault |
| Extra args | User can pass unsafe server flags | Add warning badges for host/network flags and env-changing args |
| Remote endpoints | User can point at untrusted APIs | Show trust level and disable secret forwarding by default |
| RAG ingest | Huge/binary/untrusted files | Size limits, file type allowlist, HTML/script stripping |
| Logs | Paths/API errors may expose secrets | Redact API keys/tokens and home path segments in visible logs |
| Backups | Data loss during relocation | Create `.bak` before migration or before destructive compaction |
| Permissions | Multi-user machine privacy | Prefer user-only data dir permissions on Linux/Windows |

## Test Targets

- Data migration preserves `conversations.db`, `-wal`, and `-shm`.
- Migration refuses to overwrite an existing destination DB.
- Server launch never goes through a shell.
- `--host 0.0.0.0` in extra args is surfaced as a warning before start.
- RAG ingest rejects oversized files and unsupported extensions.
- Logs redact `sk-...` style keys.
