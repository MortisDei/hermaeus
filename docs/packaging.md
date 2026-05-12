# Packaging

Aether ships source-available archive packages for Linux and Windows. Packages
are generated locally into the ignored `dist/` directory and are not committed to
the repository.

## Linux

```bash
./build.sh
```

The default Linux package is framework-dependent for `linux-x64`:

```text
dist/aether-<version>-linux-x64/
dist/aether-<version>-linux-x64.tar.gz
dist/aether-<version>-linux-x64.tar.gz.sha256
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

- published Aether desktop binaries
- `docs/README.md`, `docs/LICENSE.md`, `docs/NOTICE.md`, and
  `docs/COMMERCIAL.md`
- `docs/aether-branding.*` when present
- `aether.desktop`
- `aether.ico`, `aether-app.png`, `aether-tray.png`,
  `aether-tray-dark.png`, and `aether-tray-light.png`
- `install-desktop.sh`
- `uninstall-desktop.sh`

To install the desktop launcher for the current user:

```bash
tar -xzf dist/aether-<version>-linux-x64.tar.gz -C dist
dist/aether-<version>-linux-x64/install-desktop.sh
```

The installer copies the package under
`${XDG_DATA_HOME:-$HOME/.local/share}/aether/`, installs a desktop entry under
`${XDG_DATA_HOME:-$HOME/.local/share}/applications/`, and installs the icon
under `${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/`. It does not require
root and does not write to system paths.

To remove the user-local launcher and installed package:

```bash
~/.local/share/aether/aether-<version>-linux-x64/uninstall-desktop.sh
```

## Windows

```powershell
pwsh ./build.ps1
```

The default Windows package is framework-dependent for `win-x64`:

```text
dist/aether-<version>-win-x64/
dist/aether-<version>-win-x64.zip
dist/aether-<version>-win-x64.zip.sha256
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

The Windows package includes published Aether desktop binaries, app/tray icon
assets, repository license and commercial notices under `docs/`,
`docs/aether-branding.*` when present, and `Launch-Aether.cmd` for starting the
app from the extracted folder.

## Checksums

Each script writes a SHA256 file next to the archive. Verify archives before
redistribution:

```bash
sha256sum -c dist/aether-<version>-linux-x64.tar.gz.sha256
```

```powershell
Get-FileHash -Algorithm SHA256 dist/aether-<version>-win-x64.zip
Get-Content dist/aether-<version>-win-x64.zip.sha256
```

## Licensing Posture

Packages include the source-available noncommercial license, commercial license
notice, and third-party notice documents. Commercial use still requires a
separate paid commercial license.
