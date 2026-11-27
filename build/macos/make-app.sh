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

# App icon: an iconset from the 1024 px master (build/appicon/make-icon.cs), compiled by iconutil.
icon_master="$script_dir/../appicon/lingocue-1024.png"
iconset="$(mktemp -d)/AppIcon.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$icon_master" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  sips -z $((size * 2)) $((size * 2)) "$icon_master" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"

# libmpv (video preview) and its dependencies go to Contents/Frameworks.
bash "$script_dir/bundle-libmpv.sh" "$app"

# Ad-hoc signature: required for arm64 binaries to launch at all; not a Developer ID signature.
codesign --force --deep --sign - "$app"

# The app quits at start-up when libmpv cannot be loaded, and nothing else in the build loads it.
# dlopen it the way the app does, so a broken bundle (e.g. a duplicate LC_RPATH) fails here.
python3 -I -c 'import ctypes, sys; ctypes.CDLL(sys.argv[1])' "$app/Contents/Frameworks/libmpv.2.dylib" ||
  { echo "libmpv.2.dylib in $app does not load" >&2; exit 1; }

echo "Created $app"
