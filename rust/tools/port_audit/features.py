"""Inventory 1.2 inbound packets and database tables for behavior review.

Usage: python -I rust/tools/port_audit/features.py --compact-db PATH --output PATH
Optional --reference PATH indexes table references in a local decompilation.
The report contains schema, counts and source locations, never row contents.
Source mentions, registrations and test mentions do not prove feature correctness.
"""

import sys

sys.dont_write_bytecode = True

import argparse
import collections
import hashlib
import json
import pathlib
import re
import sqlite3
from contextlib import closing
from datetime import datetime, timezone

ROOT = pathlib.Path(__file__).resolve().parents[3]
GAME = ROOT / "rust/crates/aaemu-game"
WORD = re.compile(r"\b[A-Za-z_]\w*\b")
SQL_TABLE = re.compile(r'\b(?:FROM|JOIN|INTO|UPDATE)\s+["`\[]?(\w+)', re.I)
GAP = re.compile(r"TODO|todo!\(|unimplemented!\(|not (?:ported|implemented)|coming soon|log_only_packet!", re.I)


def sha256(path):
    h = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def sources(root):
    """Keep production and test mentions separate, including inline test modules."""
    words = collections.defaultdict(set)
    queries = collections.defaultdict(set)
    tests = collections.defaultdict(set)
    gaps = []
    for path in sorted((root / "rust/crates").rglob("*.rs")):
        if "target" in path.parts:
            continue
        text = path.read_text(encoding="utf-8")
        relative = path.relative_to(root).as_posix()
        is_test = "tests" in path.parts or path.stem.endswith("tests") or path.stem == "test_support"
        # Conservatively classify the suffix after an inline test module as tests.
        # Runtime references still require review: macros can choose tables dynamically.
        split = re.search(r"#\[cfg\(test\)\]\s*(?:pub\([^)]*\)\s+)?mod\s+\w+\s*\{", text)
        runtime = "" if is_test else text[:split.start()] if split else text
        test_text = text if is_test else text[split.start():] if split else ""
        for word in set(WORD.findall(runtime)):
            words[word].add(relative)
        for word in set(WORD.findall(test_text)):
            tests[word].add(relative)
        for table in SQL_TABLE.findall(runtime):
            queries[table].add(relative)
        for line, value in enumerate(runtime.splitlines(), 1):
            if GAP.search(value):
                gaps.append({"file": relative, "line": line, "text": value.strip()})
    return words, queries, tests, gaps


def packets(opcodes, fixture, words, tests):
    registered = {}
    for line in fixture.splitlines():
        if line and not line.startswith("#"):
            level, opcode, name = line.split("\t")
            registered[name] = {"level": int(level), "opcode": int(opcode, 16)}
    inbound = opcodes.split("pub mod cs {", 1)[1].split("pub mod sc {", 1)[0]
    result = []
    for name, value in re.findall(r"pub const (\w+): u16 = (0x[0-9a-fA-F]+);", inbound):
        if not name.startswith(("CS", "X2")):
            continue
        opcode = int(value, 16)
        registration = registered.get(name)
        result.append({
            "name": name, "opcode": value, "supported_1_2": opcode != 0xFFF,
            "registration_fixture": registration,
            "fixture_opcode_matches": bool(registration and registration["opcode"] == opcode),
            "source_mentions": sorted(p for p in words[name] if not p.endswith("/opcodes.rs")),
            "test_mentions": sorted(tests[name]),
            "behavior_verification": "requires_review",
        })
    return result


def compact_tables(path, words, queries, tests):
    uri = path.resolve().as_uri() + "?mode=ro"
    with closing(sqlite3.connect(uri, uri=True)) as conn:
        conn.execute("PRAGMA query_only = ON")
        tables = []
        for name, schema in conn.execute("SELECT name,sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name"):
            quoted = '"' + name.replace('"', '""') + '"'
            count = conn.execute("SELECT COUNT(*) FROM " + quoted).fetchone()[0]
            tables.append(table_record(name, schema, words, queries, tests, row_count=count))
    return tables


def table_record(name, schema, words, queries, tests, **extra):
    return {
        "name": name, "schema": schema, **extra,
        "source_mentions": sorted(words[name]),
        "literal_sql_mentions": sorted(queries[name]),
        "test_mentions": sorted(tests[name]),
        "usage_verification": "requires_review",
    }


def mysql_tables(root, words, queries, tests):
    tables = []
    for database in ("login", "game"):
        path = root / f"SQL/aaemu_{database}.sql"
        text = path.read_text(encoding="utf-8")
        for match in re.finditer(r"CREATE TABLE(?: IF NOT EXISTS)?\s+`?(\w+)`?\s*\(.*?;", text, re.I | re.S):
            tables.append(table_record(match[1], match[0], words, queries, tests, database=database))
    return tables


def packet_locations(root, inbound):
    """Locate declarations and nearby unfinished markers; these are review candidates."""
    names = {p["name"] for p in inbound}
    locations = collections.defaultdict(list)
    candidates = collections.defaultdict(list)
    for path in sorted((root / "rust/crates/aaemu-game/src").rglob("*.rs")):
        if path.stem.endswith("tests") or path.stem == "test_support":
            continue
        lines = path.read_text(encoding="utf-8").splitlines()
        relative = path.relative_to(root).as_posix()
        for index, line in enumerate(lines):
            match = re.match(r"\s*(?:pub (?:struct|type)\s+|impl (?:ClientPacket for )?|log_only_packet!\(\s*)?(\w+)\b", line)
            if not match or match[1] not in names:
                continue
            name = match[1]
            locations[name].append({"file": relative, "line": index + 1})
            # Macro declarations and their docs usually contain their entire small
            # handler nearby. Wider bodies must still be inspected through source links.
            for offset in range(max(0, index - 4), min(len(lines), index + 35)):
                if GAP.search(lines[offset]) or "logged only" in lines[offset] or "only logged" in lines[offset]:
                    value = {"file": relative, "line": offset + 1, "text": lines[offset].strip()}
                    if value not in candidates[name]:
                        candidates[name].append(value)
    for packet in inbound:
        packet["declaration_locations"] = locations[packet["name"]]
        packet["nearby_incomplete_candidates"] = candidates[packet["name"]]


def reference_tables(path, tables):
    """Index exact table literals/SQL in one pass; addresses are research leads."""
    names = {t["name"] for t in tables}
    hits = collections.defaultdict(list)
    function = None
    with path.open(encoding="utf-8", errors="replace") as source:
        for line, text in enumerate(source, 1):
            function_match = re.match(r"^[^ /].*\b(sub_[0-9A-Fa-f]+)\s*\(", text)
            if function_match:
                function = function_match[1]
            candidates = set(SQL_TABLE.findall(text))
            candidates.update(re.findall(r'"([A-Za-z_]\w*)"', text))
            for name in candidates & names:
                if len(hits[name]) < 5:
                    hits[name].append({"line": line, "function": function})
    for table in tables:
        table["reference_locations"] = hits[table["name"]]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--compact-db", required=True, type=pathlib.Path)
    parser.add_argument("--output", required=True, type=pathlib.Path)
    parser.add_argument("--reference", type=pathlib.Path)
    parser.add_argument("--mysql-live-report", type=pathlib.Path,
                        help="Read-only exported JSON list: database/name/schema/row_count (no credentials).")
    parser.add_argument("--review", action="append", default=[], type=pathlib.Path,
                        help="Attach a reviewed evidence ledger (repeatable); does not imply passing tests.")
    args = parser.parse_args()
    words, queries, tests, gaps = sources(ROOT)
    opcode_path = GAME / "src/opcodes.rs"
    fixture_path = GAME / "tests/fixtures/csharp_game_network_registry.tsv"
    inbound = packets(opcode_path.read_text(), fixture_path.read_text(), words, tests)
    packet_locations(ROOT, inbound)
    tables = compact_tables(args.compact_db, words, queries, tests)
    if args.reference:
        reference_tables(args.reference, tables)
    report = {
        "generated_utc": datetime.now(timezone.utc).isoformat(),
        "provenance": {"compact_sha256": sha256(args.compact_db), "opcodes_sha256": sha256(opcode_path), "registry_fixture_sha256": sha256(fixture_path)},
        "packets": inbound, "compact_tables": tables,
        "mysql_base_schema_tables": mysql_tables(ROOT, words, queries, tests),
        "incomplete_code_candidates": gaps,
        "notes": [
            "Registration follows the fixture enforced by registry_matches_csharp_game_network; this is not behavior verification.",
            "Source/SQL/test mentions and missing mentions are audit leads, not proof of use, disuse or correctness.",
            "Dynamic table names, client-only data, diagnostics and features absent in 1.2 need manual classification.",
            "MySQL inventory uses base schema files; live migrations and row counts need a separate read-only review.",
            "Reference locations may describe modern-only behavior; verify 1.2 tables and wire format before implementing.",
        ],
    }
    if args.mysql_live_report:
        live = json.loads(args.mysql_live_report.read_text(encoding="utf-8"))
        report["mysql_live_tables"] = [
            table_record(t["name"], t["schema"], words, queries, tests,
                         database=t["database"], row_count=t["row_count"])
            for t in live
        ]
    if args.reference:
        report["provenance"]["reference_sha256"] = sha256(args.reference)
    report["review_ledgers"] = [json.loads(path.read_text(encoding="utf-8")) for path in args.review]
    for packet in inbound:
        packet["behavior_reviews"] = [
            review for ledger in report["review_ledgers"] for review in ledger.get("packets", [])
            if review["name"] == packet["name"]
        ]
    for records, review_key in [
        (tables, "compact_tables"),
        (report["mysql_base_schema_tables"], "mysql_tables"),
        (report.get("mysql_live_tables", []), "mysql_tables"),
    ]:
        for table in records:
            table["usage_reviews"] = [
                review for ledger in report["review_ledgers"]
                for review in ledger.get(review_key, [])
                if review["name"] == table["name"]
            ]
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    supported = [p for p in inbound if p["supported_1_2"]]
    missing = [p["name"] for p in supported if not p["fixture_opcode_matches"]]
    print(f"1.2 packets: {len(supported)}; registration gaps: {missing}")
    print(f"Compact tables: {len(tables)}; no production mentions: {sum(not t['source_mentions'] for t in tables)}")
    print(f"MySQL base tables: {len(report['mysql_base_schema_tables'])}; incomplete code candidates: {len(gaps)}")
    print(f"Report: {args.output}")
    return 1 if missing else 0


if __name__ == "__main__":
    sys.exit(main())
