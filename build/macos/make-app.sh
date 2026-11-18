#!/usr/bin/env bash
# Wraps a self-contained osx-arm64 publish folder into LingoCue.app (unsigned).
# Usage: make-app.sh <publish-dir> <output-dir> <version>
set -euo pipefail

publish_dir="$1"
out_dir="$2"
version="$3"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

app="$out_dir/LingoCue.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

cp -R "$publish_dir"/. "$app/Contents/MacOS/"
sed "s/__VERSION__/$version/g" "$script_dir/Info.plist" > "$app/Contents/Info.plist"
chmod +x "$app/Contents/MacOS/LingoCue"

# libmpv (video preview) and its dependencies go to Contents/Frameworks.
bash "$script_dir/bundle-libmpv.sh" "$app"

# Ad-hoc signature: required for arm64 binaries to launch at all; not a Developer ID signature.
codesign --force --deep --sign - "$app"

echo "Created $app"
