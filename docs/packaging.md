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

The Linux package root contains only the public launcher and desktop install
files. Its layout is:

- `Hermaeus`, the public launcher
- `app/`, containing the published desktop runtime and `app/LocalApi/`
- `docs/README.md`, `docs/user-guide.md`, `docs/LICENSE.md`,
  `docs/NOTICE.md`, and `docs/COMMERCIAL.md`
- `docs/hermaeus-branding.*` when present
- `icons/`, containing desktop-install icon resources
- `hermaeus.desktop`
- `install-desktop.sh`
- `uninstall-desktop.sh`

Normal packages exclude `.pdb` symbol files.

To install the desktop launcher for the current user:

```bash
tar -xzf dist/hermaeus-<version>-linux-x64.tar.gz -C dist
dist/hermaeus-<version>-linux-x64/install-desktop.sh
```

The installer copies the package under
`${XDG_DATA_HOME:-$HOME/.local/share}/hermaeus/`, installs a desktop entry under
`${XDG_DATA_HOME:-$HOME/.local/share}/applications/`, and installs the icon
under `${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/512x512/apps/` using
the canonical application icon. It does not require root and does not write to
system paths.

To remove the user-local launcher and installed package:

```bash
~/.local/share/hermaeus/hermaeus-<version>-linux-x64/uninstall-desktop.sh
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

The Windows package includes published Hermaeus desktop binaries, app/tray icon
assets, repository license and commercial notices under `docs/`,
`docs/user-guide.md`, `docs/hermaeus-branding.*` when present, and
`Launch-Hermaeus.cmd` for starting the app from the extracted folder. Normal
packages exclude `.pdb` files.

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
- While `VersionSuffix` is `alpha` (or any prerelease suffix), releases are
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
