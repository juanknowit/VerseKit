#!/usr/bin/env bash
# dev-run.sh — Build everything, install plugins, and launch the app.
# Run from anywhere: bash scripts/dev-run.sh
set -euo pipefail

BASE="$(cd "$(dirname "$0")/.." && pwd)"
PLUGIN_INSTALL="$HOME/.local/share/versekit/plugins"

echo "==> Building solution..."
dotnet build "$BASE/VerseKit.slnx" -c Debug --nologo -v q

echo ""
echo "==> Installing plugins..."
for plugin in WebResourcesManager MetadataBrowser; do
    PLUGIN_SRC="$BASE/plugins/$plugin/bin/Debug/net10.0"
    PLUGIN_DST="$PLUGIN_INSTALL/$plugin"
    mkdir -p "$PLUGIN_DST"
    cp -r "$PLUGIN_SRC/." "$PLUGIN_DST/"
    echo "    → $PLUGIN_DST"
done

echo ""
echo "==> Launching VerseKit..."
dotnet run --project "$BASE/src/VerseKit.App" --no-build
