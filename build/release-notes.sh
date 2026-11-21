#!/usr/bin/env bash
# Prints the CHANGELOG.md section of one version (the lines under "## [x.y.z]").
# Usage: release-notes.sh 1.0.0 [CHANGELOG.md]
set -euo pipefail
version="${1#v}"
changelog="${2:-CHANGELOG.md}"
# Plain prefix match: "\[" inside an awk string is not a regex escape, so a regex never matched.
notes=$(awk -v head="## [$version]" '
  index($0, head) == 1 {found=1; next}
  found && index($0, "## [") == 1 {exit}
  found {print}' "$changelog")
if [ -z "$notes" ]; then notes="See CHANGELOG.md"; fi
printf '%s\n' "$notes"
