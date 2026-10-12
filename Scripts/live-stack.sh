#!/usr/bin/env bash
# Local live stack for agents and developers: private MySQL + Rust login/game servers + the real
# ArcheAge 1.2 client started with aaemu-launcher. See AGENT-QUICKSTART.md.
#
#   Scripts/live-stack.sh build      # cargo build --release --bins
#   Scripts/live-stack.sh sync       # copy app/ configs + data into the runtime dirs
#   Scripts/live-stack.sh db-reset   # drop + recreate the two throwaway databases
#   Scripts/live-stack.sh start      # MySQL, login, game; waits until the game registered
#   Scripts/live-stack.sh client     # launch the 1.2 client (ticket login, no typing)
#   Scripts/live-stack.sh status     # what is running + last log lines
#   Scripts/live-stack.sh shot       # screenshot to $LIVE/shots/now.png (view it with an image reader)
#   Scripts/live-stack.sh stop       # client, servers, MySQL
#
# Every path is an environment variable so another machine can override it. Secrets (DB and test
# account passwords) live only in $LIVE/cfg and the runtime Config.Local.json files, never in git.
set -euo pipefail

REPO=$(cd "$(dirname "$0")/.." && pwd)
LIVE=${AAEMU_LIVE:-D:/aaemu-live}                         # runtime dirs, logs, MySQL data
BIN=${AAEMU_BIN:-D:/aaemu-target/main/release}            # cargo target dir (see rust/.cargo/config.toml)
MYSQL_BIN=${AAEMU_MYSQL_BIN:-/c/Program Files/MySQL/MySQL Server 8.0/bin}
MYSQL_PORT=${AAEMU_MYSQL_PORT:-3317}                      # private instance; NEVER the user's 3306
CLIENT=${AAEMU_CLIENT:-D:/AA1.2/bin32/archeage.exe}       # read-only client install
LOGIN=${AAEMU_LOGIN:-127.0.0.1:21237}
CLIENT_USER=${AAEMU_USER:-clienttest}
PWFILE=${AAEMU_PASSWORD_FILE:-$LIVE/cfg/client-test-password.txt}
DRIVER_DLL=${AAEMU_DRIVER_DLL:-$(dirname "$BIN")/aadriver/aadriver.dll}

MYSQL=("$MYSQL_BIN/mysql.exe" -h127.0.0.1 -P"$MYSQL_PORT" -uroot)
running() { tasklist 2>/dev/null | grep -qi "^$1"; }
mysql_up() { "${MYSQL[@]}" -N -e "select 1" >/dev/null 2>&1; }

cmd_build() { (cd "$REPO/rust" && cargo build --release --bins); }

cmd_sync() {
  local app="$REPO/rust/crates/aaemu-game/app"
  mkdir -p "$LIVE/rs-login" "$LIVE/rs-game/Data" "$LIVE/logs" "$LIVE/shots"
  cp "$REPO/rust/crates/aaemu-login/app/Config.json" "$LIVE/rs-login/"
  cp "$app/Config.json" "$LIVE/rs-game/"
  rm -rf "$LIVE/rs-game/Configurations" "$LIVE/rs-game/Scripts"
  cp -r "$app/Configurations" "$app/Scripts" "$LIVE/rs-game/"
  cp -r "$app/Data/." "$LIVE/rs-game/Data/"   # includes the git-ignored compact.sqlite3
  for f in "$LIVE/rs-login/Config.Local.json" "$LIVE/rs-game/Config.Local.json"; do
    [ -f "$f" ] || echo "WARNING: missing $f (ports, DB, ClientData) - see rust/LIVE-TESTING.md section 2"
  done
  echo "synced app/ into $LIVE/rs-login and $LIVE/rs-game"
}

cmd_mysql() {
  if mysql_up; then return; fi
  ("$MYSQL_BIN/mysqld.exe" --defaults-file="$LIVE/mysql/my.ini" >/dev/null 2>&1 &)
  for _ in $(seq 1 60); do mysql_up && return; sleep 1; done
  echo "MySQL did not start; see $LIVE/logs/mysqld.err" >&2; exit 1
}

cmd_db_reset() {
  cmd_mysql
  if running aaemu-game.exe || running aaemu-login.exe; then
    echo "stop the servers first (Scripts/live-stack.sh stop)" >&2; exit 1
  fi
  for k in login game; do
    "${MYSQL[@]}" -e "DROP DATABASE IF EXISTS rs_$k; CREATE DATABASE rs_$k CHARACTER SET utf8mb4"
    # first two lines select the aaemu_* schema; the sed is for MySQL < 8.0.29
    tail -n +3 "$REPO/SQL/aaemu_$k.sql" | sed 's/CREATE TRIGGER IF NOT EXISTS/CREATE TRIGGER/' \
      | "${MYSQL[@]}" "rs_$k"
    echo "recreated rs_$k"
  done
}

cmd_start() {
  cmd_mysql
  if ! running aaemu-login.exe; then
    (cd "$LIVE/rs-login" && NO_COLOR=1 RUST_LOG=${RUST_LOG:-info} "$BIN/aaemu-login.exe" \
      > "$LIVE/logs/rs-login.log" 2>&1 &)
    sleep 3
  fi
  if ! running aaemu-game.exe; then
    (cd "$LIVE/rs-game" && NO_COLOR=1 RUST_LOG=${RUST_LOG:-info} "$BIN/aaemu-game.exe" "$LIVE/rs-game" \
      > "$LIVE/logs/rs-game.log" 2>&1 &)
  fi
  echo "waiting for the game server (data + physics load takes ~1 min)..."
  for _ in $(seq 1 180); do
    if grep -q "Successfully registered on LoginServer" "$LIVE/logs/rs-game.log" 2>/dev/null; then
      echo "stack ready: login $LOGIN, game registered"; return
    fi
    running aaemu-game.exe || { echo "game server exited; tail $LIVE/logs/rs-game.log" >&2; exit 1; }
    sleep 2
  done
  echo "timed out waiting for registration; check $LIVE/logs/rs-game.log" >&2; exit 1
}

cmd_client() {
  running archeage.exe && { echo "client already running"; return; }
  local driver_args=()
  if [ -f "$DRIVER_DLL" ]; then
    driver_args=(--driver-dll "$(cygpath -w "$DRIVER_DLL" 2>/dev/null || echo "$DRIVER_DLL")")
  fi
  ("$BIN/aaemu-launcher.exe" --client "$(cygpath -w "$CLIENT" 2>/dev/null || echo "$CLIENT")" \
     --login "$LOGIN" --user "$CLIENT_USER" --password-file "$PWFILE" \
     "${driver_args[@]}" \
     > "$LIVE/logs/launcher.log" 2>&1 &)
  sleep 8; cat "$LIVE/logs/launcher.log"
  echo "login appears in rs-login.log as '$CLIENT_USER connected.' after client loading"
}

cmd_status() {
  # (the machine may run other mysqld services, so probe the private port instead of the process)
  if mysql_up; then echo "UP    mysql :$MYSQL_PORT"; else echo "down  mysql :$MYSQL_PORT"; fi
  for p in aaemu-login.exe aaemu-game.exe archeage.exe; do
    if running "$p"; then echo "UP    $p"; else echo "down  $p"; fi
  done
  for f in rs-login rs-game; do echo "--- $f.log"; tail -3 "$LIVE/logs/$f.log" 2>/dev/null | cut -c1-160; done
}

cmd_shot() {
  powershell -NoProfile -Command "Add-Type -AssemblyName System.Windows.Forms,System.Drawing; \
\$b=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds; \$bmp=New-Object System.Drawing.Bitmap \$b.Width,\$b.Height; \
[System.Drawing.Graphics]::FromImage(\$bmp).CopyFromScreen(\$b.Location,[System.Drawing.Point]::Empty,\$b.Size); \
(New-Object System.Drawing.Bitmap \$bmp,(\$b.Width/2),(\$b.Height/2)).Save('$(cygpath -w "$LIVE/shots/now.png" 2>/dev/null || echo "$LIVE/shots/now.png")')"
  echo "$LIVE/shots/now.png"
}

cmd_stop() {
  taskkill //F //IM archeage.exe >/dev/null 2>&1 || true
  taskkill //F //IM aaemu-game.exe >/dev/null 2>&1 || true
  taskkill //F //IM aaemu-login.exe >/dev/null 2>&1 || true
  if mysql_up; then "${MYSQL[@]}" -e "SHUTDOWN"; fi
  echo "stopped"
}

case "${1:-}" in
  build) cmd_build ;; sync) cmd_sync ;; db-reset) cmd_db_reset ;; start) cmd_start ;;
  client) cmd_client ;; status) cmd_status ;; shot) cmd_shot ;; stop) cmd_stop ;;
  *) sed -n '2,13p' "$0"; exit 1 ;;
esac
