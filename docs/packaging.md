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

Framework-dependent packages require the .NET 10 desktop/runtime stack on the
target machine. Self-contained packages include the runtime and are larger.

The Linux package includes:

- published Hermaeus desktop binaries
- `docs/README.md`, `docs/LICENSE.md`, `docs/NOTICE.md`, and
  `docs/COMMERCIAL.md`
- `docs/hermaeus-branding.*` when present
- `hermaeus.desktop`
- `hermaeus.ico`, `hermaeus-app.png`, `hermaeus-tray.png`,
  `hermaeus-tray-dark.png`, and `hermaeus-tray-light.png`
- `install-desktop.sh`
- `uninstall-desktop.sh`

To install the desktop launcher for the current user:

```bash
tar -xzf dist/hermaeus-<version>-linux-x64.tar.gz -C dist
dist/hermaeus-<version>-linux-x64/install-desktop.sh
```

The installer copies the package under
`${XDG_DATA_HOME:-$HOME/.local/share}/hermaeus/`, installs a desktop entry under
`${XDG_DATA_HOME:-$HOME/.local/share}/applications/`, and installs the icon
under `${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/`. It does not require
root and does not write to system paths.

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

Framework-dependent packages require the .NET 10 runtime on the target machine.
Self-contained packages include the runtime and are larger.

The Windows package includes published Hermaeus desktop binaries, app/tray icon
assets, repository license and commercial notices under `docs/`,
`docs/hermaeus-branding.*` when present, and `Launch-Hermaeus.cmd` for starting the
app from the extracted folder.

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

## Licensing Posture

Packages include the source-available noncommercial license, commercial license
notice, and third-party notice documents. Commercial use still requires a
separate paid commercial license.
