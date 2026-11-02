#!/usr/bin/env bash
# Renders a zone generator's gallery in batch mode (the project must NOT be open in an Editor).
# Usage: Tools/ZoneArt/gallery.sh <generator> <outDir> [extra args, e.g. -zoneOnly 2 -zoneSeed 7 -zoneWidth 960 -zoneHeight 540]
# Prints the gallery report and any compile or runtime errors. Exit code 0 on success.
set -u
GEN="${1:?generator name}"
OUT="${2:?output dir}"
shift 2
PROJECT="$(cd "$(dirname "$0")/../.." && pwd)"
LOG="$OUT/unity.log"
mkdir -p "$OUT"
unity run "$PROJECT" --no-tail --timeout 1500 --log-file "$LOG" -- \
  -executeMethod FriendlyPersonShooter.ZoneArt.EditorTools.ZoneGallery.RunFromCommandLine \
  -zoneGenerator "$GEN" -zoneOut "$OUT" "$@" >/dev/null 2>&1
CODE=$?
grep -E "error CS|\[ZoneGallery\] (FAILED|.*zone [0-9])|Shader error|Exception" "$LOG" | grep -v "^UnityEngine\." | head -60
if [ -f "$OUT/report.md" ] && [ "$CODE" -eq 0 ]; then
  cat "$OUT/report.md"
else
  echo "Gallery run failed (exit $CODE). Full log: $LOG"
  [ "$CODE" -eq 0 ] && CODE=1
fi
exit $CODE
