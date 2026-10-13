#!/usr/bin/env python3
"""S->C coverage: which server->client opcodes of the 1.2 protocol does the server implement?

Primary, exact finding: **an opcode of the 1.2 `SCOffsets` table with no Rust packet type
anywhere in the game crate**. A packet type is created by `packet_def!` / `raw_body_packet!`
or by one of the wrapper macros (`sc_packet!`, `simple_packet!`, `bc_packet!`,
`skill_level_packet!`), and by construction the type must exist before a packet can be
encoded. Absence is therefore a hard gap, independent of grep heuristics.

Secondary, approximate finding (the `never_sent` bucket): packets whose type exists but whose
name was never seen at a *structural* use site outside its declaring file. This is a grep
heuristic -- it produces false positives whenever a packet is built and sent through a local
helper in the same file -- so treat that bucket as a candidate list to check by hand, not as
proof.

Why this matters: the C#-vs-Rust audit in `rust/PORT-AUDIT.md` only proves coverage of the
packets the *former C# emulator* implemented. An opcode the 1.2 client supports but the C#
never wrote (e.g. `SCRankCharacterPacket`, `SCRacingPacket`, the `SCSecondPass*` family) falls
outside that audit, which is exactly what this script measures.

Usage: python rust/tools/opcode_coverage/sc_coverage.py [--json out.json]
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]  # <repo>/rust/tools/opcode_coverage -> <repo>
GAME_SRC = REPO / "rust" / "crates" / "aaemu-game" / "src"
OPCODES = GAME_SRC / "opcodes.rs"

MOD_RE = re.compile(r"^\s*pub mod\s+(\w+)")
CONST_RE = re.compile(r"^\s*pub const\s+(\w+)\s*:\s*u16\s*=\s*(0x[0-9a-fA-F]+)")
# S->C packets are declared through one of these wrappers. All but ``packet_def!`` /
# ``raw_body_packet!`` pass the packet name and derive the opcode as ``sc::<Name>``
# (``sc_packet!``, ``simple_packet!``, ``bc_packet!``, ``skill_level_packet!``), so the
# declared type name is enough to place them.
PACKET_DEF_RE = re.compile(
    r"packet_def!\s*\(\s*(\w+)\s*,[^;]*?opcode\s*=\s*(?:[\w:]+::)?(\w+)", re.S
)
RAW_BODY_RE = re.compile(r"raw_body_packet!\s*\(\s*(\w+)\s*,\s*([\w:]+)", re.S)
# ``sc_packet!(<doc comments> NAME { .. } |..| { .. })`` -- the type name follows the macro's
# doc comments, each of which is a complete ``#[..]`` attribute.
# ``sc_packet!`` / ``simple_packet!`` / ``bc_packet!`` / ``skill_level_packet!`` call sites
MACRO_CALL_RE = re.compile(r"\b(?:sc_packet|simple_packet|bc_packet|skill_level_packet)!\s*\(?")
MACRO_DEF_RE = re.compile(
    r"macro_rules!\s*(sc_packet|simple_packet|bc_packet|skill_level_packet)\b"
)
MARSHAL_RE = re.compile(r"impl\s+(?:[\w:]+::)?Marshal\s+for\s+(\w+)")
# a *structural* mention of an S->C packet type: constructed (`Foo {`), built by an
# associated fn (`Foo::new`), or referred to (`&Foo`)
PACKET_USE_RE = re.compile(r"\b([A-Z]\w*Packet)\s*(?:\{|\()")
OPCODES_REL = "rust/crates/aaemu-game/src/opcodes.rs"
TEST_MARKERS = ("#[cfg(test)]", "#[test]")


def rust_files() -> list[Path]:
    return [
        p
        for p in sorted(GAME_SRC.rglob("*.rs"))
        if not p.name.endswith("_tests.rs") and "tests" not in p.parts
    ]


def load_table(module: str) -> dict[str, int]:
    tables: dict[str, dict[str, int]] = {}
    current = None
    for line in OPCODES.read_text(encoding="utf-8").splitlines():
        m = MOD_RE.match(line)
        if m:
            current = m.group(1)
            tables.setdefault(current, {})
            continue
        m = CONST_RE.match(line)
        if m and current:
            tables[current][m.group(1)] = int(m.group(2), 16)
    return tables[module]


TEST_START_RE = re.compile(r"#\[cfg\(test\)\]")


def wrapper_types(text: str) -> list[str]:
    """Type names passed to the generator macros (``sc_packet!`` and friends).

    Scanned line by line rather than by regex: between the macro name and the type name sit doc
    comments that may contain arbitrary text (backticks, brackets, colons), so the type name is
    simply the first line of the call that looks like an identifier.
    """
    names: list[str] = []
    lines = text.split("\n")
    for i, line in enumerate(lines):
        if not MACRO_CALL_RE.search(line):
            continue
        for nxt in lines[i + 1 : i + 30]:
            stripped = nxt.strip()
            if not stripped or stripped.startswith("//") or stripped.startswith("#["):
                continue
            m = re.match(r"([A-Za-z_]\w*)", stripped)
            if m:
                names.append(m.group(1))
            break
    return names


def blank_item_bodies(text: str, starts: list[int]) -> str:
    """Blank the ``{ .. }`` block belonging to each item whose decoration starts at ``starts``.

    Works on an ``out`` list so it stays linear; newlines are preserved so line numbers match.
    """
    out = list(text)
    for start in starts:
        i = start
        while i < len(text) and text[i] != "{":
            i += 1
        if i >= len(text):
            for k in range(start, len(text)):
                if out[k] != "\n":
                    out[k] = " "
            continue
        depth, j = 0, i
        while j < len(text):
            ch = text[j]
            if ch == '"':
                j += 1
                while j < len(text) and text[j] != '"':
                    j += 2 if text[j] == "\\" else 1
            elif ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    break
            j += 1
        for k in range(i, min(j + 1, len(text))):
            if out[k] != "\n":
                out[k] = " "
    return "".join(out)


def strip_test_modules(text: str) -> str:
    """Blank out every ``#[cfg(test)]`` item, keeping line numbers valid.

    Brace-aware, so a test module *before* production code does not hide the rest of the file.
    """
    return blank_item_bodies(text, [m.end() for m in TEST_START_RE.finditer(text)])


def blank_macro_bodies(text: str) -> str:
    """Blank out every ``macro_rules!`` *definition* body.

    A generator macro's body mentions its ``$name`` parameter and the helper macro it expands
    to, so scanning it would invent packet types that do not exist. Only the *body* between the
    pattern's ``=> {`` and its closing brace is blanked -- the pattern itself contains braces
    (``{ ($(#[$m:meta])* $name:ident ...) => { ... } }``) that must not be mistaken for it.
    """
    out = list(text)
    for m in MACRO_DEF_RE.finditer(text):
        arrow = text.find("=>", m.end())
        if arrow < 0:
            continue
        i = text.find("{", arrow)
        if i < 0:
            continue
        depth, j = 0, i
        while j < len(text):
            ch = text[j]
            if ch == '"':
                j += 1
                while j < len(text) and text[j] != '"':
                    j += 2 if text[j] == "\\" else 1
            elif ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    break
            j += 1
        for k in range(i, min(j + 1, len(text))):
            if out[k] != "\n":
                out[k] = " "
    return "".join(out)
    return "".join(out)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--json")
    args = ap.parse_args()

    sc = load_table("sc")
    by_value: dict[int, list[str]] = {}
    for name, value in sc.items():
        by_value.setdefault(value, []).append(name)

    defined: dict[str, dict[str, str]] = {}  # opcode constant -> {type, file}
    marshal_types: set[str] = set()
    production_text: dict[str, str] = {}

    for path in rust_files():
        raw = path.read_text(encoding="utf-8", errors="replace")
        rel = str(path.relative_to(REPO)).replace("\\", "/")
        production_text[rel] = blank_macro_bodies(strip_test_modules(raw))
        for m in PACKET_DEF_RE.finditer(production_text[rel]):
            defined[m.group(2)] = {"type": m.group(1), "file": rel}
        for m in RAW_BODY_RE.finditer(production_text[rel]):
            op = m.group(2).split("::")[-1]
            defined.setdefault(op, {"type": m.group(1), "file": rel})
        for name in wrapper_types(production_text[rel]):
            defined.setdefault(name, {"type": name, "file": rel})
        for m in MARSHAL_RE.finditer(production_text[rel]):
            marshal_types.add(m.group(1))

    # inverted index of every structural mention of an S->C packet type
    index: dict[str, list[tuple[str, int]]] = {}
    for rel, text in production_text.items():
        lines = text.split("\n")
        for m in PACKET_USE_RE.finditer(text):
            line_no = text[: m.start()].count("\n") + 1
            line = lines[line_no - 1].strip()
            if line.startswith("//") or line.startswith("///") or "opcode" in line:
                continue  # doc comment or an opcode-table entry, not a construction site
            index.setdefault(m.group(1), []).append((rel, line_no))

    rows = []
    for name, value in sorted(sc.items(), key=lambda kv: kv[1]):
        if value == 0xFFF:
            continue
        info = None
        for candidate in [name, *by_value[value]]:
            if candidate in defined:
                info = defined[candidate]
                break
        sent_at: list[str] = []
        if info:
            own = info["file"]
            sent_at = [
                f"{rel}:{line}"
                for rel, line in index.get(info["type"], [])
                if rel != own and rel != OPCODES_REL
            ]
        rows.append(
            {
                "opcode": value,
                "name": name,
                "type": info["type"] if info else None,
                "file": info["file"] if info else None,
                "marshals": bool(info and info["type"] in marshal_types),
                "sent": bool(sent_at),
                "sent_at": sent_at[:5],
            }
        )

    no_type = [r for r in rows if not r["type"]]
    no_marshal = [r for r in rows if r["type"] and not r["marshals"]]
    never_sent = [r for r in rows if r["type"] and r["marshals"] and not r["sent"]]

    report = {
        "sc_wire_opcodes": len(rows),
        "has_packet_type": len(rows) - len(no_type),
        "no_packet_type": no_type,
        "no_marshal": no_marshal,
        "never_sent": never_sent,
        "rows": rows,
    }

    print(f"S->C distinct wire opcodes        : {report['sc_wire_opcodes']}")
    print(f"  with a Rust packet type         : {report['has_packet_type']}")
    print(f"  WITHOUT any Rust packet type    : {len(no_type)}")
    print(f"  typed but no Marshal impl       : {len(no_marshal)}")
    print(f"  typed + Marshal, never sent     : {len(never_sent)}")
    print()
    print("No Rust packet type at all (server cannot encode these):")
    for r in no_type:
        print(f"  0x{r['opcode']:03x}  {r['name']}")
    print()
    print("Typed + Marshal, but never constructed outside their own file:")
    for r in never_sent:
        print(f"  0x{r['opcode']:03x}  {r['name']:<40} {r['file']}")

    if args.json:
        Path(args.json).write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(f"\nwrote {args.json}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
