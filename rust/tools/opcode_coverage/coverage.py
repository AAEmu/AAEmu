#!/usr/bin/env python3
"""Opcode coverage: which 1.2 opcodes does the game server register a handler for?

Two independent measurements:

1. ``rust/crates/aaemu-game/src/opcodes.rs`` -> every C->S opcode constant of the 1.2
   protocol (generated from the client's ``CSOffsets.cs``).
2. every ``register::<Struct>(LEVEL, <mod>::<Opcode>)`` call in the game crate, found by
   scanning for the ``register`` keyword and taking its balanced ``(`` .. ``)`` argument
   list. ``PacketRegistry::register`` is generic over the packet type, so *every* handled
   C->S opcode necessarily appears as a ``register`` argument -- this is the registry the
   server actually builds at startup (see ``src/net/registry.rs``).

Usage: python rust/tools/opcode_coverage/coverage.py [--json out.json]
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
# a real call: ``register(...)`` / ``register::<Foo>(...)`` / ``reg.register::<Foo>(...)``,
# but not ``pub fn register(...)`` (require a '.' or start of line before the keyword)
KEYWORD_RE = re.compile(r"(?:^|[.\s])register", re.M)


def load_opcodes() -> dict[str, dict[str, int]]:
    """Return {module: {name: opcode}} from opcodes.rs."""
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
    return tables


def balanced_args(text: str, open_paren: int) -> str | None:
    """Return the contents of the balanced ``( ... )`` starting at ``open_paren``.

    String literals are skipped so their parentheses do not confuse the depth counter.
    """
    depth = 0
    i = open_paren
    while i < len(text):
        ch = text[i]
        if ch == '"':
            i += 1
            while i < len(text) and text[i] != '"':
                i += 2 if text[i] == "\\" else 1
        elif ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
            if depth == 0:
                return text[open_paren + 1 : i]
        i += 1
    return None


def split_args(args: str) -> list[str]:
    """Split ``A, B`` at top-level commas (generics and closures keep their commas)."""
    out, depth, cur = [], 0, ""
    for ch in args:
        if ch in "(<{[":
            depth += 1
        elif ch in ")>}]":
            depth -= 1
        if ch == "," and depth == 0:
            out.append(cur)
            cur = ""
        else:
            cur += ch
    out.append(cur)
    return [p.strip() for p in out]


def skip_ws(text: str, i: int) -> int:
    while i < len(text) and text[i].isspace():
        i += 1
    return i


def parse_call(text: str, after_keyword: int) -> tuple[str, str] | None:
    """Parse the ``::<Struct>(args)`` that follows a ``register`` keyword.

    Returns ``(struct, args)``, or None when the keyword is not a generic call (for example
    a ``pub fn register(reg: &mut PacketRegistry)`` definition or a hand written
    ``self.register(name, aliases)``).
    """
    i = skip_ws(text, after_keyword)
    struct = ""
    if text.startswith("::", i):
        i = skip_ws(text, i + 2)
        if i >= len(text) or text[i] != "<":
            return None
        depth, start, i = 0, i + 1, i
        while i < len(text):
            if text[i] == "<":
                depth += 1
            elif text[i] == ">":
                depth -= 1
                if depth == 0:
                    break
            i += 1
        else:
            return None
        struct = text[start:i].strip()
        i = skip_ws(text, i + 1)
    if i >= len(text) or text[i] != "(":
        return None
    args = balanced_args(text, i)
    return None if args is None else (struct, args)


def load_registrations() -> dict[str, list[dict[str, str]]]:
    """Return {module: [{struct, opcode, file, line}]} for every ``register`` call."""
    regs: dict[str, list[dict[str, str]]] = {}
    for path in sorted(GAME_SRC.rglob("*.rs")):
        text = path.read_text(encoding="utf-8", errors="replace")
        for m in KEYWORD_RE.finditer(text):
            parsed = parse_call(text, m.end())
            if parsed is None:
                continue
            struct, got = parsed
            parts = [p for p in split_args(got) if p]
            if len(parts) != 2:
                continue  # ``register(&mut self, command, aliases)`` and friends
            arg = parts[1]
            if "::" not in arg:
                continue
            module, _, opcode = arg.rpartition("::")
            module, opcode = module.strip(), opcode.strip()
            if not module.isidentifier() or not opcode.isidentifier():
                continue
            regs.setdefault(module, []).append(
                {
                    "struct": struct.split("::")[-1],
                    "opcode": opcode,
                    "file": str(path.relative_to(REPO)).replace("\\", "/"),
                    "line": str(text[: m.start()].count("\n") + 1),
                }
            )
    return regs


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--json")
    args = ap.parse_args()

    tables = load_opcodes()
    regs = load_registrations()

    cs = tables["cs"]
    registered = regs.get("cs", [])
    handled = {r["opcode"] for r in registered}
    duplicates = sorted(
        op
        for op in handled
        if len([r for r in registered if r["opcode"] == op]) > 1
    )

    by_value: dict[int, list[str]] = {}
    for name, value in cs.items():
        by_value.setdefault(value, []).append(name)

    unhandled = [
        {"name": name, "opcode": value}
        for name, value in sorted(cs.items(), key=lambda kv: kv[1])
        if value != 0xFFF and name not in handled
    ]
    handled_values = {
        value for name, value in cs.items() if name in handled and value != 0xFFF
    }

    report = {
        "cs_symbols": len(cs),
        "cs_wire_values": len([v for v in by_value if v != 0xFFF]),
        "cs_no_opcode_0xfff": sorted(n for n, v in cs.items() if v == 0xFFF),
        "registered_cs_constants": len(handled & set(cs)),
        "registered_unknown_constants": sorted(handled - set(cs)),
        "handled_wire_values": len(handled_values),
        "duplicate_registrations": duplicates,
        "unhandled": unhandled,
        "unhandled_count": len(unhandled),
        "registrations": registered,
        "registrations_by_module": {k: len(v) for k, v in sorted(regs.items())},
    }

    print(f"C->S constants in opcodes.rs         : {report['cs_symbols']}")
    print(f"C->S constants without a 1.2 opcode  : {len(report['cs_no_opcode_0xfff'])}")
    print(f"C->S distinct wire opcodes           : {report['cs_wire_values']}")
    print(f"cs::register() calls in the crate    : {len(registered)}")
    print(f"C->S constants registered in handlers: {report['registered_cs_constants']}")
    print(f"C->S wire opcodes with a handler     : {report['handled_wire_values']}")
    print(f"C->S wire opcodes WITHOUT a handler  : {report['unhandled_count']}")
    if duplicates:
        print(f"duplicate opcode registrations       : {duplicates}")
    if report["registered_unknown_constants"]:
        print(f"  names not in opcodes.rs            : {report['registered_unknown_constants']}")
    print()
    print("Unhandled C->S opcodes (client can send these; server has no handler):")
    for u in unhandled:
        names = "/".join(by_value[u["opcode"]])
        print(f"  0x{u['opcode']:03x}  {names}")

    if args.json:
        Path(args.json).write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(f"\nwrote {args.json}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
