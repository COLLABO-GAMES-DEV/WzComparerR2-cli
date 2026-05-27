#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -lt 4 ]; then
  echo "Usage: $0 <wcr2> <map-wz-file-or-dir> <map-id> <output-dir>" >&2
  echo "Note: CLI map screenshot rendering is not implemented yet; this exports map metadata." >&2
  exit 1
fi

WCR2="$1"
MAP_WZ="$2"
MAP_ID="$3"
OUT_DIR="$4"

mkdir -p "$OUT_DIR"

"$WCR2" map portals "$MAP_WZ" --id "$MAP_ID" --json > "$OUT_DIR/portals.json"
"$WCR2" map life "$MAP_WZ" --id "$MAP_ID" --json > "$OUT_DIR/life.json"
"$WCR2" map objects "$MAP_WZ" --id "$MAP_ID" --json > "$OUT_DIR/objects.json"
"$WCR2" map reactors "$MAP_WZ" --id "$MAP_ID" --json > "$OUT_DIR/reactors.json"

echo "Wrote map metadata to $OUT_DIR"
