#!/usr/bin/env bash
set -euo pipefail

PROJ="src/Aether.Desktop/Aether.Desktop.csproj"
OUT="dist"

echo "Restoring..."
dotnet restore

echo "Building linux-x64..."
dotnet publish "$PROJ" -c Release -r linux-x64 --self-contained false -o "$OUT/linux-x64"

echo "Building linux-arm64..."
dotnet publish "$PROJ" -c Release -r linux-arm64 --self-contained false -o "$OUT/linux-arm64"

echo "Done — binaries in $OUT/"
