#!/usr/bin/env python3
"""Inventory a client feature area from its compiled Lua (`.alb`) UI scripts.

The 1.2 client ships its UI as compiled Lua bytecode. String constants (widget names,
engine / `X2*` API symbols, locale keys, packet field names) survive compilation, so the
scripts are a reliable list of what the *client* knows how to do — independent of what the
server implements.

Usage:
  python alb_symbols.py <dir> [--out out.txt] [--min 4]

Prints every distinct printable symbol/identifier string found in the `.alb` files of
``dir`` (recursively), sorted, together with the file it came from.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

# printable ASCII run inside the bytecode
RUN = re.compile(rb"[\x20-\x7e]{4,}")


def symbols(path: Path) -> set[str]:
    data = path.read_bytes()
    out: set[str] = set()
    for m in RUN.finditer(data):
        s = m.group().decode("latin1").strip()
        if len(s) < 4:
            continue
        # keep it only if it looks like an identifier / dotted path / locale key
        if re.fullmatch(r"[A-Za-z_][A-Za-z0-9_.:]{3,}", s):
            out.add(s)
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("dir", type=Path)
    ap.add_argument("--out", type=Path)
    ap.add_argument("--min", type=int, default=4)
    args = ap.parse_args()

    per_file: dict[str, set[str]] = {}
    all_syms: dict[str, set[str]] = {}
    for p in sorted(args.dir.rglob("*.alb")):
        syms = symbols(p)
        per_file[str(p.relative_to(args.dir))] = syms
        for s in syms:
            all_syms.setdefault(s, set()).add(str(p.relative_to(args.dir)))

    print(f"{len(per_file)} .alb file(s), {len(all_syms)} distinct symbol(s)")
    interesting = sorted(
        s
        for s in all_syms
        if not s.startswith(("locale", "Locale"))
        and re.search(r"[A-Z]", s)
        and len(s) >= args.min
    )
    if args.out:
        lines = [f"{s}\t{','.join(sorted(all_syms[s])[:3])}" for s in interesting]
        args.out.write_text("\n".join(lines), encoding="utf-8")
        print(f"wrote {args.out} ({len(interesting)} symbols)")
    else:
        for s in interesting:
            print(s)
    return 0


if __name__ == "__main__":
    sys.exit(main())
