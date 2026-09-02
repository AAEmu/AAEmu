#!/usr/bin/env python3
"""Apply SQL/compact/*_compact_*.sql onto a compact.sqlite3 or game.sqlite3.

Game/World apply the same scripts at boot. Use this for dedicate copies:

  python apply_compact_sql.py --db path/to/compact.sqlite3
  python apply_compact_sql.py --db path/to/game.sqlite3 --sql SQL/compact

Extra local tables and extra local rows are kept. Matching keys are updated.
A missing table section is skipped. Re-runs are no-ops via _aaemu_compact_updates.
"""
from __future__ import annotations

import argparse
import sqlite3
from datetime import datetime, timezone
from pathlib import Path

TABLE_MARKER = "-- compact_table:"
TRACKING = "_aaemu_compact_updates"


def parse_script(script: str) -> list[tuple[str | None, str]]:
    statements: list[tuple[str | None, str]] = []
    buf: list[str] = []
    table: str | None = None
    in_string = False
    i = 0
    n = len(script)

    def flush() -> None:
        text = "".join(buf).strip()
        buf.clear()
        if text:
            statements.append((table, text))

    while i < n:
        ch = script[i]
        if not in_string and ch == "-" and i + 1 < n and script[i + 1] == "-":
            end = i
            while end < n and script[end] not in "\r\n":
                end += 1
            rest = script[i:end].strip()
            if rest.startswith(TABLE_MARKER):
                flush()
                name = rest[len(TABLE_MARKER) :].strip()
                table = name or None
            i = end
            continue
        if ch == "'":
            buf.append(ch)
            if in_string and i + 1 < n and script[i + 1] == "'":
                buf.append(script[i + 1])
                i += 2
                continue
            in_string = not in_string
            i += 1
            continue
        if not in_string and ch == ";":
            flush()
            i += 1
            continue
        buf.append(ch)
        i += 1
    flush()
    return statements


def table_exists(conn: sqlite3.Connection, name: str) -> bool:
    row = conn.execute(
        "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = ? LIMIT 1", (name,)
    ).fetchone()
    return row is not None


def ensure_tracking(conn: sqlite3.Connection) -> None:
    conn.execute(
        f"CREATE TABLE IF NOT EXISTS {TRACKING} ("
        "script_name TEXT NOT NULL PRIMARY KEY, "
        "installed INTEGER NOT NULL, "
        "install_date TEXT NOT NULL, "
        "last_error TEXT NOT NULL)"
    )


def installed_names(conn: sqlite3.Connection) -> set[str]:
    return {
        r[0]
        for r in conn.execute(f"SELECT script_name FROM {TRACKING} WHERE installed = 1")
    }


def record(conn: sqlite3.Connection, name: str, ok: bool, error: str = "") -> None:
    conn.execute(
        f"INSERT INTO {TRACKING} (script_name, installed, install_date, last_error) "
        "VALUES (?, ?, ?, ?) "
        "ON CONFLICT(script_name) DO UPDATE SET "
        "installed = excluded.installed, install_date = excluded.install_date, "
        "last_error = excluded.last_error",
        (name, 1 if ok else 0, datetime.now(timezone.utc).isoformat(), error),
    )


def apply_file(conn: sqlite3.Connection, path: Path) -> tuple[int, int]:
    executed = 0
    skipped = 0
    for table, sql in parse_script(path.read_text(encoding="utf-8")):
        if table and not table_exists(conn, table):
            skipped += 1
            continue
        conn.execute(sql)
        executed += 1
    return executed, skipped


def script_paths(sql_arg: Path) -> list[Path]:
    if sql_arg.is_file():
        return [sql_arg]
    files = sorted(sql_arg.glob("*_compact_*.sql"))
    if not files:
        raise SystemExit(f"no *_compact_*.sql files in {sql_arg}")
    return files


def main() -> None:
    here = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db", required=True, type=Path, help="compact.sqlite3 or game.sqlite3")
    parser.add_argument(
        "--sql",
        type=Path,
        default=here,
        help="SQL/compact directory or a single *_compact_*.sql file",
    )
    args = parser.parse_args()
    if not args.db.is_file():
        raise SystemExit(f"database not found: {args.db}")

    conn = sqlite3.connect(args.db)
    try:
        ensure_tracking(conn)
        done = installed_names(conn)
        for path in script_paths(args.sql):
            name = path.name
            if name in done:
                print(f"already installed: {name}")
                continue
            try:
                executed, skipped = apply_file(conn, path)
                record(conn, name, True)
                conn.commit()
                print(f"installed {name} statements={executed} skipped_missing_table={skipped}")
            except Exception as ex:
                record(conn, name, False, str(ex))
                conn.commit()
                raise SystemExit(f"failed {name}: {ex}") from ex
    finally:
        conn.close()


if __name__ == "__main__":
    main()
