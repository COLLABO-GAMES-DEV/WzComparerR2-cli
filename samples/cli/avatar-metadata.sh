#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -lt 3 ]; then
  echo "Usage: $0 <wcr2> <avatar-code> <output-json>" >&2
  echo "Note: CLI avatar image rendering is not implemented yet; this exports item metadata." >&2
  exit 1
fi

WCR2="$1"
AVATAR_CODE="$2"
OUT_JSON="$3"

mkdir -p "$(dirname "$OUT_JSON")"
"$WCR2" avatar unpack --code "$AVATAR_CODE" --json > "$OUT_JSON"

echo "Wrote $OUT_JSON"
