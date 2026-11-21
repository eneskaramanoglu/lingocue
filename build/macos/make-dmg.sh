#!/usr/bin/env bash
# Puts LingoCue.app and a link to /Applications into a compressed disk image.
# Usage: make-dmg.sh <LingoCue.app> <output.dmg>
set -euo pipefail

app="$1"
dmg="$2"

staging="$(mktemp -d)"
cp -R "$app" "$staging/"
ln -s /Applications "$staging/Applications"

rm -f "$dmg"
hdiutil create -volname "LingoCue" -srcfolder "$staging" -fs HFS+ -format UDZO -ov "$dmg"
rm -rf "$staging"

echo "Created $dmg"
