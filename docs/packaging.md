# Packaging

Hermaeus ships source-available archive packages for Linux and Windows. Packages
are generated locally into the ignored `dist/` directory and are not committed to
the repository.

## Linux

```bash
./build.sh
```

The default Linux package is framework-dependent for `linux-x64`:

```text
dist/hermaeus-<version>-linux-x64/
dist/hermaeus-<version>-linux-x64.tar.gz
dist/hermaeus-<version>-linux-x64.tar.gz.sha256
```

Useful options:

```bash
./build.sh --skip-restore
./build.sh --runtime linux-arm64
./build.sh --configuration Debug
./build.sh --self-contained
```

The normal `./build.sh` path performs the required `linux-x64` restores. Use
`--skip-restore` only as an optimisation after restoring every published
project for that RID. For the default package:

```bash
dotnet restore Hermaeus.sln -r linux-x64
./build.sh --skip-restore
```

An ordinary solution restore without the publish RID can leave `project.assets.json`
without the required target and cause `NETSDK1047`.

Framework-dependent packages require the .NET 10 desktop/runtime stack on the
target machine. Self-contained packages include the runtime and are larger.

The Linux package root contains only public launch actions and grouped content.
Its layout is:

- `Hermaeus`, a relocation-safe link to the native application launcher
- `Install Hermaeus`, the graphical user-local desktop installer
- `Uninstall Hermaeus`, the graphical user-local desktop uninstaller
- `app/`, containing the published desktop runtime and `app/LocalApi/`
- `docs/README.md`, `docs/user-guide.md`, `docs/LICENSE.md`,
  `docs/NOTICE.md`, and `docs/COMMERCIAL.md`
- `docs/hermaeus-branding.*` when present
- `icons/`, containing desktop-install icon resources

Normal packages exclude `.pdb` symbol files.

Double-click `Hermaeus` in the extracted directory to launch directly. It is a
relative link to the package's native .NET apphost, not a shell script, so file
managers can treat it as an executable without an execute-text-files preference.
The link and its target remain valid when the extracted directory is moved.
The internal executable has a neutral filename so it is not misclassified as a
Desktop Entry by file managers that treat `.Desktop` suffixes case-insensitively.

To install the desktop launcher for the current user, double-click
`Install Hermaeus` and confirm the action. No terminal or executable-text-file
preference is required. The implementation scripts remain under
`app/integration/` and are not part of the package's user-facing root.

For release-package validation from a terminal, the same implementation can be
invoked directly:

```bash
tar -xzf dist/hermaeus-<version>-linux-x64.tar.gz -C dist
dist/hermaeus-<version>-linux-x64/app/integration/install-desktop.sh
```

The installer copies the package under
`${XDG_DATA_HOME:-$HOME/.local/share}/hermaeus/`, installs a desktop entry under
`${XDG_DATA_HOME:-$HOME/.local/share}/applications/`, and installs the icon
under `${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/512x512/apps/` using
the canonical application icon. The installed desktop filename, its icon name,
and the application's X11/XWayland WM class all use `hermaeus`, allowing the
desktop shell to associate running windows with this entry. It does not require
root and does not write to system paths.

Source and Debug launches use the same `hermaeus` window identity as release
builds, but a desktop shell can still show a generic icon when there is no
installed `hermaeus.desktop` entry to associate with that running window. The
installed package is the release taskbar and application-menu verification path.

To remove the user-local launcher and installed package, double-click
`Uninstall Hermaeus` in the extracted archive and confirm. The terminal
equivalent for release validation is:

```bash
dist/hermaeus-<version>-linux-x64/app/integration/uninstall-desktop.sh
```

## Windows

```powershell
pwsh ./build.ps1
```

The default Windows package is framework-dependent for `win-x64`:

```text
dist/hermaeus-<version>-win-x64/
dist/hermaeus-<version>-win-x64.zip
dist/hermaeus-<version>-win-x64.zip.sha256
```

Useful options:

```powershell
pwsh ./build.ps1 -SkipRestore
pwsh ./build.ps1 -Runtime win-arm64
pwsh ./build.ps1 -Configuration Debug
pwsh ./build.ps1 -SelfContained
```

The normal PowerShell build performs RID-specific restores. If the projects
were already restored for the target RID, the equivalent optimised path is:

```powershell
dotnet restore Hermaeus.sln -r win-x64
pwsh ./build.ps1 -SkipRestore -Runtime win-x64
```

Framework-dependent packages require the .NET 10 runtime on the target machine.
Self-contained packages include the runtime and are larger.

The Windows archive keeps its public root small:

```text
hermaeus-<version>-win-x64/
    Hermaeus.exe
    app/
        Hermaeus.Desktop.exe
        <desktop runtime files>
        LocalApi/
            <Local API runtime files>
    docs/
    icons/
```

`Hermaeus.exe` is a minimal open-source launcher implemented with Win32. Its
auditable source is `src/Hermaeus.Launcher/launcher.c`, with the canonical
application icon linked from `launcher.rc`. It resolves its own package
directory and starts only the fixed bundled target
`app\Hermaeus.Desktop.exe`, using `app\` as the working
directory and forwarding the original command-line arguments. It performs no
network, update, installation, elevation, registry, discovery, or persistence
behavior. The launcher exists only to keep the portable archive tidy and give
users a normal double-click entry point. The old `Launch-Hermaeus.cmd` is not
packaged.

The build requires a native compiler only for this small launcher. Windows
builds use the Visual Studio C++ tools already present on GitHub's
`windows-latest` release runner. A non-Windows `win-x64` build uses
`x86_64-w64-mingw32-gcc` and `windres`; cross-building `win-arm64` requires a
Windows host with the Visual Studio ARM64 C++ tools. No third-party library or
launcher runtime is added to the package.

Published desktop files live under `app/`, the Local API payload under
`app/LocalApi/`, repository documentation and notices under `docs/`, and
application/tray icon assets under `icons/`. Normal packages exclude `.pdb`
files across the whole tree. `build.ps1` validates this layout before creating
the ZIP and checksum.

Linux tar headers use numeric root ownership and remove group/other write bits.
This prevents a local builder username, group name, or permissive worktree mode
from becoming part of a published archive. Extraction does not require root and
the user who extracts the archive still owns the resulting files.

## Checksums

Each script writes a SHA256 file next to the archive. Verify archives before
redistribution:

```bash
sha256sum -c dist/hermaeus-<version>-linux-x64.tar.gz.sha256
```

```powershell
Get-FileHash -Algorithm SHA256 dist/hermaeus-<version>-win-x64.zip
Get-Content dist/hermaeus-<version>-win-x64.zip.sha256
```

## Releases

Starting with 0.29.0-alpha, pushing an annotated tag builds and publishes a
GitHub Release automatically; nothing about local packaging above changes.

- Versions live in `Directory.Build.props` (`VersionPrefix`/`VersionSuffix`);
  that file is the single source of truth. Nothing else should be edited to
  bump a version.
- Every minor version bump (0.28.0 -> 0.29.0) gets an annotated tag
  `v<version>` (e.g. `v0.29.0-alpha`) pushed once the release commit is on
  `main` with green CI:

  ```bash
  git tag -a v0.29.0-alpha -m "Hermaeus 0.29.0-alpha"
  git push origin v0.29.0-alpha
  ```

  The tag push triggers `.github/workflows/release.yml`, which builds
  win-x64 and linux-x64 packages with the same `build.ps1`/`build.sh` scripts
  described above, verifies the tag matches `Directory.Build.props`, and
  publishes a GitHub Release with changelog-derived notes.
- Patch versions (0.29.1) are tagged and released only when they carry an
  urgent fix users need; otherwise they ride until the next minor.
- While `VersionSuffix` is a prerelease suffix, releases are
  marked prerelease on GitHub.
- Binaries are unsigned. Every release's notes say so plainly and include
  per-OS SHA256 verification commands (see Checksums above). Installer
  signing remains documented future work.
- The release workflow never bumps a version, writes the changelog, or
  creates a tag; it only reacts to a tag that already exists.

## Licensing Posture

Packages include the source-available noncommercial license, commercial license
notice, and third-party notice documents. Uses outside PolyForm Noncommercial's
permitted purposes require separate written permission; see `COMMERCIAL.md`.
