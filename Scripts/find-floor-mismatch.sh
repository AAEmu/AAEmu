#!/usr/bin/env bash
# Find Floor ≠ Path mismatches from FloorDebug lines in Server.log.
#
# Enable logging first:
#   World.FloorDebug = true
# Expected line (FloorQuery):
#   Floor src=Terrain ctx=Move xyz=(x,y,z) terrain=133.8 nav=210.0 floor=133.8 deltaNav=76.2
#
# Usage:
#   bash Scripts/find-floor-mismatch.sh
#   bash Scripts/find-floor-mismatch.sh --summary
#   bash Scripts/find-floor-mismatch.sh --tail 5000
#   bash Scripts/find-floor-mismatch.sh --threshold 2.0 path/to/Server.log
#
# Suspects (exit 0):
#   - Move/Spawn/Skill with src=Legacy* (regression after TerrainFirst outdoors)
#   - |floor - terrain| > threshold when terrain != 0 and src is not NavSurface
#
# OK (exit 1): FloorDebug lines present, no suspects.
# Error (exit 2): no logs / usage.
#
# Note: large deltaNav after TerrainFirst outdoors is EXPECTED (we intentionally
# ignore nav vertex Z). This script keys on src and |floor-terrain|, not deltaNav==0.

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
SUMMARY=0
TAIL_LINES=0
THRESHOLD="1.0"
FILES=()

usage() {
  cat <<'USAGE'
Find Floor/terrain mismatches from FloorDebug log lines.

  bash Scripts/find-floor-mismatch.sh
  bash Scripts/find-floor-mismatch.sh --summary
  bash Scripts/find-floor-mismatch.sh --tail 5000
  bash Scripts/find-floor-mismatch.sh --threshold 2.0
  bash Scripts/find-floor-mismatch.sh path/to/Server.log

Exit: 0 = suspects found, 1 = logs OK / no suspects, 2 = error
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help)
      usage
      exit 0
      ;;
    --summary)
      SUMMARY=1
      shift
      ;;
    --tail)
      TAIL_LINES="${2:?--tail requires a line count}"
      shift 2
      ;;
    --threshold)
      THRESHOLD="${2:?--threshold requires a number}"
      shift 2
      ;;
    --)
      shift
      FILES+=("$@")
      break
      ;;
    -*)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
    *)
      FILES+=("$1")
      shift
      ;;
  esac
done

discover_logs() {
  local f
  shopt -s globstar nullglob
  for f in \
    "${REPO_ROOT}/AAEmu.Game/bin/"**/Logs/Server.log \
    "${REPO_ROOT}/AAEmu.Game/Logs/Server.log" \
    "${REPO_ROOT}/.server_files/"**/Logs/Server.log
  do
    [[ -f "$f" ]] && echo "$f" && return 0
  done
  for f in "${REPO_ROOT}/.server_files/logs/game.log" "${REPO_ROOT}/.server_files/logs/"*game*.log; do
    [[ -f "$f" ]] && echo "$f"
  done
  shopt -u globstar nullglob
}

if [[ ${#FILES[@]} -eq 0 ]]; then
  mapfile -t FILES < <(discover_logs | sort -u)
fi

if [[ ${#FILES[@]} -eq 0 ]]; then
  echo "No Game logs found."
  echo "Start Game with World.FloorDebug=true, then re-run."
  echo "Typical path:"
  echo "  ${REPO_ROOT}/AAEmu.Game/bin/Debug/net10.0/Logs/Server.log"
  echo "Or pass a file:  bash Scripts/find-floor-mismatch.sh /path/to/Server.log"
  exit 2
fi

echo "Log files:"
existing=()
for f in "${FILES[@]}"; do
  if [[ -f "$f" ]]; then
    echo "  $f  ($(wc -l < "$f" | tr -d ' ') lines)"
    existing+=("$f")
  else
    echo "  $f  (missing)"
  fi
done
if [[ ${#existing[@]} -eq 0 ]]; then
  echo "None of the log files exist yet."
  exit 2
fi
[[ "$TAIL_LINES" -gt 0 ]] && echo "Scope: last $TAIL_LINES lines"
echo "Threshold |floor-terrain|: $THRESHOLD"
echo

read_logs() {
  local f
  for f in "${existing[@]}"; do
    if [[ "$TAIL_LINES" -gt 0 ]]; then
      tail -n "$TAIL_LINES" "$f"
    else
      cat "$f"
    fi
  done
}

# POSIX awk field extractor: after KEY= until whitespace
# Emit: SRC|CTX|TERRAIN|NAV|FLOOR|DELTANAV|ABS_FT|LINE
parse_floor_lines() {
  # Fields separated by SOH (\001); log line may contain | from NLog layouts.
  awk '
    function abs(x) { return x < 0 ? -x : x }
    function after_eq(line, key,   s, p) {
      s = line
      p = index(s, key "=")
      if (p == 0) return ""
      s = substr(s, p + length(key) + 1)
      if (match(s, /^[^[:space:]]+/))
        return substr(s, RSTART, RLENGTH)
      return ""
    }
    /Floor src=/ {
      line = $0
      src = after_eq(line, "src")
      ctx = after_eq(line, "ctx")
      terrain = after_eq(line, "terrain")
      nav = after_eq(line, "nav")
      floor = after_eq(line, "floor")
      deltaNav = after_eq(line, "deltaNav")
      if (src == "" || floor == "") next
      ft = 0
      if (terrain != "") ft = abs(floor - terrain)
      printf "%s\001%s\001%s\001%s\001%s\001%s\001%.6f\001%s\n", src, ctx, terrain, nav, floor, deltaNav, ft, line
    }
  '
}

tmp="$(mktemp)"
suspects="$(mktemp)"
trap 'rm -f "$tmp" "$suspects"' EXIT

read_logs | parse_floor_lines > "$tmp"
total=$(wc -l < "$tmp" | tr -d ' ')

if [[ "$total" -eq 0 ]]; then
  echo "No FloorDebug lines found (looking for: Floor src=...)."
  echo "Set World.FloorDebug=true, reproduce movement outdoors, then re-run."
  exit 2
fi

awk -v thr="$THRESHOLD" -F'\001' '
  {
    src=$1; ctx=$2; terrain=$3; floor=$5; ft=$7+0; line=$8
    if (src ~ /^Legacy/ && (ctx == "Move" || ctx == "Spawn" || ctx == "Skill")) {
      print "LEGACY_FLOOR\001" line
      next
    }
    if (terrain != "" && (terrain+0) != 0 && ft > thr && src != "NavSurface") {
      print "TERRAIN_DELTA\001" line
      next
    }
  }
' "$tmp" > "$suspects"

suspect_count=$(wc -l < "$suspects" | tr -d ' ')

if [[ "$SUMMARY" -eq 1 ]]; then
  echo "=== Summary ==="
  echo "FloorDebug samples: $total"
  echo "Suspects:           $suspect_count"
  echo
  echo "By src:"
  cut -d$'\001' -f1 "$tmp" | sort | uniq -c | sort -nr
  echo
  echo "By ctx:"
  cut -d$'\001' -f2 "$tmp" | sort | uniq -c | sort -nr
  echo
  awk -F'\001' '
    { v[NR]=$7+0 }
    END {
      n=NR
      if (n==0) { print "abs(floor-terrain): n=0"; exit }
      for (i=2;i<=n;i++) {
        key=v[i]; j=i-1
        while (j>=1 && v[j]>key) { v[j+1]=v[j]; j-- }
        v[j+1]=key
      }
      p50 = v[int((n+1)*0.50)]
      p95 = v[int((n+1)*0.95)]
      if (p95=="") p95=v[n]
      sum=0; for (i=1;i<=n;i++) sum+=v[i]
      printf "abs(floor-terrain): n=%d mean=%.3f p50=%.3f p95=%.3f max=%.3f\n", n, sum/n, p50, p95, v[n]
    }
  ' "$tmp"
  echo
  echo "deltaNav (informational; large outdoors is OK after TerrainFirst):"
  awk -F'\001' '
    { v[NR]=$6+0 }
    END {
      n=NR
      if (n==0) exit
      for (i=2;i<=n;i++) {
        key=v[i]; j=i-1
        while (j>=1 && v[j]>key) { v[j+1]=v[j]; j-- }
        v[j+1]=key
      }
      p50 = v[int((n+1)*0.50)]
      p95 = v[int((n+1)*0.95)]
      if (p95=="") p95=v[n]
      printf "  n=%d p50=%.3f p95=%.3f max=%.3f\n", n, p50, p95, v[n]
    }
  ' "$tmp"
  echo
fi

if [[ "$suspect_count" -gt 0 ]]; then
  echo "=== Suspects ($suspect_count) ==="
  head -n 40 "$suspects" | while IFS=$'\001' read -r kind line; do
    echo "[$kind] $line"
  done
  if [[ "$suspect_count" -gt 40 ]]; then
    echo "... $((suspect_count - 40)) more"
  fi
  echo
  echo "Suspects found (floating / Legacy outdoor Floor)."
  exit 0
fi

echo "No Floor mismatches in scanned scope ($total FloorDebug samples)."
echo "Outdoor Move should show src=Terrain; Legacy* src on Move is a regression."
exit 1
