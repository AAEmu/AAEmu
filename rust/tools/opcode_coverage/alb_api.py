#!/usr/bin/env python3
"""Extract the X2* API surface a compiled-Lua client uses.

The 1.2 client's UI is compiled Lua bytecode in `game/scriptsbin/x2ui/**/*.alb`. Its string
constants keep the engine API symbol names, so scanning the files yields the exact set of
`X2Foo.Bar` calls the client can make.

Usage: python alb_api.py <uibindir> [--out api.tsv]
Writes `symbol<TAB>file[,file...]` per line.
"""

from __future__ import annotations

import argparse
import re
import sys
from collections import defaultdict
from pathlib import Path

RUN = re.compile(rb"[\x20-\x7e]{4,}")
# X2Foo.Bar / X2Foo:Bar -- the game's Lua bindings for the client host API
X2 = re.compile(r"^X2[A-Za-z0-9_]*[.:][A-Za-z_][A-Za-z0-9_]*$")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("dir", type=Path)
    ap.add_argument("--out", type=Path)
    args = ap.parse_args()

    hits: dict[str, set[str]] = defaultdict(set)
    files = 0
    for p in sorted(args.dir.rglob("*.alb")):
        files += 1
        rel = str(p.relative_to(args.dir))
        for m in RUN.finditer(p.read_bytes()):
            for tok in re.split(r"[\s\"'(),\[\]{}]+", m.group().decode("latin1")):
                if X2.match(tok):
                    hits[tok].add(rel)

    print(f"{files} .alb file(s), {len(hits)} X2 API symbol(s)")
    if args.out:
        lines = [f"{s}\t{','.join(sorted(fs)[:3])}" for s, fs in sorted(hits.items())]
        args.out.write_text("\n".join(lines), encoding="utf-8")
        print(f"wrote {args.out}")
    else:
        for s in sorted(hits):
            print(s)
    return 0


if __name__ == "__main__":
    sys.exit(main())
