#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -lt 3 ]; then
  echo "Usage: $0 <wcr2> <wz-file-or-dir> <output-dir>" >&2
  echo "Example: $0 dotnet-wcr2 /path/to/Base.wz out/extract" >&2
  exit 1
fi

WCR2="$1"
INPUT="$2"
OUT_DIR="$3"

mkdir -p "$OUT_DIR"

extract_path() {
  local wz_path="$1"
  local safe_name
  safe_name="$(printf '%s' "$wz_path" | tr '/\\' '__')"
  "$WCR2" extract "$INPUT" --path "$wz_path" --out "$OUT_DIR/$safe_name" --recursive --manifest "$OUT_DIR/$safe_name/manifest.json" --json
}

extract_path "String"
extract_path "Item"
extract_path "Character"
