#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT_DIR/src/Hermaeus.Desktop/Hermaeus.Desktop.csproj"
LOCALAPI_PROJECT="$ROOT_DIR/src/Hermaeus.LocalApi/Hermaeus.LocalApi.csproj"
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

PACKAGE_NAME="hermaeus-$VERSION-$RUNTIME"
PACKAGE_DIR="$DIST_DIR/$PACKAGE_NAME"
PUBLISH_DIR="$DIST_DIR/.publish-$RUNTIME"
LOCALAPI_PUBLISH_DIR="$DIST_DIR/.publish-localapi-$RUNTIME"
ARCHIVE="$DIST_DIR/$PACKAGE_NAME.tar.gz"
CHECKSUM="$ARCHIVE.sha256"
DOC_DIR="$PACKAGE_DIR/docs"
LOCALAPI_DIR="$PACKAGE_DIR/LocalApi"

if [[ "$SKIP_RESTORE" == "false" ]]; then
  echo "Restoring..."
  dotnet restore "$PROJECT" -r "$RUNTIME"
  dotnet restore "$LOCALAPI_PROJECT" -r "$RUNTIME"
fi

echo "Publishing $RUNTIME ($CONFIGURATION, self-contained=$SELF_CONTAINED)..."
rm -rf "$PACKAGE_DIR" "$PUBLISH_DIR" "$LOCALAPI_PUBLISH_DIR" "$ARCHIVE" "$CHECKSUM"
mkdir -p "$PACKAGE_DIR" "$PUBLISH_DIR" "$LOCALAPI_PUBLISH_DIR" "$DOC_DIR" "$LOCALAPI_DIR"

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

localapi_publish_args=(
  "$LOCALAPI_PROJECT"
  -c "$CONFIGURATION"
  -r "$RUNTIME"
  --self-contained "$SELF_CONTAINED"
  -o "$LOCALAPI_PUBLISH_DIR"
  --no-restore
  -p:UseSharedCompilation=false
  -m:1
)

dotnet publish "${localapi_publish_args[@]}"

cp -a "$PUBLISH_DIR"/. "$PACKAGE_DIR"/
cp -a "$LOCALAPI_PUBLISH_DIR"/. "$LOCALAPI_DIR"/
cp "$ROOT_DIR/README.md" "$DOC_DIR/README.md"
cp "$ROOT_DIR/LICENSE.md" "$DOC_DIR/LICENSE.md"
cp "$ROOT_DIR/NOTICE.md" "$DOC_DIR/NOTICE.md"
cp "$ROOT_DIR/COMMERCIAL.md" "$DOC_DIR/COMMERCIAL.md"
cp "$ROOT_DIR/src/Hermaeus.Desktop/Assets/hermaeus.ico" "$PACKAGE_DIR/hermaeus.ico"
cp "$ROOT_DIR/src/Hermaeus.Desktop/Assets/hermaeus-app.png" "$PACKAGE_DIR/hermaeus-app.png"
cp "$ROOT_DIR/src/Hermaeus.Desktop/Assets/hermaeus-tray.png" "$PACKAGE_DIR/hermaeus-tray.png"
cp "$ROOT_DIR/src/Hermaeus.Desktop/Assets/hermaeus-tray-dark.png" "$PACKAGE_DIR/hermaeus-tray-dark.png"
cp "$ROOT_DIR/src/Hermaeus.Desktop/Assets/hermaeus-tray-light.png" "$PACKAGE_DIR/hermaeus-tray-light.png"

for branding in "$ROOT_DIR"/docs/hermaeus-branding.*; do
  if [[ -f "$branding" ]]; then
    cp "$branding" "$DOC_DIR/"
  fi
done

cat > "$PACKAGE_DIR/hermaeus.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=Hermaeus
Comment=Local-first AI workspace
Exec=sh -c 'appdir=$(dirname "$1"); cd "$appdir"; exec "$appdir/Hermaeus.Desktop"' sh %k
Icon=hermaeus
Terminal=false
Categories=Utility;Development;
StartupWMClass=Hermaeus
DESKTOP

cat > "$PACKAGE_DIR/install-desktop.sh" <<'INSTALL'
#!/usr/bin/env bash
set -euo pipefail

SOURCE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PACKAGE_ID="$(basename "$SOURCE_DIR")"
APP_EXEC="$SOURCE_DIR/Hermaeus.Desktop"

if [[ ! -x "$APP_EXEC" ]]; then
  echo "Missing executable: $APP_EXEC" >&2
  exit 1
fi

DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
APP_BASE="$DATA_HOME/hermaeus"
INSTALL_DIR="$APP_BASE/$PACKAGE_ID"
APPLICATIONS_DIR="$DATA_HOME/applications"
PNG_ICON_DIR="$DATA_HOME/icons/hicolor/256x256/apps"
DESKTOP_FILE="$APPLICATIONS_DIR/hermaeus.desktop"
PNG_ICON_FILE="$PNG_ICON_DIR/hermaeus.png"

mkdir -p "$APP_BASE" "$APPLICATIONS_DIR" "$PNG_ICON_DIR"

if [[ "$SOURCE_DIR" != "$INSTALL_DIR" ]]; then
  rm -rf "$INSTALL_DIR"
  mkdir -p "$INSTALL_DIR"
  cp -a "$SOURCE_DIR"/. "$INSTALL_DIR"/
fi

chmod +x "$INSTALL_DIR/Hermaeus.Desktop" "$INSTALL_DIR/install-desktop.sh" "$INSTALL_DIR/uninstall-desktop.sh"
cp "$INSTALL_DIR/hermaeus-app.png" "$PNG_ICON_FILE"

cat > "$DESKTOP_FILE" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Hermaeus
Comment=Local-first AI workspace
Exec=$INSTALL_DIR/Hermaeus.Desktop
Icon=hermaeus
Terminal=false
Categories=Utility;Development;
StartupWMClass=Hermaeus
DESKTOP

chmod 0644 "$DESKTOP_FILE" "$PNG_ICON_FILE"

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$APPLICATIONS_DIR" >/dev/null 2>&1 || true
fi

if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache "$DATA_HOME/icons/hicolor" >/dev/null 2>&1 || true
fi

echo "Installed Hermaeus desktop launcher to $DESKTOP_FILE"
echo "Installed Hermaeus package to $INSTALL_DIR"
INSTALL

cat > "$PACKAGE_DIR/uninstall-desktop.sh" <<'UNINSTALL'
#!/usr/bin/env bash
set -euo pipefail

SOURCE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PACKAGE_ID="$(basename "$SOURCE_DIR")"
DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
INSTALL_DIR="$DATA_HOME/hermaeus/$PACKAGE_ID"
DESKTOP_FILE="$DATA_HOME/applications/hermaeus.desktop"
PNG_ICON_FILE="$DATA_HOME/icons/hicolor/256x256/apps/hermaeus.png"

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

echo "Removed Hermaeus desktop launcher and installed package."
UNINSTALL

chmod +x "$PACKAGE_DIR/install-desktop.sh" "$PACKAGE_DIR/uninstall-desktop.sh"
rm -rf "$PUBLISH_DIR" "$LOCALAPI_PUBLISH_DIR"

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
