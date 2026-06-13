#!/usr/bin/env bash
# bundle-app.sh — Assembles a macOS .app bundle from a dotnet publish directory.
#
# Usage:
#   bash scripts/bundle-app.sh <publish-dir> <version> <output-dir>
#
# Example (local):
#   bash scripts/bundle-app.sh publish/osx-arm64 0.1.0 dist
#
# The script:
#   1. Creates "VerseKit.app/Contents/{MacOS,Resources}"
#   2. Copies the publish output into Contents/MacOS/
#   3. Moves any bundled plugins/ folder → Contents/Resources/plugins/
#   4. Writes Info.plist with the real version number substituted
#   5. Copies AppIcon.icns into Contents/Resources/ (if present)

set -euo pipefail

PUBLISH_DIR="${1:?Usage: $0 <publish-dir> <version> <output-dir>}"
VERSION="${2:?Missing version argument}"
OUT_DIR="${3:?Missing output-dir argument}"

APP_NAME="VerseKit"
BUNDLE="$OUT_DIR/$APP_NAME.app"
MACOS="$BUNDLE/Contents/MacOS"
RESOURCES="$BUNDLE/Contents/Resources"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$SCRIPT_DIR/.."

echo "==> Assembling \"$APP_NAME.app\" v$VERSION"

# ── 1. Create bundle skeleton ──────────────────────────────────────
rm -rf "$BUNDLE"
mkdir -p "$MACOS" "$RESOURCES"

# ── 2. Copy publish output into MacOS/ ────────────────────────────
cp -r "$PUBLISH_DIR/." "$MACOS/"

# ── 3. Relocate bundled plugins to Resources/ ─────────────────────
if [ -d "$MACOS/plugins" ]; then
    mv "$MACOS/plugins" "$RESOURCES/plugins"
    echo "    plugins → Contents/Resources/plugins"
fi

# ── 4. Write Info.plist (version substituted) ─────────────────────
INFO_SRC="$REPO_ROOT/src/VerseKit.App/Info.plist"
if [ ! -f "$INFO_SRC" ]; then
    echo "Error: $INFO_SRC not found" >&2
    exit 1
fi
sed "s/VERSION_PLACEHOLDER/$VERSION/g" "$INFO_SRC" > "$BUNDLE/Contents/Info.plist"
echo "    Info.plist written (v$VERSION)"

# ── 5. Copy icon ───────────────────────────────────────────────────
ICNS="$REPO_ROOT/src/VerseKit.App/Assets/AppIcon.icns"
if [ -f "$ICNS" ]; then
    cp "$ICNS" "$RESOURCES/AppIcon.icns"
    echo "    AppIcon.icns copied"
else
    echo "    Warning: AppIcon.icns not found — run: bash scripts/generate-icon.sh"
fi

# ── 6. Fix executable permissions ─────────────────────────────────
chmod +x "$MACOS/VerseKit.App"

echo "==> Bundle ready: $BUNDLE"

# ── 7. Zip for release + SHA-256 sidecar ──────────────────────────
# The in-app updater downloads "<name>.zip" and verifies it against
# "<name>.zip.sha256" published on the same GitHub release.
ZIP="$OUT_DIR/VerseKit-$VERSION-osx-arm64.zip"
rm -f "$ZIP" "$ZIP.sha256"
( cd "$OUT_DIR" && ditto -c -k --keepParent "$APP_NAME.app" "$(basename "$ZIP")" )
shasum -a 256 "$ZIP" | awk '{print $1}' > "$ZIP.sha256"
echo "==> Release zip:  $ZIP"
echo "==> Checksum:     $(cat "$ZIP.sha256")"
