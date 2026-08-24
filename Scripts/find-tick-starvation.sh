#!/usr/bin/env bash
# Find #1491 evidence: ActiveRegionTick and Tick WARN pairs with the same duration.
#
# Usage (from anywhere; repo root is inferred):
#   bash Scripts/find-tick-starvation.sh
#   bash Scripts/find-tick-starvation.sh --watch
#   bash Scripts/find-tick-starvation.sh path/to/Server.log
#   bash Scripts/find-tick-starvation.sh --watch path/to/game.log
#
#   bash Scripts/find-tick-starvation.sh --tail 2000
#   bash Scripts/find-tick-starvation.sh --since 21:34:00
#
# Exit: 0 = starvation pairs found, 1 = logs present but no pairs, 2 = no logs / usage error

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
WATCH=0
TAIL_LINES=0
SINCE_TIME=""
FILES=()

usage() {
  cat <<'EOF'
Find #1491 evidence: ActiveRegionTick + Tick WARN pairs with the same duration.

  bash Scripts/find-tick-starvation.sh
  bash Scripts/find-tick-starvation.sh --watch
  bash Scripts/find-tick-starvation.sh --tail 2000          # last N lines only (after Game restart)
  bash Scripts/find-tick-starvation.sh --since 21:34:00     # lines at/after HH:MM:SS today
  bash Scripts/find-tick-starvation.sh path/to/Server.log

Exit: 0 = starvation pairs found (bug), 1 = no pairs (fix OK or no heavy pass yet), 2 = error
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help)
      usage
      exit 0
      ;;
    -w|--watch|--follow)
      WATCH=1
      shift
      ;;
    --tail)
      TAIL_LINES="${2:?--tail requires a line count}"
      shift 2
      ;;
    --since)
      SINCE_TIME="${2:?--since requires HH:MM:SS}"
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
  # Prefer canonical Server.log; game.log tee often duplicates the same lines.
  for f in \
    "${REPO_ROOT}/AAEmu.Game/bin/"**/Logs/Server.log \
    "${REPO_ROOT}/AAEmu.Game/Logs/Server.log" \
    "${REPO_ROOT}/.server_files/"**/Logs/Server.log
  do
    [[ -f "$f" ]] && echo "$f" && return
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
  echo "Start Game first, walk in main_world, then re-run."
  echo "Typical path:"
  echo "  ${REPO_ROOT}/AAEmu.Game/bin/Debug/net10.0/Logs/Server.log"
  echo "Or pass a file:  bash Scripts/find-tick-starvation.sh /path/to/Server.log"
  exit 2
fi

echo "Log files:"
for f in "${FILES[@]}"; do
  if [[ -f "$f" ]]; then
    echo "  $f  ($(wc -l < "$f") lines)"
  else
    echo "  $f  (waiting for file)"
  fi
done
[[ "$TAIL_LINES" -gt 0 ]] && echo "Scope: last $TAIL_LINES lines"
[[ -n "$SINCE_TIME" ]] && echo "Scope: since $SINCE_TIME"
echo

# Prints: KIND|timestamp|world_ms|tick_ms|line
# KIND = PAIR | WORLD | TICK
analyze() {
  local since_arg=""
  [[ -n "$SINCE_TIME" ]] && since_arg="-v since=$SINCE_TIME"
  # POSIX awk. Pairs World+Tick lines that share HH:mm:ss and ~same ms.
  awk $since_arg '
    function abs(x) { return x < 0 ? -x : x }
    function ts_of(line) {
      if (match(line, /[0-9][0-9]:[0-9][0-9]:[0-9][0-9]/))
        return substr(line, RSTART, 8)
      return "??:??:??"
    }
    function after_took(line,   s) {
      s = line
      sub(/.* took[ \t]+/, "", s)
      sub(/[^0-9.].*/, "", s)
      return s
    }
    BEGIN {
      if (since != "") use_since = 1
    }
    /ActiveRegionTick took/ {
      ts = ts_of($0)
      if (use_since && ts < since) next
      ms = after_took($0) + 0
      key = ts "|" (++wseq)
      wms[key] = ms
      wts[key] = ts
      wline[key] = $0
      worder[++nw] = key
      next
    }
    /Tick took/ && /to finish/ {
      ts = ts_of($0)
      if (use_since && ts < since) next
      ms = after_took($0) + 0
      key = ts "|" (++tseq)
      tms[key] = ms
      tts[key] = ts
      tline[key] = $0
      torder[++nt] = key
      next
    }
    END {
      # Match each World WARN to any unused Tick WARN in the same second with ~same ms.
      # Keep every Tick in that second; do not overwrite by timestamp.
      for (i = 1; i <= nw; i++) {
        key = worder[i]
        ts = wts[key]
        matched = 0
        for (j = 1; j <= nt; j++) {
          tkey = torder[j]
          if (tkey in used_tick) continue
          if (tts[tkey] != ts) continue
          if (abs(wms[key] - tms[tkey]) < 2.0) {
            printf "PAIR|%s|%s|%s|%s\n", ts, wms[key], tms[tkey], wline[key]
            printf "PAIR|%s|%s|%s|%s\n", ts, wms[key], tms[tkey], tline[tkey]
            used_tick[tkey] = 1
            matched = 1
            break
          }
        }
        if (!matched)
          printf "WORLD|%s|%s||%s\n", ts, wms[key], wline[key]
      }
      for (i = 1; i <= nt; i++) {
        key = torder[i]
        if (!(key in used_tick))
          printf "TICK|%s||%s|%s\n", tts[key], tms[key], tline[key]
      }
    }
  '
}

read_log_input() {
  local f
  for f in "${existing[@]}"; do
    if [[ "$TAIL_LINES" -gt 0 ]]; then
      tail -n "$TAIL_LINES" "$f"
    else
      cat "$f"
    fi
  done
}

print_scan() {
  local pairs=0 worlds=0 ticks=0
  local kind ts wms tms line

  while IFS='|' read -r kind ts wms tms line; do
    case "$kind" in
      PAIR)
        if [[ $((pairs % 2)) -eq 0 ]]; then
          echo "----- STARVATION PAIR (bug)  ts=$ts  World=${wms}ms  Tick=${tms}ms -----"
        fi
        echo "  $line"
        pairs=$((pairs + 1))
        ;;
      WORLD)
        echo "[world-only, Tick not blocked] $line"
        worlds=$((worlds + 1))
        ;;
      TICK)
        echo "[tick-only] $line"
        ticks=$((ticks + 1))
        ;;
    esac
  done

  echo
  echo "Summary:"
  echo "  starvation pairs : $((pairs / 2))   (ActiveRegionTick + Tick, same ms → bug is reproducing)"
  echo "  world-only WARN  : $worlds   (slow scan, but tick thread was NOT blocked — expected AFTER the fix)"
  echo "  tick-only WARN   : $ticks"
}

if [[ "$WATCH" -eq 1 ]]; then
  echo "Watching (Ctrl+C to stop). Walk in main_world; pairs print when they appear."
  echo
  last_world_ts=""
  last_world_ms=""
  last_world_line=""
  # shellcheck disable=SC2086
  tail -n 0 -F "${FILES[@]}" 2>/dev/null | while IFS= read -r line; do
    if [[ "$line" =~ ActiveRegionTick\ took[[:space:]]+([0-9]+) ]]; then
      last_world_ms="${BASH_REMATCH[1]}"
      if [[ "$line" =~ ([0-9]{2}:[0-9]{2}:[0-9]{2}) ]]; then
        last_world_ts="${BASH_REMATCH[1]}"
      else
        last_world_ts=""
      fi
      last_world_line="$line"
      echo "[world] $line"
    elif [[ "$line" =~ Tick\ took[[:space:]]+([0-9]+(\.[0-9]+)?)ms\ to\ finish ]]; then
      tick_ms="${BASH_REMATCH[1]}"
      tick_ts=""
      if [[ "$line" =~ ([0-9]{2}:[0-9]{2}:[0-9]{2}) ]]; then
        tick_ts="${BASH_REMATCH[1]}"
      fi
      if [[ -n "$last_world_ms" && "$tick_ts" == "$last_world_ts" ]]; then
        awk -v w="$last_world_ms" -v t="$tick_ms" 'BEGIN { d=w-t; if (d<0) d=-d; exit !(d<2.0) }'
        if [[ $? -eq 0 ]]; then
          echo "***** STARVATION PAIR (bug)  ${last_world_ms} ms / ${tick_ms} ms *****"
          echo "  $last_world_line"
          echo "  $line"
        else
          echo "[tick] $line"
        fi
      else
        echo "[tick] $line"
      fi
    fi
  done
  exit 0
fi

existing=()
for f in "${FILES[@]}"; do
  [[ -f "$f" ]] && existing+=("$f")
done
if [[ ${#existing[@]} -eq 0 ]]; then
  echo "None of the log files exist yet. Start Game, then re-run (or use --watch)."
  exit 2
fi

tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT
read_log_input | analyze > "$tmp"
print_scan < "$tmp"
pair_lines=$(grep -c '^PAIR|' "$tmp" || true)
if [[ "$pair_lines" -gt 0 ]]; then
  echo
  echo "Bug IS reproducing (sync ActiveRegionTick blocking TickLoop)."
  exit 0
fi
echo
echo "No starvation pairs in scanned scope."
echo "Long ActiveRegionTick + no matching Tick took = fix is working (or no heavy pass yet)."
exit 1
