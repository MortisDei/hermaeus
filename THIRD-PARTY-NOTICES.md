# Third-Party Notices

Hermaeus itself is licensed under the PolyForm Noncommercial License 1.0.0
(see `LICENSE.md`). Everything listed here belongs to somebody else and is
governed by its own licence, not by Hermaeus's.

The list is split by **what Hermaeus actually does with each thing**, because
the obligations are different:

| Section | What it means |
| --- | --- |
| [1. Bundled](#1-bundled-with-hermaeus) | Ships inside the Hermaeus installer/build. Hermaeus redistributes this code. |
| [2. Native libraries](#2-native-libraries-inside-those-packages) | Native code carried inside the packages in section 1, and therefore also redistributed. |
| [3. Bundled data](#3-bundled-data) | Non-code assets that ship in the build. |
| [4. Downloaded on your machine](#4-downloaded-on-your-machine-at-your-request) | Hermaeus fetches these at your request and runs them locally. Hermaeus does **not** redistribute them; you obtain them from the publisher. |
| [5. Model weights](#5-model-weights-hermaeus-offers-to-download) | Weights Hermaeus offers to download. Same as section 4: not redistributed, and each carries the publisher's own licence, some of which restrict use. |
| [6. Services](#6-services-hermaeus-talks-to) | Remote services Hermaeus calls. |
| [7. Development only](#7-development-only-not-shipped) | Build and test tooling that never ships. |

Maintenance: section 1 is checked by `ThirdPartyNoticeGuardTests`, which fails
the build if a package in the shipped dependency closure has no entry here.
Sections 2 to 7 are maintained by hand; add to them in the same change that
adds the dependency.

---

## 1. Bundled with Hermaeus

NuGet packages in the resolved dependency closure of `Hermaeus.Desktop` and
`Hermaeus.LocalApi`. Licence and copyright are as declared in each package's
own `.nuspec`.

| Package | Version | Licence | Copyright |
| --- | --- | --- | --- |
| Avalonia | 12.1.2 | MIT | Copyright 2013-2026 (c) The AvaloniaUI Project |
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 | BSD-3-Clause (ANGLE) | Copyright 2013-2026 (c) The AvaloniaUI Project |
| Avalonia.AvaloniaEdit | 12.0.0 | MIT | Copyright 2017-2026 (c) The AvaloniaUI Project |
| Avalonia.BuildServices | 11.3.2 | MIT | Copyright 2023-2025 (c) The AvaloniaUI Project |
| Avalonia.Desktop | 12.1.2 | MIT | Copyright 2013-2026 (c) The AvaloniaUI Project |
| Avalonia.Fonts.Inter | 12.1.2 | MIT (package); the Inter font itself is SIL OFL 1.1, see section 3 | Copyright 2013-2026 (c) The AvaloniaUI Project |
| Avalonia.FreeDesktop | 12.1.2 | MIT | Copyright 2013-2026 (c) The AvaloniaUI Project |
| Avalonia.FreeDesktop.AtSpi | 12.1.2 | MIT | Copyright 2013-2026 (c) The AvaloniaUI Project |
| Avalonia.HarfBuzz | 12.1.2 | MIT | Copyright 2013-2026 (c) The AvaloniaUI Project |
| Avalonia.Native | 12.1.2 | MIT | Copyright 2013-2026 (c) The AvaloniaUI Project |
| Avalonia.Remote.Protocol | 12.1.2 | MIT | Copyright 2013-2026 (c) The AvaloniaUI Project |
| Avalonia.Skia | 12.1.2 | MIT | Copyright 2013-2026 (c) The AvaloniaUI Project |
| Avalonia.Themes.Fluent | 12.1.2 | MIT | Copyright 2013-2026 (c) The AvaloniaUI Project |
| Avalonia.Win32 | 12.1.2 | MIT | Copyright 2013-2026 (c) The AvaloniaUI Project |
| Avalonia.X11 | 12.1.2 | MIT | Copyright 2013-2026 (c) The AvaloniaUI Project |
| CommunityToolkit.Mvvm | 8.3.2 | MIT | (c) .NET Foundation and Contributors |
| Google.Protobuf | 3.30.2 | BSD-3-Clause | Copyright 2015, Google Inc. |
| HarfBuzzSharp | 8.3.1.3 | MIT | (c) Microsoft Corporation |
| HarfBuzzSharp.NativeAssets.Linux | 8.3.1.3 | MIT | (c) Microsoft Corporation |
| HarfBuzzSharp.NativeAssets.macOS | 8.3.1.3 | MIT | (c) Microsoft Corporation |
| HarfBuzzSharp.NativeAssets.WebAssembly | 8.3.1.3 | MIT | (c) Microsoft Corporation |
| HarfBuzzSharp.NativeAssets.Win32 | 8.3.1.3 | MIT | (c) Microsoft Corporation |
| Markdig | 0.38.0 | BSD-2-Clause | Alexandre Mutel |
| MicroCom.Runtime | 0.11.6 | MIT | Copyright 2021 (c) Nikita Tsukanov |
| Microsoft.Data.Sqlite | 9.0.9 | MIT | (c) Microsoft Corporation |
| Microsoft.Data.Sqlite.Core | 9.0.9 | MIT | (c) Microsoft Corporation |
| Microsoft.Extensions.DependencyInjection | 9.0.4 | MIT | (c) Microsoft Corporation |
| Microsoft.Extensions.DependencyInjection.Abstractions | 9.0.4 | MIT | (c) Microsoft Corporation |
| Microsoft.ML.OnnxRuntime | 1.25.1 | MIT | (c) Microsoft Corporation |
| Microsoft.ML.OnnxRuntime.Managed | 1.25.1 | MIT | (c) Microsoft Corporation |
| Microsoft.ML.Tokenizers | 2.0.0 | MIT | (c) Microsoft Corporation |
| PdfPig | 0.1.14 | Apache-2.0 | The PdfPig authors |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.0 | Apache-2.0 | Copyright 2014-2025 SourceGear, LLC |
| SQLitePCLRaw.config.e_sqlite3 | 3.0.0 | Apache-2.0 | Copyright 2014-2025 SourceGear, LLC |
| SQLitePCLRaw.core | 3.0.0 | Apache-2.0 | Copyright 2014-2025 SourceGear, LLC |
| SQLitePCLRaw.lib.e_sqlite3 | 3.50.3 | Apache-2.0 | Copyright 2014-2025 SourceGear, LLC |
| SQLitePCLRaw.provider.e_sqlite3 | 3.0.0 | Apache-2.0 | Copyright 2014-2025 SourceGear, LLC |
| SkiaSharp | 3.119.4 | MIT | (c) Microsoft Corporation |
| SkiaSharp.NativeAssets.Linux | 3.119.4 | MIT | (c) Microsoft Corporation |
| SkiaSharp.NativeAssets.macOS | 3.119.4 | MIT | (c) Microsoft Corporation |
| SkiaSharp.NativeAssets.WebAssembly | 3.119.4 | MIT | (c) Microsoft Corporation |
| SkiaSharp.NativeAssets.Win32 | 3.119.4 | MIT | (c) Microsoft Corporation |
| System.Numerics.Tensors | 9.0.9 | MIT | (c) Microsoft Corporation |
| Tmds.DBus.Protocol | 0.94.1 | MIT | Tom Deseyn |

Apache-2.0 requires that its NOTICE text, where the upstream project ships
one, travels with redistributions. The Apache-2.0 packages above
(SQLitePCLRaw, PdfPig) ship their licence files inside the package, which is
carried into the build output.

## 2. Native libraries inside those packages

These are redistributed as part of section 1 and carry their own upstream
licences.

| Library | Carried by | Licence | Copyright |
| --- | --- | --- | --- |
| Skia | SkiaSharp.NativeAssets.* | BSD-3-Clause | Copyright (c) 2011 Google Inc. |
| ANGLE | Avalonia.Angle.Windows.Natives | BSD-3-Clause | Copyright 2018 The ANGLE Project Authors |
| HarfBuzz | HarfBuzzSharp.NativeAssets.* | MIT ("Old MIT") | Copyright (c) the HarfBuzz authors and contributors |
| SQLite (`e_sqlite3`) | SQLitePCLRaw.lib.e_sqlite3 | Public domain | The SQLite authors dedicate SQLite to the public domain |
| ONNX Runtime | Microsoft.ML.OnnxRuntime | MIT | (c) Microsoft Corporation |
| Protocol Buffers | Google.Protobuf | BSD-3-Clause | Copyright 2015, Google Inc. |

## 3. Bundled data

### CMU Pronouncing Dictionary (cmudict)

Used by `Hermaeus.Voice` (`src/Hermaeus.Voice/Assets/cmudict.txt.gz`) as the
primary word-to-pronunciation lexicon for native Kokoro text-to-speech.

Copyright (C) 1993-2015 Carnegie Mellon University. All rights reserved.
Full licence text: `src/Hermaeus.Voice/Assets/CMUDICT-LICENSE.txt`.
Source: https://github.com/cmusphinx/cmudict

### Inter (typeface)

Embedded in the `Avalonia.Fonts.Inter` package and used as the application
font. The package is MIT; the typeface is licensed separately under the SIL
Open Font License 1.1, which permits bundling and redistribution with software
and requires that the font not be sold on its own.

Copyright (c) 2016 The Inter Project Authors.
Licence: https://openfontlicense.org
Source: https://github.com/rsms/inter

## 4. Downloaded on your machine, at your request

Hermaeus does not redistribute any of these. It fetches them from the
publisher, to your machine, when you ask it to, and verifies a pinned SHA256
before trusting anything it downloaded. They remain governed by their
publisher's licence.

### llama.cpp (`llama-server`)

Downloaded from the project's own GitHub release assets and run as a local
child process. Hermaeus never modifies or redistributes the binaries.

MIT License. Copyright (c) 2023-2026 The ggml authors.
Source: https://github.com/ggml-org/llama.cpp

### NVIDIA CUDA runtime (`cudart`)

When a CUDA build of `llama-server` is selected, Hermaeus also fetches the
matching `cudart-*` companion archive published alongside that llama.cpp
release. **This archive contains NVIDIA's redistributable CUDA runtime
libraries and is not open source.** It is governed by the NVIDIA Software
License Agreement and the CUDA Supplement to it, not by llama.cpp's MIT
licence. Hermaeus does not redistribute it; it is downloaded to your machine
from the same release you chose.

Licence: https://docs.nvidia.com/cuda/eula/index.html

### Ollama and OpenAI-compatible endpoints

Hermaeus talks to these over HTTP if you configure them. It does not download,
bundle or install them. Ollama is MIT (https://github.com/ollama/ollama).

### Python voice backends (XTTS v2, F5-TTS, Kokoro server)

Optional, and only if you choose one. Hermaeus creates a virtual environment
and installs the packages you selected from PyPI; it ships none of them and
redistributes none of them. Their licences are their own, and some model
weights used by these backends are **not** permissively licensed. Read the
provider's terms before relying on one commercially.

## 5. Model weights Hermaeus offers to download

Not redistributed. Hermaeus downloads them from Hugging Face at your request
and verifies a pinned SHA256. Each is governed by its publisher's licence. The
setup wizard shows the licence of each starter model before you download it.

### Starter chat models (setup wizard)

| Model | GGUF publisher | Licence |
| --- | --- | --- |
| Phi-4 mini Instruct | unsloth | MIT |
| Gemma 4 E2B IT QAT | unsloth | Apache-2.0 |
| Gemma 4 E4B IT QAT | unsloth | Apache-2.0 |
| Qwen3 8B | Qwen | Apache-2.0 |
| Qwen3 14B | Qwen | Apache-2.0 |

### Other model assets

| Asset | Used for | Licence |
| --- | --- | --- |
| `onnx-community/Kokoro-82M-v1.0-ONNX` | Native Kokoro text-to-speech | Apache-2.0 (converted from `hexgrad/Kokoro-82M`) |
| `onnx-community/whisper-base` | Local speech recognition | Converted from `openai/whisper-base`, MIT |
| `nomic-ai/nomic-embed-text-v1.5-GGUF` | Doctor's suggested embedding model | Apache-2.0 |
| `bartowski/microsoft_Phi-4-mini-reasoning-GGUF` | Local AI setup's suggested model | MIT (base model `microsoft/Phi-4-mini-reasoning`) |

Any other model is one **you** chose, through the Hugging Face browser or by
pointing Hermaeus at a file on disk. Hermaeus does not vet or relicense it, and
the publisher's terms apply.

## 6. Services Hermaeus talks to

### Hugging Face Hub

Hermaeus uses the public Hugging Face Hub API to search for models, read model
cards, resolve download URLs and check for updates. Use of the Hub is subject
to Hugging Face's Terms of Service (https://huggingface.co/terms-of-service).

"Hugging Face" and the Hugging Face logo are trademarks of Hugging Face, Inc.
Hermaeus is not affiliated with, endorsed by, or sponsored by Hugging Face.
The app deliberately does **not** reproduce the Hugging Face logo; the model
card's source badge uses a generic glyph and the letters "HF" for exactly this
reason.

### GitHub

Release metadata and release assets for llama.cpp and for Hermaeus's own
update check are fetched from GitHub's public API.

## 7. Development only, not shipped

Used to build or test Hermaeus; never redistributed in a Hermaeus release.

| Package | Licence |
| --- | --- |
| xunit, xunit.runner.visualstudio | Apache-2.0 |
| Microsoft.NET.Test.Sdk | MIT |
| Microsoft.AspNetCore.TestHost | MIT |
| coverlet.collector | MIT |
| JsonSchema.Net (`src/Tools/TraceValidator`) | MIT |

---

## Reporting a problem with this file

If you own something listed here and an attribution is wrong, missing, or an
obligation is not being met, please open an issue on the Hermaeus repository.
It will be treated as a defect, not a discussion.
