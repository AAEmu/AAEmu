"""Builds rust/PORT-AUDIT.md from a fresh scan of the C# and Rust trees plus the verdict files.

Usage: python rust/tools/port_audit/gen.py rust/PORT-AUDIT.md (without an argument: print the
counts and the files with a MISSING type). verdicts_*.txt lines: `Type[,Type...]|STATUS|note`.
"""
import os, re, json, collections, sys
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
RUST = os.path.join(ROOT, "rust", "crates")

def snake(n):
    s = re.sub(r'(?<=[a-z0-9])([A-Z])', r'_\1', n)
    s = re.sub(r'(?<=[A-Z])([A-Z][a-z])', r'_\1', s)
    return s.lower()

rfiles = {}
for dp, dn, fn in os.walk(RUST):
    if 'target' in dp:
        continue
    for f in fn:
        if f.endswith('.rs'):
            p = os.path.join(dp, f)
            rfiles[os.path.relpath(p, RUST).replace(chr(92), '/')] = open(p, encoding='utf-8', errors='replace').read()

defs = collections.defaultdict(set)
words = collections.defaultdict(collections.Counter)
negs = collections.defaultdict(set)
defre = re.compile(r'\b(?:struct|enum|trait|type|fn|const|static|mod|union)\s+(?:r#)?(\w+)')
macdef = re.compile(r'^\s*(?:raw_body_packet!|packet_def!|simple_packet!|log_only_packet!|cs_packet!|sc_packet!|int_enum!)?\s*\(?\s*(?:///[^\n]*\n\s*)*([A-Z]\w+)\s*[,{:]', re.M)
wre = re.compile(r'[A-Za-z_][A-Za-z0-9_]*')
for p, t in rfiles.items():
    for m in defre.finditer(t):
        defs[m.group(1)].add(p)
    for line in t.splitlines():
        s = line.strip()
        m = re.match(r'(?:raw_body_packet!|packet_def!)\(\s*(\w+)', s) or re.match(r'([A-Z]\w+Packet)\s*\{', s)
        if m:
            defs[m.group(1)].add(p)
    for m in wre.finditer(t):
        words[m.group(0)][p] += 1
    for line in t.splitlines():
        if re.search(r'not ported|TODO|todo!\(|unported|not modelled', line):
            for m in wre.finditer(line):
                negs[m.group(0)].add(p)

# verdict overrides: name|STATUS|note
over = {}
for f in sorted(os.listdir(HERE)):
    if f.startswith('verdicts_') and f.endswith('.txt'):
        for line in open(os.path.join(HERE, f), encoding='utf-8'):
            line = line.strip()
            if not line or line.startswith('#'):
                continue
            names, st, note = line.split('|', 2)
            for n in names.split(','):
                over[n.strip()] = (st.strip().lower(), note.strip())

typere = re.compile(r'^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|unsafe|new|ref|file)\s+)*(class|struct|enum|interface|record(?:\s+(?:class|struct))?)\s+(\w+)', re.M)
EXCL = ['AAEmu.Game/Scripts/Commands', 'AAEmu.Game/Scripts/SubCommands']

def classify(name, kind):
    if name in over:
        return over[name]
    cand = [name, snake(name)]
    if kind == 'interface' and re.match(r'I[A-Z]', name):
        cand.append(name[1:])
    for c in cand:
        if c in defs:
            files = sorted(defs[c], key=lambda f: -words[c][f])
            return ('ported', files[0])
    for c in cand:
        if c in words:
            top = words[c].most_common(1)[0][0]
            if name in negs and all(f in negs[name] for f in words[c]):
                return ('MISSING', 'only mentioned as not ported in ' + top)
            return ('merged', top)
    if kind == 'interface':
        return ('dropped', 'interface only (DI / abstraction); implementations audited separately')
    return ('MISSING', '')

rows = collections.defaultdict(list)
counts = collections.Counter()
missing = []
for proj in ['AAEmu.Commons', 'AAEmu.Login', 'AAEmu.Game']:
    for dp, dn, fn in os.walk(os.path.join(ROOT, proj)):
        rp = os.path.relpath(dp, ROOT).replace(chr(92), '/')
        parts = rp.split('/')
        if 'obj' in parts or 'bin' in parts:
            continue
        if any(rp == e or rp.startswith(e + '/') for e in EXCL):
            continue
        for f in sorted(fn):
            if not f.endswith('.cs'):
                continue
            src = open(os.path.join(dp, f), encoding='utf-8-sig', errors='replace').read()
            nc = re.sub(r'/\*.*?\*/', '', src, flags=re.S)
            nc = re.sub(r'//[^\n]*', '', nc)
            types = [(m.group(1).split()[0], m.group(2)) for m in typere.finditer(nc)]
            if not types:
                st, note = over.get(f[:-3], ('dropped', 'no type declarations (comments / global usings only)'))
                rows[rp].append((f, '-', st, note))
                counts[st] += 1
                continue
            res = [(n, kind) + classify(n, kind) for kind, n in types]
            sts = [r[2] for r in res]
            if 'MISSING' in sts:
                st = 'MISSING'
            elif all(s == 'ported' for s in sts):
                st = 'ported'
            elif all(s in ('dropped',) for s in sts):
                st = 'dropped'
            elif any(s in ('ported',) for s in sts) and all(s in ('ported', 'merged', 'dropped') for s in sts):
                st = 'ported' if sts[0] == 'ported' else 'merged'
            else:
                st = 'merged'
            counts[st] += 1
            def fmt(n, s, note):
                if s == 'ported' and ' ' not in note:
                    return f"`{n}` → `{note}`"
                if s == 'merged' and ' ' not in note:
                    return f"`{n}` merged into `{note}`"
                return f"`{n}` {s}: {note}"
            detail = '; '.join(fmt(n, s, note) for n, kind, s, note in res)
            rows[rp].append((f, ', '.join(n for _, n in types[:4]) + (' …' if len(types) > 4 else ''), st, detail))
            if st == 'MISSING':
                missing.append(rp + '/' + f + ': ' + detail)

print(counts)
for m in missing:
    print('MISSING', m)

if len(sys.argv) > 1:
    out = [open(os.path.join(HERE, 'header.md'), encoding='utf-8').read().replace('{COUNTS}', ', '.join(f"{k}: {v}" for k, v in sorted(counts.items())))]
    for d in sorted(rows):
        out.append(f"\n### `{d}`\n\n| File | Types | Status | Rust |\n|---|---|---|---|")
        for f, ty, st, det in rows[d]:
            det = det.replace('|', '\\|')
            out.append(f"| `{f}` | {ty} | {st} | {det} |")
    out.append(open(os.path.join(HERE, 'footer.md'), encoding='utf-8').read())
    open(sys.argv[1], 'w', encoding='utf-8').write('\n'.join(out) + '\n')
