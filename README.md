# Aether

> A lean, next-gen MVVM desktop chat client for llama.cpp and OpenAI-compatible APIs.
> Built with Avalonia UI + .NET 10. Linux (Wayland/X11) and Windows. System dark/light theme.

## Features

- **Multi-backend** — llama.cpp (local, via llama-server) + any OpenAI-compatible endpoint (remote)
- **Streaming** — real-time token streaming via IAsyncEnumerable
- **Conversation history** — SQLite, auto-titled, full-text search
- **Markdown rendering** — native Avalonia renderer (Markdig parser, no WebView)
- **Server management** — launch/stop llama-server, auto-discovery, model file browser
- **MVVM** — CommunityToolkit.Mvvm, no code-behind logic leakage
- **System theme** — Avalonia FluentTheme, follows OS dark/light automatically
- **Wayland** — Avalonia UsePlatformDetect() handles Wayland/X11 transparently

## Quick Start

```bash
# Prerequisites: .NET 10 SDK, llama.cpp (llama-server binary)
# 1. Build llama.cpp from https://github.com/ggerganov/llama.cpp
#    or download pre-built: https://github.com/ggerganov/llama.cpp/releases
# 2. Ensure 'llama-server' is in PATH or set path in Services tab

git clone <your-repo>
cd Aether
dotnet run --project src/Aether.Desktop

# Or use the Services tab to launch llama-server with your model
```

## Build

```bash
# Linux
./build.sh

# Windows
.\build.ps1

# Manual
dotnet publish src/Aether.Desktop -c Release -r linux-x64 --self-contained false -o dist/linux
dotnet publish src/Aether.Desktop -c Release -r win-x64   --self-contained false -o dist/windows
```

## Project Structure

```
Aether.Core/        Models + service interfaces
Aether.Services/    llama.cpp, OpenAI, SQLite, Server management implementations
Aether.ViewModels/  MVVM ViewModels (CommunityToolkit)
Aether.Desktop/     Avalonia views, controls, styles, entry point
```

## Data Locations

| Platform | Path |
|----------|------|
| Linux    | `~/.local/share/Aether/` |
| Windows  | `%LOCALAPPDATA%\Aether\` |

## Configuration

### llama.cpp (Chat)
- Default: `http://localhost:8080` (manage via Services tab)
- Uses OpenAI-compatible `/v1/chat/completions` API
- Auto-discovery of `llama-server` in PATH
- Manual model file browser in Settings

### OpenAI / Compatible API
- Settings → enter Base URL + API key
- Routes model names starting with `gpt`, `o1`, `o3`, `o4` to OpenAI
- Other models use llama.cpp backend

### RAG / Embeddings
- Separate llama-server instance (default port 8081)
- Configured via Services → Embeddings tab
- Model selection in Settings

## Roadmap

- [ ] Image generation (DALL-E, Stable Diffusion)
- [ ] Voice input/output
- [ ] Plugin / tool-call system
- [ ] Conversation export (Markdown, JSON)
- [ ] Per-conversation model parameters
- [ ] Prompt templates library
