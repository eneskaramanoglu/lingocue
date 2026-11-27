#!/usr/bin/env bash
# Copies Homebrew's libmpv and all of its non-system dependencies into LingoCue.app/Contents/Frameworks
# and rewrites their load paths to @loader_path, so the app runs on Macs without Homebrew.
# Requires: brew install mpv dylibbundler
# Usage: bundle-libmpv.sh <path/to/LingoCue.app>
set -euo pipefail

app="$1"
frameworks="$app/Contents/Frameworks"
brew_prefix="$(brew --prefix)"
libmpv="$(brew --prefix mpv)/lib/libmpv.2.dylib"

if [ ! -f "$libmpv" ]; then
  echo "libmpv not found at $libmpv (brew install mpv)" >&2
  exit 1
fi

mkdir -p "$frameworks"
cp -L "$libmpv" "$frameworks/libmpv.2.dylib"
chmod u+w "$frameworks/libmpv.2.dylib"
install_name_tool -id "@loader_path/libmpv.2.dylib" "$frameworks/libmpv.2.dylib"

# -of overwrite, -b bundle deps, -x fix this file, -d destination, -p install path prefix, -s search path.
dylibbundler -of -b \
  -x "$frameworks/libmpv.2.dylib" \
  -d "$frameworks/" \
  -p "@loader_path/" \
  -s "$brew_prefix/lib"

# dylibbundler adds an LC_RPATH for -p even when the file already has one, and Homebrew's libmpv
# already carries @loader_path/. dyld (macOS 15) refuses to load a file with a duplicate LC_RPATH,
# so keep one copy of each rpath.
rpaths() { otool -l "$1" | awk '/cmd LC_RPATH/ { getline; getline; print $2 }'; }
for lib in "$frameworks"/*.dylib; do
  # -delete_rpath removes one copy per call, so repeat until no path is listed twice.
  while duplicate="$(rpaths "$lib" | sort | uniq -d | sed -n 1p)" && [ -n "$duplicate" ]; do
    install_name_tool -delete_rpath "$duplicate" "$lib" 2>/dev/null
  done
done

# Fail loudly if anything still points into Homebrew.
leftover=0
for lib in "$frameworks"/*.dylib; do
  if otool -L "$lib" | tail -n +2 | grep -E "$brew_prefix|/usr/local/(opt|Cellar)" ; then
    echo "Unbundled Homebrew reference in $lib" >&2
    leftover=1
  fi
done
[ "$leftover" -eq 0 ] || exit 1

count=$(ls "$frameworks"/*.dylib | wc -l | tr -d ' ')
size=$(du -sh "$frameworks" | cut -f1)
echo "Bundled libmpv with $count dylibs ($size) into $frameworks"
