#!/usr/bin/env python3
"""Build a compact.sqlite3 content SQL file from two client compact databases.

Usage:
  python generate_delta.py --old compact_r584.sqlite3 --new compact_r589.sqlite3 \\
      --out 2026-09-03_compact_r584_to_r589.sql

The SQL is meant for CompactSqliteUpdater / apply_compact_sql.py. It updates
matching primary keys, inserts new keys, and only deletes keys listed in the
old file that the new file dropped (hero_schedules in the 584->589 set).
Operator-only tables and extra rows are left alone.
"""
from __future__ import annotations

import argparse
import sqlite3
from pathlib import Path


def sql_ident(name: str) -> str:
    return '"' + name.replace('"', '""') + '"'


def sql_literal(value) -> str:
    if value is None:
        return "NULL"
    if isinstance(value, bytes):
        return "X'" + value.hex() + "'"
    if isinstance(value, bool):
        return "'t'" if value else "'f'"
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        return str(value)
    text = str(value).replace("'", "''")
    return "'" + text + "'"


def table_info(conn: sqlite3.Connection, schema: str, table: str):
    rows = list(conn.execute(f"PRAGMA {schema}.table_info({sql_ident(table)})"))
    cols = [r[1] for r in rows]
    pk = [r[1] for r in rows if r[5]]
    return cols, pk


def list_tables(conn: sqlite3.Connection, schema: str) -> set[str]:
    q = (
        f"SELECT name FROM {schema}.sqlite_master "
        "WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '_aaemu_%'"
    )
    return {r[0] for r in conn.execute(q)}


def emit_update(table: str, pk: list[str], changed: dict, key: tuple) -> str:
    sets = ", ".join(f"{sql_ident(c)} = {sql_literal(v)}" for c, v in changed.items())
    where = " AND ".join(f"{sql_ident(c)} = {sql_literal(v)}" for c, v in zip(pk, key))
    return f"UPDATE {sql_ident(table)} SET {sets} WHERE {where};"


def emit_insert(table: str, cols: list[str], row: tuple, pk: list[str]) -> str:
    col_sql = ", ".join(sql_ident(c) for c in cols)
    val_sql = ", ".join(sql_literal(v) for v in row)
    pk_sql = ", ".join(sql_ident(c) for c in pk)
    excluded = ", ".join(
        f"{sql_ident(c)} = excluded.{sql_ident(c)}" for c in cols if c not in pk
    )
    conflict = (
        f" ON CONFLICT({pk_sql}) DO UPDATE SET {excluded}" if excluded else " ON CONFLICT DO NOTHING"
    )
    return f"INSERT INTO {sql_ident(table)} ({col_sql}) VALUES ({val_sql}){conflict};"


def emit_delete(table: str, pk: list[str], key: tuple) -> str:
    where = " AND ".join(f"{sql_ident(c)} = {sql_literal(v)}" for c, v in zip(pk, key))
    return f"DELETE FROM {sql_ident(table)} WHERE {where};"


def generate(old_path: Path, new_path: Path, out_path: Path, label: str) -> None:
    conn = sqlite3.connect(old_path)
    conn.execute("ATTACH DATABASE ? AS nw", (str(new_path),))
    old_tables = list_tables(conn, "main")
    new_tables = list_tables(conn, "nw")
    shared = sorted(old_tables & new_tables)

    lines = [
        f"-- AAEmu compact content: {label}",
        "-- Client compact row delta. Not a MySQL SQL/updates script.",
        "-- Apply with CompactSqliteUpdater (Game/World boot) or apply_compact_sql.py.",
        "-- Extra local tables and extra local rows are kept. Matching keys are updated.",
        "-- compact_table sections are skipped when the target database has no such table.",
        "",
    ]

    stats = []
    for table in shared:
        old_cols, old_pk = table_info(conn, "main", table)
        new_cols, new_pk = table_info(conn, "nw", table)
        pk = old_pk or new_pk
        cols = [c for c in new_cols if c in old_cols]
        if not pk or not cols:
            continue
        if any(c not in cols for c in pk):
            continue

        pk_sql = ",".join(sql_ident(c) for c in pk)
        col_sql = ",".join(sql_ident(c) for c in cols)
        insert_keys = list(
            conn.execute(
                f"SELECT {pk_sql} FROM nw.{sql_ident(table)} "
                f"EXCEPT SELECT {pk_sql} FROM main.{sql_ident(table)}"
            )
        )
        delete_keys = list(
            conn.execute(
                f"SELECT {pk_sql} FROM main.{sql_ident(table)} "
                f"EXCEPT SELECT {pk_sql} FROM nw.{sql_ident(table)}"
            )
        )

        join = " AND ".join(f"o.{sql_ident(c)} = n.{sql_ident(c)}" for c in pk)
        non_pk = [c for c in cols if c not in pk]
        updates = []
        if non_pk:
            diff = " OR ".join(f"o.{sql_ident(c)} IS NOT n.{sql_ident(c)}" for c in non_pk)
            select = ", ".join(
                [f"o.{sql_ident(c)}" for c in pk]
                + [f"o.{sql_ident(c)}" for c in non_pk]
                + [f"n.{sql_ident(c)}" for c in non_pk]
            )
            for row in conn.execute(
                f"SELECT {select} FROM main.{sql_ident(table)} o "
                f"JOIN nw.{sql_ident(table)} n ON {join} WHERE {diff}"
            ):
                key = row[: len(pk)]
                old_vals = row[len(pk) : len(pk) + len(non_pk)]
                new_vals = row[len(pk) + len(non_pk) :]
                changed = {
                    col: new_v
                    for col, old_v, new_v in zip(non_pk, old_vals, new_vals)
                    if old_v != new_v
                }
                if changed:
                    updates.append((key, changed))

        if not insert_keys and not delete_keys and not updates:
            continue

        stats.append((table, len(insert_keys), len(delete_keys), len(updates)))
        lines.append(f"-- compact_table: {table}")
        for key in delete_keys:
            lines.append(emit_delete(table, pk, key))
        for key, changed in updates:
            lines.append(emit_update(table, pk, changed, key))
        for key in insert_keys:
            where = " AND ".join(f"{sql_ident(c)} = {sql_literal(v)}" for c, v in zip(pk, key))
            row = conn.execute(
                f"SELECT {col_sql} FROM nw.{sql_ident(table)} WHERE {where}"
            ).fetchone()
            if row is not None:
                lines.append(emit_insert(table, cols, row, pk))
        lines.append("")

    header_stats = ["-- Tables:"]
    header_stats.extend(
        f"--   {name}: insert={ins} delete={dele} update={upd}" for name, ins, dele, upd in stats
    )
    header_stats.append("")
    text = "\n".join(lines[:6] + header_stats + lines[6:] + [""])
    out_path.write_text(text, encoding="utf-8", newline="\n")
    print(f"wrote {out_path} ({out_path.stat().st_size} bytes, {len(stats)} tables)")
    for name, ins, dele, upd in stats:
        print(f"  {name}: +{ins} -{dele} ~{upd}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--old", required=True, type=Path, help="Older compact.sqlite3 (e.g. r584)")
    parser.add_argument("--new", required=True, type=Path, help="Newer compact.sqlite3 (e.g. r589)")
    parser.add_argument("--out", required=True, type=Path, help="SQL file to write")
    parser.add_argument("--label", default="client compact revision 584 -> 589")
    args = parser.parse_args()
    generate(args.old, args.new, args.out, args.label)


if __name__ == "__main__":
    main()
