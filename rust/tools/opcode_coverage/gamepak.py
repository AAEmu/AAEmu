#!/usr/bin/env python3
"""Read the 1.2 client `game_pak` (AAPak TypeA) and extract files by regex.

The 1.2 pak uses the same format as the 10.x one documented in
`D:\\aa-re\\zone\\agent_geom\\tools\\pakx.py`: a 0x200 AES-CBC encrypted header at the end of
the file, a table of 0x150-byte encrypted entries before it, and the file data at the front.
Type A entry: name at +0 (0x108 bytes), offset/size as qwords at +0x108.

Usage:
  python gamepak.py list    <pak> <regex> [--out names.txt]
  python gamepak.py get     <pak> <outdir> <namesfile>
  python gamepak.py info    <pak>
"""

from __future__ import annotations

import argparse
import re
import struct
import sys
from pathlib import Path

from Crypto.Cipher import AES

KEY = bytes(
    [0x32, 0x1F, 0x2A, 0xEE, 0xAA, 0x58, 0x4A, 0xB4, 0x9A, 0x6C, 0x9E, 0x09, 0xD5, 0x9E, 0x9C, 0x6F]
)
HEADER = 0x200
ENTRY = 0x150
NAME_LEN = 0x108


def dec(b: bytes) -> bytes:
    return AES.new(KEY, AES.MODE_CBC, iv=bytes(16)).decrypt(b)


class Pak:
    def __init__(self, path: Path) -> None:
        self.path = path
        self.f = path.open("rb")
        self.f.seek(0, 2)
        self.length = self.f.tell()
        self.f.seek(self.length - HEADER)
        h = dec(self.f.read(HEADER))
        if h[0:4] == b"WIBO":
            self.kind, fc, xc = "A", *struct.unpack_from("<II", h, 8)
        elif h[8:12] == b"IDEJ":
            self.kind = "B"
            fc, xc = struct.unpack_from("<I", h, 12)[0], struct.unpack_from("<I", h, 0)[0]
        elif h[0:4] == b"ZERO":
            self.kind, fc, xc = "F", *struct.unpack_from("<II", h, 8)
        else:
            raise SystemExit(f"bad pak header: {h[:16]!r}")
        self.file_count, self.extra_count = fc, xc
        total = (fc + xc) * ENTRY
        off = self.length - HEADER - total
        off -= off % HEADER
        self.f.seek(off)
        fat = self.f.read(total)
        self.entries: list[tuple[str, int, int]] = []
        for i in range(fc + xc):
            d = dec(fat[i * ENTRY : (i + 1) * ENTRY])
            if self.kind == "A":
                name = d[0:NAME_LEN].split(b"\0")[0].decode("latin1")
                o, s = struct.unpack_from("<qq", d, NAME_LEN)
            elif self.kind == "B":
                name = d[0x20 : 0x20 + NAME_LEN].split(b"\0")[0].decode("latin1")
                s = struct.unpack_from("<q", d, 0x130)[0]
                o = struct.unpack_from("<q", d, 0x18)[0]
            else:
                name = d[8 : 8 + NAME_LEN].split(b"\0")[0].decode("latin1")
                o, s = struct.unpack_from("<qq", d, 0x110)
            is_file = (i < fc) if self.kind in "AF" else (i >= xc)
            if is_file:
                self.entries.append((name, o, s))

    def info(self) -> str:
        return (
            f"{self.path.name}: {self.length:,} bytes, type {self.kind}, "
            f"{self.file_count:,} files + {self.extra_count:,} extra"
        )

    def read(self, name: str) -> bytes:
        for n, o, s in self.entries:
            if n.lower() == name.lower():
                self.f.seek(o)
                return self.f.read(s)
        raise KeyError(name)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["list", "get", "info"])
    ap.add_argument("pak", type=Path)
    ap.add_argument("arg", nargs="?", default="")
    ap.add_argument("--out", type=Path)
    args = ap.parse_args()

    p = Pak(args.pak)
    print(p.info(), file=sys.stderr)
    if args.mode == "info":
        return 0

    if args.mode == "list":
        rx = re.compile(args.arg, re.I)
        hits = [e for e in p.entries if rx.search(e[0])]
        print(f"{len(hits)} match(es)", file=sys.stderr)
        lines = [f"{n}\t{o}\t{s}" for n, o, s in hits]
        if args.out:
            args.out.write_text("\n".join(lines), encoding="utf-8")
            print(f"wrote {args.out}", file=sys.stderr)
        else:
            print("\n".join(n for n, _, _ in hits))
        return 0

    # get
    outdir = Path(args.arg)
    names = [l.split("\t")[0] for l in (args.out.read_text(encoding="utf-8").splitlines() if args.out else [])]
    if not names:
        raise SystemExit("get needs --out <namesfile>")
    n_ok = 0
    for name in names:
        try:
            data = p.read(name)
        except KeyError:
            continue
        dest = outdir / name
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(data)
        n_ok += 1
    print(f"extracted {n_ok}/{len(names)}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
