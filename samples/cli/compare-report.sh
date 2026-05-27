#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -lt 4 ]; then
  echo "Usage: $0 <wcr2> <old-wz-file-or-dir> <new-wz-file-or-dir> <output-dir>" >&2
  exit 1
fi

WCR2="$1"
OLD_INPUT="$2"
NEW_INPUT="$3"
OUT_DIR="$4"

mkdir -p "$OUT_DIR"

"$WCR2" compare "$OLD_INPUT" "$NEW_INPUT" --json --out "$OUT_DIR/compare.json"
"$WCR2" compare "$OLD_INPUT" "$NEW_INPUT" --format markdown --out "$OUT_DIR/compare.md"
"$WCR2" compare "$OLD_INPUT" "$NEW_INPUT" --type changed --max-results 500 --json --out "$OUT_DIR/changed.json"

echo "Wrote:"
echo "  $OUT_DIR/compare.json"
echo "  $OUT_DIR/compare.md"
echo "  $OUT_DIR/changed.json"
