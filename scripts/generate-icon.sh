#!/usr/bin/env bash
# generate-icon.sh — Converts AppIcon-1024.png → AppIcon.icns
#
# Prerequisites (all built into macOS / Xcode CLI tools):
#   sips      — resize PNGs
#   iconutil  — build .icns from an iconset
#
# Usage:
#   python3 scripts/create-icon.py   # create the 1024×1024 source PNG first
#   bash scripts/generate-icon.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ASSETS="$SCRIPT_DIR/../src/VerseKit.App/Assets"
SOURCE="$ASSETS/AppIcon-1024.png"

if [ ! -f "$SOURCE" ]; then
    echo "Error: $SOURCE not found."
    echo "Create it first:  python3 scripts/create-icon.py"
    exit 1
fi

ICONSET="$ASSETS/AppIcon.iconset"
rm -rf "$ICONSET"
mkdir -p "$ICONSET"

echo "Resizing PNG variants…"
sips -z 16   16   "$SOURCE" --out "$ICONSET/icon_16x16.png"      >/dev/null
sips -z 32   32   "$SOURCE" --out "$ICONSET/icon_16x16@2x.png"   >/dev/null
sips -z 32   32   "$SOURCE" --out "$ICONSET/icon_32x32.png"      >/dev/null
sips -z 64   64   "$SOURCE" --out "$ICONSET/icon_32x32@2x.png"   >/dev/null
sips -z 128  128  "$SOURCE" --out "$ICONSET/icon_128x128.png"    >/dev/null
sips -z 256  256  "$SOURCE" --out "$ICONSET/icon_128x128@2x.png" >/dev/null
sips -z 256  256  "$SOURCE" --out "$ICONSET/icon_256x256.png"    >/dev/null
sips -z 512  512  "$SOURCE" --out "$ICONSET/icon_256x256@2x.png" >/dev/null
sips -z 512  512  "$SOURCE" --out "$ICONSET/icon_512x512.png"    >/dev/null
cp "$SOURCE"                       "$ICONSET/icon_512x512@2x.png"

echo "Building .icns…"
if ! iconutil -c icns "$ICONSET" -o "$ASSETS/AppIcon.icns"; then
    echo "iconutil rejected the iconset; falling back to tiff2icns…"
    tiff2icns "$SOURCE" "$ASSETS/AppIcon.icns"
fi
rm -rf "$ICONSET"

echo "Done → $ASSETS/AppIcon.icns"
