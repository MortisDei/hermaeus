#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT_DIR/src/Aether.Desktop/Aether.Desktop.csproj"
DIST_DIR="$ROOT_DIR/dist"
RUNTIME="linux-x64"
CONFIGURATION="Release"
SELF_CONTAINED="false"
SKIP_RESTORE="false"

usage() {
  cat <<'USAGE'
Usage: ./build.sh [options]

Options:
  --runtime <rid>          Target runtime identifier. Default: linux-x64.
  --configuration <name>   Build configuration. Default: Release.
  --self-contained         Publish a self-contained package. Default: framework-dependent.
  --skip-restore           Skip dotnet restore.
  -h, --help               Show this help.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --runtime)
      RUNTIME="${2:?Missing value for --runtime}"
      shift 2
      ;;
    --configuration)
      CONFIGURATION="${2:?Missing value for --configuration}"
      shift 2
      ;;
    --self-contained)
      SELF_CONTAINED="true"
      shift
      ;;
    --skip-restore)
      SKIP_RESTORE="true"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

version_prefix="$(sed -n 's:.*<VersionPrefix>\(.*\)</VersionPrefix>.*:\1:p' "$ROOT_DIR/Directory.Build.props" | head -n 1)"
version_suffix="$(sed -n 's:.*<VersionSuffix>\(.*\)</VersionSuffix>.*:\1:p' "$ROOT_DIR/Directory.Build.props" | head -n 1)"

if [[ -z "$version_prefix" ]]; then
  echo "Could not read VersionPrefix from Directory.Build.props." >&2
  exit 1
fi

VERSION="$version_prefix"
if [[ -n "$version_suffix" ]]; then
  VERSION="$VERSION-$version_suffix"
fi

PACKAGE_NAME="aether-$VERSION-$RUNTIME"
PACKAGE_DIR="$DIST_DIR/$PACKAGE_NAME"
PUBLISH_DIR="$DIST_DIR/.publish-$RUNTIME"
ARCHIVE="$DIST_DIR/$PACKAGE_NAME.tar.gz"
CHECKSUM="$ARCHIVE.sha256"
DOC_DIR="$PACKAGE_DIR/docs"

if [[ "$SKIP_RESTORE" == "false" ]]; then
  echo "Restoring..."
  dotnet restore "$PROJECT" -r "$RUNTIME"
fi

echo "Publishing $RUNTIME ($CONFIGURATION, self-contained=$SELF_CONTAINED)..."
rm -rf "$PACKAGE_DIR" "$PUBLISH_DIR" "$ARCHIVE" "$CHECKSUM"
mkdir -p "$PACKAGE_DIR" "$PUBLISH_DIR" "$DOC_DIR"

publish_args=(
  "$PROJECT"
  -c "$CONFIGURATION"
  -r "$RUNTIME"
  --self-contained "$SELF_CONTAINED"
  -o "$PUBLISH_DIR"
  --no-restore
  -p:UseSharedCompilation=false
  -m:1
)

dotnet publish "${publish_args[@]}"

cp -a "$PUBLISH_DIR"/. "$PACKAGE_DIR"/
cp "$ROOT_DIR/README.md" "$DOC_DIR/README.md"
cp "$ROOT_DIR/LICENSE.md" "$DOC_DIR/LICENSE.md"
cp "$ROOT_DIR/NOTICE.md" "$DOC_DIR/NOTICE.md"
cp "$ROOT_DIR/COMMERCIAL.md" "$DOC_DIR/COMMERCIAL.md"
cp "$ROOT_DIR/src/Aether.Desktop/Assets/aether.ico" "$PACKAGE_DIR/aether.ico"
cp "$ROOT_DIR/src/Aether.Desktop/Assets/aether-app.png" "$PACKAGE_DIR/aether-app.png"
cp "$ROOT_DIR/src/Aether.Desktop/Assets/aether-tray.png" "$PACKAGE_DIR/aether-tray.png"
cp "$ROOT_DIR/src/Aether.Desktop/Assets/aether-tray-dark.png" "$PACKAGE_DIR/aether-tray-dark.png"
cp "$ROOT_DIR/src/Aether.Desktop/Assets/aether-tray-light.png" "$PACKAGE_DIR/aether-tray-light.png"

for branding in "$ROOT_DIR"/docs/aether-branding.*; do
  if [[ -f "$branding" ]]; then
    cp "$branding" "$DOC_DIR/"
  fi
done

cat > "$PACKAGE_DIR/aether.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=Aether
Comment=Local-first AI workspace
Exec=sh -c 'appdir=$(dirname "$1"); cd "$appdir"; exec "$appdir/Aether.Desktop"' sh %k
Icon=aether
Terminal=false
Categories=Utility;Development;
StartupWMClass=Aether
DESKTOP

cat > "$PACKAGE_DIR/install-desktop.sh" <<'INSTALL'
#!/usr/bin/env bash
set -euo pipefail

SOURCE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PACKAGE_ID="$(basename "$SOURCE_DIR")"
APP_EXEC="$SOURCE_DIR/Aether.Desktop"

if [[ ! -x "$APP_EXEC" ]]; then
  echo "Missing executable: $APP_EXEC" >&2
  exit 1
fi

DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
APP_BASE="$DATA_HOME/aether"
INSTALL_DIR="$APP_BASE/$PACKAGE_ID"
APPLICATIONS_DIR="$DATA_HOME/applications"
PNG_ICON_DIR="$DATA_HOME/icons/hicolor/256x256/apps"
DESKTOP_FILE="$APPLICATIONS_DIR/aether.desktop"
PNG_ICON_FILE="$PNG_ICON_DIR/aether.png"

mkdir -p "$APP_BASE" "$APPLICATIONS_DIR" "$PNG_ICON_DIR"

if [[ "$SOURCE_DIR" != "$INSTALL_DIR" ]]; then
  rm -rf "$INSTALL_DIR"
  mkdir -p "$INSTALL_DIR"
  cp -a "$SOURCE_DIR"/. "$INSTALL_DIR"/
fi

chmod +x "$INSTALL_DIR/Aether.Desktop" "$INSTALL_DIR/install-desktop.sh" "$INSTALL_DIR/uninstall-desktop.sh"
cp "$INSTALL_DIR/aether-app.png" "$PNG_ICON_FILE"

cat > "$DESKTOP_FILE" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Aether
Comment=Local-first AI workspace
Exec=$INSTALL_DIR/Aether.Desktop
Icon=aether
Terminal=false
Categories=Utility;Development;
StartupWMClass=Aether
DESKTOP

chmod 0644 "$DESKTOP_FILE" "$PNG_ICON_FILE"

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$APPLICATIONS_DIR" >/dev/null 2>&1 || true
fi

if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache "$DATA_HOME/icons/hicolor" >/dev/null 2>&1 || true
fi

echo "Installed Aether desktop launcher to $DESKTOP_FILE"
echo "Installed Aether package to $INSTALL_DIR"
INSTALL

cat > "$PACKAGE_DIR/uninstall-desktop.sh" <<'UNINSTALL'
#!/usr/bin/env bash
set -euo pipefail

SOURCE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PACKAGE_ID="$(basename "$SOURCE_DIR")"
DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
INSTALL_DIR="$DATA_HOME/aether/$PACKAGE_ID"
DESKTOP_FILE="$DATA_HOME/applications/aether.desktop"
PNG_ICON_FILE="$DATA_HOME/icons/hicolor/256x256/apps/aether.png"

rm -f "$DESKTOP_FILE" "$PNG_ICON_FILE"

if [[ "$SOURCE_DIR" == "$INSTALL_DIR" ]]; then
  cd "$HOME"
  rm -rf "$INSTALL_DIR"
else
  rm -rf "$INSTALL_DIR"
fi

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$DATA_HOME/applications" >/dev/null 2>&1 || true
fi

if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache "$DATA_HOME/icons/hicolor" >/dev/null 2>&1 || true
fi

echo "Removed Aether desktop launcher and installed package."
UNINSTALL

chmod +x "$PACKAGE_DIR/install-desktop.sh" "$PACKAGE_DIR/uninstall-desktop.sh"
rm -rf "$PUBLISH_DIR"

echo "Creating $ARCHIVE..."
tar -C "$DIST_DIR" -czf "$ARCHIVE" "$PACKAGE_NAME"

echo "Writing $CHECKSUM..."
(
  cd "$DIST_DIR"
  sha256sum "$PACKAGE_NAME.tar.gz" > "$PACKAGE_NAME.tar.gz.sha256"
)

echo "Package ready:"
echo "  $PACKAGE_DIR"
echo "  $ARCHIVE"
echo "  $CHECKSUM"
