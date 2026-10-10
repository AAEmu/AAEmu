#!/bin/sh
# Empties every table (except `updates`) of the given databases on the private live-test MySQL,
# so that a parity run starts both servers from identical state (account / character / item ids
# then line up). Stop the servers first: they cache ids and names in memory.
#
#   MYSQL="/c/Program Files/MySQL/MySQL Server 8.0/bin/mysql.exe" PORT=3317 \
#     scripts/reset-live-db.sh rs_login rs_game cs_login cs_game
set -eu
MYSQL=${MYSQL:-mysql}
PORT=${PORT:-3317}
USER=${DB_USER:-root}
for db in "$@"; do
  # (the Windows mysql client ends lines with CR LF)
  tables=$("$MYSQL" -h127.0.0.1 -P"$PORT" -u"$USER" -N -B -e \
    "SELECT table_name FROM information_schema.tables WHERE table_schema='$db' AND table_type='BASE TABLE' AND table_name<>'updates'" \
    | tr -d '\015')
  sql="SET FOREIGN_KEY_CHECKS=0;"
  for t in $tables; do sql="$sql TRUNCATE TABLE \`$t\`;"; done
  sql="$sql SET FOREIGN_KEY_CHECKS=1;"
  "$MYSQL" -h127.0.0.1 -P"$PORT" -u"$USER" "$db" -e "$sql"
  echo "reset $db ($(echo $tables | wc -w) tables)"
done
