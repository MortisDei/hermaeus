# 03. Dependencies and native assets

## Inventory method and evidence limits

**RF/VE, 2026-10-16:** enumerated all 13 tracked `.csproj` files with
`rg --files -g '*.csproj'`, parsed every PackageReference/ProjectReference/
FrameworkReference, then read each existing `obj/project.assets.json` and
available cached package `.nuspec`. All 13 assets files were present, generated
on this date, with **net10.0 only and no RID targets**. Their content hashes
below identify the inspected local graph; timestamps alone do not prove a fresh
restore. No restore, package upgrade or build was performed.

Approved-host command `dotnet list Hermaeus.sln package --vulnerable
--include-transitive --no-restore --format json` exited 0. The response named
`https://api.nuget.org/v3/index.json`, all 13 projects and **no vulnerable
package entries or errors**. This is a dated NuGet-advisory observation over
the existing graph, not proof that the app, native payload or external runtime
is vulnerability-free. Repeat against final restored RID closures before ship.

The tables below are extracted documentation, not a new dependency authority
or build tool. The project files and restored targets remain authoritative.
Consumer codes identify projects in the first table. `file:` licence metadata
means inspect that bundled licence; an absent SPDX expression is not permission
to assume MIT. Test/tool-only packages are not shipped merely because they
appear in the solution graph.

## Interpretation and decisions

- Core has no package references. All projects use the implicit
  `Microsoft.NETCore.App` framework; Web SDK LocalApi also uses
  `Microsoft.AspNetCore.App`, and Tests explicitly references it. Composition
  owns shared service registration; Desktop adds UI registrations. The actual
  project edges, including Services to Rag/Voice and Composition to all owning
  subsystems, are recorded below rather than inferred from the older AGENTS map.
- All four direct Avalonia framework packages are 12.1.3; AvaloniaEdit is
  intentionally 12.0.0. Transitive Avalonia.BuildServices 11.3.2 is a distinct
  build tool, not evidence that Avalonia 11 UI assemblies are loaded.
- DI 9.0.4 appears directly in Composition and Desktop. Desktop uses it, but
  gets the same dependency through Composition. DEL-02 can remove only the
  redundant direct declaration. It does not remove DI or a second container.
- Tmds.DBus.Protocol 0.94.1 has no direct C#/AXAML usage found. Avalonia.FreeDesktop
  12.1.3 already requires exactly that version. DEL-03 needs graph and native
  Linux checks; DBus remains needed by the framework.
- Inter is actively registered at `Program.BuildAvaloniaApp<TApplication>`.
  AppFontService defaults and branding use native role typography. DEL-01 is a
  conditional hook/package removal, not an assertion that the package is unused.
- ONNX 1.25.1 serves Voice Kokoro/Whisper and Rag cross-encoder inference.
  Different models/sessions are legitimate. The graph converges on one package
  version per consuming output; repeated references do not prove duplicate
  native runtime instances. Profile residency and session disposal separately.
- Markdig 0.38.0 parses MarkdownViewer ASTs; PdfPig 0.1.14 extracts digital PDF
  pages; Tokenizers 2.0.0 tokenises reranker input; Tensors is used for RAG SIMD
  cosine. Their features exist. No removal is justified by package size alone.
- Tensors resolves to 9.0.0 in the standalone Voice closure through
  OnnxRuntime.Managed and 9.0.9 in Rag/Services/Desktop closures. This is
  cross-project restore skew, not two versions proven loaded in Desktop.
  DI/Sqlite/TestHost 9.x on net10.0 are compatibility-review candidates, not
  demonstrated incompatibilities or automatic upgrade work.
- Preserve explicit SQLite bundle 3.0.0 in Agent/Services/Rag. The inspected
  native library resolves to 3.50.3. The
  [reviewed advisory](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)
  identifies the older SQLite vulnerability and recommends SQLite 3.50.2 or
  later. Verify `sqlite_version()` from final Windows/Linux packages before
  changing any pin. Package names alone are insufficient native evidence.

## Native, data and external deployment inventory

The generated native table lists package contents, including RIDs not shipped
by default. It is **not a final archive manifest**. Default packages publish
Desktop and LocalApi separately for win-x64 or linux-x64, framework-dependent;
self-contained is opt-in. This can legitimately place shared assemblies/native
libraries in both process payloads. Do not deduplicate across process directories
without loader/relocation tests. Shipping byte sizes and loaded-module versions
are Unknown in this pass.

Additional deployment requirements outside the NuGet table:

| Component | Ownership and packaging | Review requirement / package |
| --- | --- | --- |
| Windows launcher/apphost, .NET runtime | Source `src/Hermaeus.Launcher/launcher.c`; build.ps1 compiles fixed-target launcher; .NET 10 installed or self-contained | B0/G1: compiler/runtime/RID fingerprint, path-with-spaces launch, relocation, no PDBs |
| Linux desktop stack | Avalonia X11/FreeDesktop/AT-SPI, native Skia/HarfBuzz, font availability, DBus/session/portal; Wayland session may run XWayland | B0/U1: inspect actual backend and OS libraries, no claim of native Wayland from compositor name alone |
| CMU dictionary | Voice embeds `Assets/cmudict.txt.gz`; licence file in Voice assets | B0/G1: ensure required attribution travels in archive |
| Inter typeface | Embedded font package; package MIT, font SIL OFL as in third-party notices | D1: remove only after native font gate; otherwise retain notices |
| Moss pet data | `assets/pets/moss`, copied as Content, disabled by default | G1: provenance and unresolved redistribution/licensing receipt remain owner decision, not resolved by this plan |
| Agent scenarios and bundled help/icons/styles | Agent fixture data copied to consumers; Desktop explicit resources and BundledHelp files | G1: only intended fixtures/assets, no owner content or test artefacts |
| llama.cpp/ggml/backend libraries | Explicit managed download or owner-configured executable, not a csproj payload; pinned setup b10034 and separately newer verified downloads | S4/P3: archive digest, executable/module hashes, CUDA/Vulkan/CPU prerequisites and exact effective launch |
| CUDA driver/runtime, Vulkan loader/driver | External platform components and selected runtime archive; proprietary components retain their own terms | B0/G1: version/licence inventory, no vendor dependency added to Core or provider-neutral contracts |
| Voice helpers and capture/playback | Optional Python/XTTS/F5/Kokoro processes; Windows winmm/PInvoke and Linux external audio utilities | L2/G1: owned process cleanup, executable provenance and platform audio checks |
| ONNX/GGUF/model/tokeniser assets | Explicit pinned download or owner selection; separate from code licence | S4/P2/P3: exact revision/hash, model terms, resource/load/quality proof before use |

**RF packaging gap:** build.ps1 around 227 and build.sh around 136 explicitly
copy README, user guide, LICENSE, NOTICE and COMMERCIAL, but do not explicitly
copy root `THIRD-PARTY-NOTICES.md`. A NuGet licence existing in the package cache
does not prove it reaches publish output. B0/G1 must inspect both archives and
add required licence/NOTICE delivery during implementation if absent. This
planning pass corrects stale notice contents, not the packaging scripts.

**Licensing decision:** cached JsonSchema.Net/JsonPointer.Net/Json.More.Net
metadata names `OSMFEULA.txt`. Its text distinguishes MIT source from maintenance
terms on publisher binaries. These are TraceValidator-only, outside shipped
Desktop/LocalApi closure. Owner review of actual use/distribution is required;
no conclusion about fee liability or licence compatibility is made. Do not add
a replacement validator or dependency as a planning shortcut. Native licence
files and model terms need their own review beyond a package SPDX field.

## Extracted project and restore inventory

<!-- R34_INVENTORY_START -->
| Code | Project file | Direct packages | Project references |
| --- | --- | --- | --- |
| A | `src/Hermaeus.Agent/Hermaeus.Agent.csproj` | `Microsoft.Data.Sqlite 9.0.9`, `SQLitePCLRaw.bundle_e_sqlite3 3.0.0` | C |
| O | `src/Hermaeus.Composition/Hermaeus.Composition.csproj` | `Microsoft.Extensions.DependencyInjection 9.0.4` | C, S, A, R, M, V |
| C | `src/Hermaeus.Core/Hermaeus.Core.csproj` | None | None |
| D | `src/Hermaeus.Desktop/Hermaeus.Desktop.csproj` | `Avalonia 12.1.3`, `Avalonia.AvaloniaEdit 12.0.0`, `Avalonia.Desktop 12.1.3`, `Avalonia.Themes.Fluent 12.1.3`, `Avalonia.Fonts.Inter 12.1.3`, `Microsoft.Extensions.DependencyInjection 9.0.4`, `Markdig 0.38.0`, `Tmds.DBus.Protocol 0.94.1` | C, A, S, VM, R, M, O |
| API | `src/Hermaeus.LocalApi/Hermaeus.LocalApi.csproj` | None | O, R |
| M | `src/Hermaeus.Mcp/Hermaeus.Mcp.csproj` | None | C |
| R | `src/Hermaeus.Rag/Hermaeus.Rag.csproj` | `Microsoft.ML.OnnxRuntime 1.25.1`, `Microsoft.ML.Tokenizers 2.0.0`, `Microsoft.Data.Sqlite 9.0.9`, `SQLitePCLRaw.bundle_e_sqlite3 3.0.0`, `PdfPig 0.1.14`, `System.Numerics.Tensors 9.0.9` | C |
| S | `src/Hermaeus.Services/Hermaeus.Services.csproj` | `Microsoft.Data.Sqlite 9.0.9`, `SQLitePCLRaw.bundle_e_sqlite3 3.0.0` | C, R, V |
| T | `src/Hermaeus.Tests/Hermaeus.Tests.csproj` | `Microsoft.NET.Test.Sdk 17.14.1`, `xunit 2.9.2`, `xunit.runner.visualstudio 2.8.2`, `coverlet.collector 6.0.2`, `Microsoft.AspNetCore.TestHost 9.0.4` | C, A, R, S, VM, D, M, O, API, V |
| VM | `src/Hermaeus.ViewModels/Hermaeus.ViewModels.csproj` | `CommunityToolkit.Mvvm 8.3.2` | C, A, S, R |
| V | `src/Hermaeus.Voice/Hermaeus.Voice.csproj` | `Microsoft.ML.OnnxRuntime 1.25.1` | C |
| DRV | `src/Tools/R33Driver/R33Driver.csproj` | None | A, O, C, R, S |
| TV | `src/Tools/TraceValidator/TraceValidator.csproj` | `JsonSchema.Net 9.2.1` | None |

### Resolved packages

The union contains 64 package/version pairs. Direct consumer codes are listed separately; all others are transitive in that project.

| Package/version | All consumers | Direct consumers | Cached nuspec licence |
| --- | --- | --- | --- |
| `Avalonia.Angle.Windows.Natives/2.1.27548.20260419` | D, T | None | file: LICENSE |
| `Avalonia.AvaloniaEdit/12.0.0` | D, T | D | expression: MIT |
| `Avalonia.BuildServices/11.3.2` | D, T | None | expression: MIT |
| `Avalonia.Desktop/12.1.3` | D, T | D | expression: MIT |
| `Avalonia.Fonts.Inter/12.1.3` | D, T | D | expression: MIT |
| `Avalonia.FreeDesktop.AtSpi/12.1.3` | D, T | None | expression: MIT |
| `Avalonia.FreeDesktop/12.1.3` | D, T | None | expression: MIT |
| `Avalonia.HarfBuzz/12.1.3` | D, T | None | expression: MIT |
| `Avalonia.Native/12.1.3` | D, T | None | expression: MIT |
| `Avalonia.Remote.Protocol/12.1.3` | D, T | None | expression: MIT |
| `Avalonia.Skia/12.1.3` | D, T | None | expression: MIT |
| `Avalonia.Themes.Fluent/12.1.3` | D, T | D | expression: MIT |
| `Avalonia.Win32/12.1.3` | D, T | None | expression: MIT |
| `Avalonia.X11/12.1.3` | D, T | None | expression: MIT |
| `Avalonia/12.1.3` | D, T | D | expression: MIT |
| `CommunityToolkit.Mvvm/8.3.2` | D, T, VM | VM | expression: MIT |
| `coverlet.collector/6.0.2` | T | T | expression: MIT |
| `Google.Protobuf/3.30.2` | API, D, DRV, O, R, S, T, VM | None | expression: BSD-3-Clause |
| `HarfBuzzSharp.NativeAssets.Linux/8.3.1.3` | D, T | None | expression: MIT |
| `HarfBuzzSharp.NativeAssets.macOS/8.3.1.3` | D, T | None | expression: MIT |
| `HarfBuzzSharp.NativeAssets.WebAssembly/8.3.1.3` | D, T | None | expression: MIT |
| `HarfBuzzSharp.NativeAssets.Win32/8.3.1.3` | D, T | None | expression: MIT |
| `HarfBuzzSharp/8.3.1.3` | D, T | None | expression: MIT |
| `Humanizer.Core/3.0.10` | TV | None | expression: MIT |
| `Json.More.Net/3.0.1` | TV | None | file: OSMFEULA.txt |
| `JsonPointer.Net/7.0.1` | TV | None | file: OSMFEULA.txt |
| `JsonSchema.Net/9.2.1` | TV | TV | file: OSMFEULA.txt |
| `Markdig/0.38.0` | D, T | D | expression: BSD-2-Clause |
| `MicroCom.Runtime/0.11.6` | D, T | None | expression: MIT |
| `Microsoft.AspNetCore.TestHost/9.0.4` | T | T | expression: MIT |
| `Microsoft.CodeCoverage/17.14.1` | T | None | expression: MIT |
| `Microsoft.Data.Sqlite.Core/9.0.9` | A, API, D, DRV, O, R, S, T, VM | None | expression: MIT |
| `Microsoft.Data.Sqlite/9.0.9` | A, API, D, DRV, O, R, S, T, VM | A, R, S | expression: MIT |
| `Microsoft.Extensions.DependencyInjection.Abstractions/9.0.4` | D, DRV, O | None | expression: MIT |
| `Microsoft.Extensions.DependencyInjection/9.0.4` | D, DRV, O | D, O | expression: MIT |
| `Microsoft.ML.OnnxRuntime.Managed/1.25.1` | API, D, DRV, O, R, S, T, V, VM | None | file: LICENSE.txt |
| `Microsoft.ML.OnnxRuntime/1.25.1` | API, D, DRV, O, R, S, T, V, VM | R, V | file: LICENSE |
| `Microsoft.ML.Tokenizers/2.0.0` | API, D, DRV, O, R, S, T, VM | R | expression: MIT |
| `Microsoft.NET.Test.Sdk/17.14.1` | T | T | expression: MIT |
| `Microsoft.TestPlatform.ObjectModel/17.14.1` | T | None | expression: MIT |
| `Microsoft.TestPlatform.TestHost/17.14.1` | T | None | expression: MIT |
| `Newtonsoft.Json/13.0.3` | T | None | expression: MIT |
| `PdfPig/0.1.14` | API, D, DRV, O, R, S, T, VM | R | expression: Apache-2.0 |
| `SkiaSharp.NativeAssets.Linux/3.119.4` | D, T | None | expression: MIT |
| `SkiaSharp.NativeAssets.macOS/3.119.4` | D, T | None | expression: MIT |
| `SkiaSharp.NativeAssets.WebAssembly/3.119.4` | D, T | None | expression: MIT |
| `SkiaSharp.NativeAssets.Win32/3.119.4` | D, T | None | expression: MIT |
| `SkiaSharp/3.119.4` | D, T | None | expression: MIT |
| `SQLitePCLRaw.bundle_e_sqlite3/3.0.0` | A, API, D, DRV, O, R, S, T, VM | A, R, S | expression: Apache-2.0 |
| `SQLitePCLRaw.config.e_sqlite3/3.0.0` | A, API, D, DRV, O, R, S, T, VM | None | expression: Apache-2.0 |
| `SQLitePCLRaw.core/3.0.0` | A, API, D, DRV, O, R, S, T, VM | None | expression: Apache-2.0 |
| `SQLitePCLRaw.lib.e_sqlite3/3.50.3` | A, API, D, DRV, O, R, S, T, VM | None | No expression; inspect upstream licence |
| `SQLitePCLRaw.provider.e_sqlite3/3.0.0` | A, API, D, DRV, O, R, S, T, VM | None | expression: Apache-2.0 |
| `System.Numerics.Tensors/9.0.0` | V | None | expression: MIT |
| `System.Numerics.Tensors/9.0.9` | API, D, DRV, O, R, S, T, VM | R | expression: MIT |
| `Tmds.DBus.Protocol/0.94.1` | D, T | D | expression: MIT |
| `xunit.abstractions/2.0.3` | T | None | No expression; inspect upstream licence |
| `xunit.analyzers/1.16.0` | T | None | expression: Apache-2.0 |
| `xunit.assert/2.9.2` | T | None | expression: Apache-2.0 |
| `xunit.core/2.9.2` | T | None | expression: Apache-2.0 |
| `xunit.extensibility.core/2.9.2` | T | None | expression: Apache-2.0 |
| `xunit.extensibility.execution/2.9.2` | T | None | expression: Apache-2.0 |
| `xunit.runner.visualstudio/2.8.2` | T | T | expression: Apache-2.0 |
| `xunit/2.9.2` | T | T | expression: Apache-2.0 |

### Native package files

| Package/version | Native binary/file names | RIDs in package file list |
| --- | --- | --- |
| `Avalonia.Angle.Windows.Natives/2.1.27548.20260419` | av_libglesv2.dll | win-arm64, win-x64, win-x86 |
| `Avalonia.Native/12.1.3` | libAvaloniaNative.dylib | osx |
| `HarfBuzzSharp.NativeAssets.Linux/8.3.1.3` | libHarfBuzzSharp.so | linux-arm, linux-arm64, linux-loongarch64, linux-musl-arm, linux-musl-arm64, linux-musl-loongarch64, linux-musl-riscv64, linux-musl-x64, linux-riscv64, linux-x64, linux-x86 |
| `HarfBuzzSharp.NativeAssets.macOS/8.3.1.3` | libHarfBuzzSharp.dylib | osx |
| `HarfBuzzSharp.NativeAssets.WebAssembly/8.3.1.3` | libHarfBuzzSharp.a, Emscripten version/thread/SIMD variants | No runtimes RID; buildTransitive assets, not selected by current desktop targets |
| `HarfBuzzSharp.NativeAssets.Win32/8.3.1.3` | libHarfBuzzSharp.dll, libHarfBuzzSharp.pdb | win-arm64, win-x64, win-x86 |
| `Microsoft.ML.OnnxRuntime/1.25.1` | libonnxruntime_providers_shared.so, libonnxruntime.dylib, libonnxruntime.so, onnxruntime_providers_shared.dll, onnxruntime_providers_shared.lib, onnxruntime.aar, onnxruntime.dll, onnxruntime.lib, onnxruntime.xcframework.zip | android, ios, linux-arm64, linux-x64, osx-arm64, win-arm64, win-x64 |
| `SkiaSharp.NativeAssets.Linux/3.119.4` | libSkiaSharp.so | linux-arm, linux-arm64, linux-bionic-arm64, linux-bionic-x64, linux-loongarch64, linux-musl-arm, linux-musl-arm64, linux-musl-loongarch64, linux-musl-riscv64, linux-musl-x64, linux-riscv64, linux-x64, linux-x86 |
| `SkiaSharp.NativeAssets.macOS/3.119.4` | libSkiaSharp.dylib | osx |
| `SkiaSharp.NativeAssets.WebAssembly/3.119.4` | libSkiaSharp.a, Emscripten version/thread/SIMD variants | No runtimes RID; buildTransitive assets, not selected by current desktop targets |
| `SkiaSharp.NativeAssets.Win32/3.119.4` | libSkiaSharp.dll, libSkiaSharp.pdb | win-arm64, win-x64, win-x86 |
| `SQLitePCLRaw.lib.e_sqlite3/3.50.3` | e_sqlite3.a, e_sqlite3.dll, libe_sqlite3.dylib, libe_sqlite3.so | android-arm, android-arm64, android-x64, android-x86, ios-arm, ios-arm64, iossimulator-arm64, iossimulator-x64, iossimulator-x86, linux-arm, linux-arm64, linux-armel, linux-mips64, linux-musl-arm, linux-musl-arm64, linux-musl-s390x, linux-musl-x64, linux-ppc64le, linux-s390x, linux-x64, linux-x86, maccatalyst-arm64, maccatalyst-x64, osx-arm64, osx-x64, win-arm64, win-x64, win-x86 |

### Restore asset fingerprints

Each file has only a net10.0 target. Library counts include project references. SHA256 identifies local inspected evidence, not a package authenticity signature.

| Assets path | Libraries | SHA256 |
| --- | --- | --- |
| `src/Hermaeus.Agent/obj/project.assets.json` | 8 | `dddf069941d06779c1fcdd8f9326bf7044d973f343d22989a5272760802a1879` |
| `src/Hermaeus.Composition/obj/project.assets.json` | 21 | `53cd619e971291a72d39b781bc4eb3bddcea190e08f9c2133c996f8569c4597b` |
| `src/Hermaeus.Core/obj/project.assets.json` | 0 | `bac358cd6e35c7e0046f3dceebcc44a78414cda036d2a2524b852fcacd9cf3fd` |
| `src/Hermaeus.Desktop/obj/project.assets.json` | 52 | `521242e03b9a370d06a69699c2dbdd00489b56d83c716652fa5761eb5b5df2ac` |
| `src/Hermaeus.LocalApi/obj/project.assets.json` | 20 | `bc70d64e69390fb16dafad2b30cc59f81b8a5bca01ec0fa29b9ed95d43b60b1a` |
| `src/Hermaeus.Mcp/obj/project.assets.json` | 1 | `d973bba0f2e96dbb1cc71f5e94c72f53c4b55ea5b4f2bb922634e9b6a67f2f2b` |
| `src/Hermaeus.Rag/obj/project.assets.json` | 14 | `21833148ab4080b96051c4e8f7f832b44daf061414a9d70b843332921c6750de` |
| `src/Hermaeus.Services/obj/project.assets.json` | 16 | `19ad1da999b973abd02c2ec48b35596f7dff7004276cf79c87aaa9c993345815` |
| `src/Hermaeus.Tests/obj/project.assets.json` | 67 | `9aa4acdc9e4a3b2d3eb9aaec8e8dfe3313a0c05fc0b7b75ff9b86dd1dd0edca2` |
| `src/Hermaeus.ViewModels/obj/project.assets.json` | 19 | `2f8eb2ae2f120297aa0e3882fd642f9e936ade1b38ecf92faee5d8058a380f8d` |
| `src/Hermaeus.Voice/obj/project.assets.json` | 4 | `663eb0998ca195235e028b2ad02a337b74c6ca2e0f5bd2cd1501e6e1dfbb457c` |
| `src/Tools/R33Driver/obj/project.assets.json` | 22 | `890112e09b18812705ba7c73442afd6cceec7c67c8c3363582adae31cb2ac1fc` |
| `src/Tools/TraceValidator/obj/project.assets.json` | 4 | `549052a7b21a55398be03f6d526672d276795ba3d98279696614dd74543f3bb3` |
<!-- R34_INVENTORY_END -->
