#!/usr/bin/env python3
"""Decode onepiecetopdecks encoded decklist strings into .deck files for Decks/imported/.

Input lines: "Deck Name: <rawstring>" where rawstring is like
  1nOP16-079a4nOP16-091a4nOP16-092...
i.e. entries separated by 'a', each entry "<qty>n<cardID>". Reads from a file arg (or stdin).
"""
import sys, os, re

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Decks", "imported")

def slug(s): return re.sub(r'[^a-z0-9]+', '-', s.strip().lower()).strip('-')

def decode(raw):
    entries = []
    for tok in raw.strip().split('a'):
        tok = tok.strip()
        if not tok: continue
        m = re.match(r'^(\d+)n([A-Z]{1,5}\d{0,2}-\d{1,3})$', tok)
        if not m:  # tolerate stray fragments
            continue
        entries.append((int(m.group(1)), m.group(2)))
    return entries

def main():
    # usage: decode_topdecks.py <rawfile> [prefix]   (prefix defaults to op16)
    src = open(sys.argv[1], encoding='utf-8') if len(sys.argv) > 1 else sys.stdin
    prefix = sys.argv[2] if len(sys.argv) > 2 else "op16"
    os.makedirs(OUT, exist_ok=True)
    n = 0
    for line in src:
        if ':' not in line: continue
        name, raw = line.split(':', 1)
        entries = decode(raw)
        if not entries: continue
        total = sum(q for q, _ in entries)
        sl = slug(name)
        path = os.path.join(OUT, f"{prefix}-{sl}.deck")
        with open(path, 'w', encoding='utf-8') as fh:
            fh.write(f"# name: {prefix.upper()} {name.strip()}\n# id: {prefix}-{sl}\n")
            for q, cid in entries:
                fh.write(f"{q} {cid}\n")
        print(f"  op16-{sl}.deck  ({len(entries)} entries, {total} cards incl. leader)")
        n += 1
    print(f"wrote {n} deck files to {OUT}")

if __name__ == "__main__":
    main()
