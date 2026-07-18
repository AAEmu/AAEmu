#!/usr/bin/env bash
# Inventory AAEmu local assets; skip re-download when present.
# Never downloads MEGA/Drive multi-GB content.
# Optional: fetch GitHub launcher when missing; open browser for HitL links.
#
# Usage (from repo root or any cwd):
#   ./scripts/test-aaemu-assets.sh
#   ./scripts/test-aaemu-assets.sh --fetch-launcher
#   ./scripts/test-aaemu-assets.sh --open-missing
#   ./scripts/test-aaemu-assets.sh --repo-root /path/to/AAEmu

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../../.." && pwd)"
FETCH_LAUNCHER=0
OPEN_MISSING=0
MIN_GAME_PAK=$((1024 * 1024 * 1024))   # 1 GiB
MIN_COMPACT=$((1024 * 1024))           # 1 MiB

URL_CLIENT_MEGA="https://mega.nz/folder/GnwjQCrZ#WNWzX_lDvkzCqoTtt7I42Q"
URL_CLIENT_MEGA_ALT="https://mega.nz/folder/C3Q0WQjT#vRUethZLPiYSo2B4nE_etg"
URL_COMPACT_MEGA="https://mega.nz/file/6vxlAZiJ#Xb8VWnG4rFRRklxw6LZaYBPdZvC6j73xLPKIPAK80S8"
URL_LAUNCHER="https://github.com/ZeromusXYZ/AAEmu-Launcher/releases"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --repo-root)
      REPO_ROOT="$(cd "$2" && pwd)"
      shift 2
      ;;
    --fetch-launcher|--fetch-launcher-if-missing)
      FETCH_LAUNCHER=1
      shift
      ;;
    --open-missing|--open-missing-download-pages)
      OPEN_MISSING=1
      shift
      ;;
    -h|--help)
      sed -n '2,12p' "$0"
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 2
      ;;
  esac
done

CLIENT_FILES="${REPO_ROOT}/.client_files"

# Result accumulators (parallel arrays)
declare -a R_NAME=()
declare -a R_STATUS=()
declare -a R_DETAIL=()
declare -a R_PATH=()

add_result() {
  R_NAME+=("$1")
  R_STATUS+=("$2")
  R_DETAIL+=("$3")
  R_PATH+=("${4:-}")
}

format_size() {
  local n=$1
  if (( n >= 1073741824 )); then
    awk -v n="$n" 'BEGIN { printf "%.2f GB", n/1073741824 }'
  elif (( n >= 1048576 )); then
    awk -v n="$n" 'BEGIN { printf "%.2f MB", n/1048576 }'
  elif (( n >= 1024 )); then
    awk -v n="$n" 'BEGIN { printf "%.2f KB", n/1024 }'
  else
    echo "${n} B"
  fi
}

file_size() {
  # portable: GNU stat and BSD stat
  local f=$1
  if stat -c%s "$f" >/dev/null 2>&1; then
    stat -c%s "$f"
  else
    stat -f%z "$f"
  fi
}

find_game_pak() {
  local candidates=(
    "${CLIENT_FILES}/ArcheAge 1.2 (r208022) for AAEmu/game_pak"
    "${CLIENT_FILES}/ArcheAge 1.2.4.13 (r208022) for AAEmu/game_pak"
  )
  local c
  for c in "${candidates[@]}"; do
    if [[ -f "$c" ]]; then
      echo "$c"
      return 0
    fi
  done
  if [[ -d "$CLIENT_FILES" ]]; then
    # first match
    local found
    found="$(find "$CLIENT_FILES" -type f -name 'game_pak' 2>/dev/null | head -n1 || true)"
    if [[ -n "${found}" ]]; then
      echo "$found"
      return 0
    fi
  fi
  return 1
}

find_archeage_exe() {
  local prefer_root=${1:-}
  if [[ -n "$prefer_root" && -f "${prefer_root}/bin32/archeage.exe" ]]; then
    echo "${prefer_root}/bin32/archeage.exe"
    return 0
  fi
  if [[ -d "$CLIENT_FILES" ]]; then
    local found
    found="$(find "$CLIENT_FILES" -type f -name 'archeage.exe' 2>/dev/null | head -n1 || true)"
    if [[ -n "${found}" ]]; then
      echo "$found"
      return 0
    fi
  fi
  return 1
}

find_launcher_exe() {
  local prefer="${CLIENT_FILES}/launcher/AAEmu.Launcher/AAEmu.Launcher.exe"
  if [[ -f "$prefer" ]]; then
    echo "$prefer"
    return 0
  fi
  # Wine/native: also accept AAEmu.Launcher without .exe if ever published
  if [[ -d "$CLIENT_FILES" ]]; then
    local found
    found="$(find "$CLIENT_FILES" -type f \( -name 'AAEmu.Launcher.exe' -o -name 'AAEmu.Launcher' \) 2>/dev/null | head -n1 || true)"
    if [[ -n "${found}" ]]; then
      echo "$found"
      return 0
    fi
  fi
  return 1
}

list_archives() {
  if [[ ! -d "$CLIENT_FILES" ]]; then
    return 0
  fi
  find "$CLIENT_FILES" -maxdepth 1 -type f \( \
    -iname '*.7z' -o -iname '*.zip' -o -iname '*.rar' -o \
    -iname '*ArcheAge*' -o -iname '*compact*' -o -iname '*Launcher*' \
  \) 2>/dev/null || true
}

open_url() {
  local url=$1
  if command -v xdg-open >/dev/null 2>&1; then
    xdg-open "$url" >/dev/null 2>&1 || true
  elif command -v open >/dev/null 2>&1; then
    open "$url" >/dev/null 2>&1 || true
  elif command -v wslview >/dev/null 2>&1; then
    wslview "$url" >/dev/null 2>&1 || true
  else
    echo "  (open manually) $url"
  fi
}

# --- Client ---
PAK=""
if PAK="$(find_game_pak)"; then
  PAK_SIZE="$(file_size "$PAK")"
  PAK_ROOT="$(dirname "$PAK")"
  EXE=""
  EXE="$(find_archeage_exe "$PAK_ROOT" || true)"
  SIZE_TXT="$(format_size "$PAK_SIZE")"
  if (( PAK_SIZE >= MIN_GAME_PAK )) && [[ -n "$EXE" ]]; then
    add_result "client" "OK" "game_pak ${SIZE_TXT}; archeage.exe present - skip re-download" "$PAK"
  elif (( PAK_SIZE < MIN_GAME_PAK )); then
    add_result "client" "PARTIAL" "game_pak too small (${SIZE_TXT}); re-download or extract may be needed" "$PAK"
  else
    add_result "client" "PARTIAL" "game_pak present but archeage.exe missing - finish extract (Windows client)" "$PAK"
  fi
else
  ARCHIVES="$(list_archives | grep -E 'ArcheAge|r208022|1\.2' || true)"
  if [[ -n "$ARCHIVES" ]]; then
    FIRST="$(echo "$ARCHIVES" | head -n1)"
    add_result "client" "PARTIAL" "archive present, not extracted: ${FIRST}" "$FIRST"
  else
    add_result "client" "MISSING" "Need ArcheAge 1.2 (r208022) client - HitL MEGA/Drive download" ""
  fi
fi

# --- compact.sqlite3 ---
COMPACT="${REPO_ROOT}/AAEmu.Game/Data/compact.sqlite3"
if [[ -f "$COMPACT" ]]; then
  C_SIZE="$(file_size "$COMPACT")"
  SIZE_TXT="$(format_size "$C_SIZE")"
  if (( C_SIZE >= MIN_COMPACT )); then
    add_result "compact.sqlite3" "OK" "${SIZE_TXT} - skip re-download" "$COMPACT"
  else
    add_result "compact.sqlite3" "PARTIAL" "file too small (${SIZE_TXT})" "$COMPACT"
  fi
else
  C_ARCH="$(list_archives | grep -Ei 'compact|Server_Compact' || true)"
  if [[ -n "$C_ARCH" ]]; then
    FIRST="$(echo "$C_ARCH" | head -n1)"
    add_result "compact.sqlite3" "PARTIAL" "archive present; extract and copy to AAEmu.Game/Data/compact.sqlite3: ${FIRST}" "$FIRST"
  else
    add_result "compact.sqlite3" "MISSING" "Need compact.sqlite3 - HitL MEGA download, then place under AAEmu.Game/Data/" ""
  fi
fi

# --- Launcher ---
LAUNCHER=""
if LAUNCHER="$(find_launcher_exe)"; then
  add_result "launcher" "OK" "present - skip re-download" "$LAUNCHER"
elif [[ "$FETCH_LAUNCHER" -eq 1 ]]; then
  echo "Launcher missing - fetching latest GitHub release..."
  mkdir -p "${CLIENT_FILES}/launcher"
  DEST_ARCHIVE="${CLIENT_FILES}/AAEmu.Launcher-latest.7z"
  EXTRACT_DIR="${CLIENT_FILES}/launcher"
  set +e
  FETCH_OK=0
  if command -v gh >/dev/null 2>&1; then
    gh release download --repo ZeromusXYZ/AAEmu-Launcher --pattern "*.7z" --dir "$CLIENT_FILES" --clobber
    GOT="$(ls -t "${CLIENT_FILES}"/AAEmu.Launcher*.7z 2>/dev/null | head -n1 || true)"
    if [[ -n "$GOT" ]]; then
      DEST_ARCHIVE="$GOT"
      FETCH_OK=1
    fi
  else
    API_JSON="$(curl -fsSL -H "User-Agent: aaemu-setup" \
      https://api.github.com/repos/ZeromusXYZ/AAEmu-Launcher/releases/latest 2>/dev/null || true)"
    DL_URL="$(echo "$API_JSON" | grep -oE 'https://[^"]+\.7z' | head -n1 || true)"
    if [[ -n "$DL_URL" ]]; then
      if curl -fsSL -o "$DEST_ARCHIVE" "$DL_URL"; then
        FETCH_OK=1
      fi
    fi
  fi
  if [[ "$FETCH_OK" -eq 1 ]]; then
    if command -v 7z >/dev/null 2>&1; then
      7z x "$DEST_ARCHIVE" "-o${EXTRACT_DIR}" -y >/dev/null
    elif command -v 7za >/dev/null 2>&1; then
      7za x "$DEST_ARCHIVE" "-o${EXTRACT_DIR}" -y >/dev/null
    else
      echo "Downloaded ${DEST_ARCHIVE} but no 7z found - extract manually to .client_files/launcher/"
    fi
    if LAUNCHER="$(find_launcher_exe)"; then
      add_result "launcher" "OK" "fetched and extracted" "$LAUNCHER"
    else
      add_result "launcher" "PARTIAL" "archive downloaded; extract to .client_files/launcher/" "$DEST_ARCHIVE"
    fi
  else
    add_result "launcher" "MISSING" "fetch failed - ${URL_LAUNCHER}" ""
  fi
  set -u
else
  add_result "launcher" "MISSING" "AAEmu.Launcher.exe not found (use --fetch-launcher or HitL GitHub)" ""
fi

# --- Report ---
echo ""
echo "AAEmu asset inventory - ${REPO_ROOT}"
echo "------------------------------------------------------------------------"
MISSING=0
PARTIAL=0
HAS_CLIENT_ISSUE=0
HAS_COMPACT_ISSUE=0
HAS_LAUNCHER_ISSUE=0

i=0
while [[ $i -lt ${#R_NAME[@]} ]]; do
  name="${R_NAME[$i]}"
  status="${R_STATUS[$i]}"
  detail="${R_DETAIL[$i]}"
  path="${R_PATH[$i]}"
  case "$status" in
    OK) color='\033[0;32m' ;;
    PARTIAL) color='\033[0;33m'; PARTIAL=1 ;;
    *) color='\033[0;31m'; MISSING=1 ;;
  esac
  nocolor='\033[0m'
  if [[ ! -t 1 ]]; then
    color=''; nocolor=''
  fi
  printf "%b[%s] %s%b\n" "$color" "$status" "$name" "$nocolor"
  printf "      %s\n" "$detail"
  if [[ -n "$path" ]]; then
    printf "      %s\n" "$path"
  fi
  if [[ "$status" != "OK" ]]; then
    case "$name" in
      client) HAS_CLIENT_ISSUE=1 ;;
      compact.sqlite3) HAS_COMPACT_ISSUE=1 ;;
      launcher) HAS_LAUNCHER_ISSUE=1 ;;
    esac
  fi
  i=$((i + 1))
done
echo "------------------------------------------------------------------------"

if [[ "$OPEN_MISSING" -eq 1 ]]; then
  if [[ "$HAS_CLIENT_ISSUE" -eq 1 ]]; then
    open_url "$URL_CLIENT_MEGA"
    open_url "$URL_CLIENT_MEGA_ALT"
  fi
  if [[ "$HAS_COMPACT_ISSUE" -eq 1 ]]; then
    open_url "$URL_COMPACT_MEGA"
  fi
  if [[ "$HAS_LAUNCHER_ISSUE" -eq 1 ]]; then
    open_url "$URL_LAUNCHER"
  fi
fi

if [[ "$MISSING" -eq 0 && "$PARTIAL" -eq 0 ]]; then
  echo "All required assets present. Do not re-download multi-GB packages."
  exit 0
fi

echo ""
echo "HitL next steps:"
if [[ "$HAS_CLIENT_ISSUE" -eq 1 ]]; then
  echo "  - Client: ${URL_CLIENT_MEGA}"
  echo "    Save under .client_files/ then extract so game_pak and bin32/archeage.exe exist."
fi
if [[ "$HAS_COMPACT_ISSUE" -eq 1 ]]; then
  echo "  - compact.sqlite3: ${URL_COMPACT_MEGA}"
  echo "    Copy final file to AAEmu.Game/Data/compact.sqlite3"
fi
if [[ "$HAS_LAUNCHER_ISSUE" -eq 1 ]]; then
  echo "  - Launcher: ${URL_LAUNCHER}  (or re-run with --fetch-launcher)"
fi
echo "  Re-run this script after downloads/extracts. Existing OK assets will be skipped."
exit 1
