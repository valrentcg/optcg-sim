#!/usr/bin/env python3
"""Fetch booster-pack wrapper art for Sealed / Pre-Release mode.

Sealed mode renders a procedural foil pack for any set with no wrapper image, so this is a
drop-in UPGRADE, never a prerequisite — the mode looks right without ever running it.

Saves to:  Assets/StreamingAssets/Cards/Packs/<SET>.png
which is exactly where SealedPackArt.LocalPath() looks. Nothing is fetched at runtime.

Usage:
    python Tools/fetch_pack_art.py                # every booster set it can find
    python Tools/fetch_pack_art.py OP16 OP15      # just these
    python Tools/fetch_pack_art.py --list         # show what is already present

Run this yourself against sources you're entitled to use — the same way the card art already in
StreamingAssets/Cards got there. It deliberately does not ship with a hardcoded scrape target.
"""

import argparse
import os
import re
import sys
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACK_DIR = os.path.join(ROOT, "Assets", "StreamingAssets", "Cards", "Packs")
CARD_DIR = os.path.join(ROOT, "Assets", "StreamingAssets", "Cards", "OfficialById")

# Point this at wherever you source product images. {set} is substituted (e.g. "OP16").
# Left empty on purpose: fill it in for your source before running a fetch.
SOURCE_TEMPLATE = os.environ.get("OPTCG_PACK_ART_URL", "")

UA = "Mozilla/5.0 (compatible; optcg-sim-asset-fetch/1.0)"


def known_sets():
    """Booster sets the local card library actually covers, newest first."""
    if not os.path.isdir(CARD_DIR):
        return []
    sets = [d for d in os.listdir(CARD_DIR)
            if os.path.isdir(os.path.join(CARD_DIR, d))
            and re.fullmatch(r"(OP|EB)\d{2}", d)]
    return sorted(sets, reverse=True)


def existing():
    if not os.path.isdir(PACK_DIR):
        return set()
    return {os.path.splitext(f)[0] for f in os.listdir(PACK_DIR) if f.lower().endswith(".png")}


def fetch(set_code):
    if not SOURCE_TEMPLATE:
        print(f"  {set_code}: no source configured — set OPTCG_PACK_ART_URL "
              f"(a URL template containing {{set}}) and re-run.")
        return False
    url = SOURCE_TEMPLATE.replace("{set}", set_code)
    dest = os.path.join(PACK_DIR, set_code + ".png")
    try:
        req = urllib.request.Request(url, headers={"User-Agent": UA})
        with urllib.request.urlopen(req, timeout=30) as r:
            data = r.read()
        if len(data) < 1024:
            print(f"  {set_code}: response too small ({len(data)} bytes) — skipped")
            return False
        os.makedirs(PACK_DIR, exist_ok=True)
        with open(dest, "wb") as f:
            f.write(data)
        print(f"  {set_code}: saved {len(data):,} bytes -> {os.path.relpath(dest, ROOT)}")
        return True
    except Exception as e:
        print(f"  {set_code}: {e}")
        return False


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("sets", nargs="*", help="set codes (default: all found in the card library)")
    ap.add_argument("--list", action="store_true", help="show which sets already have wrapper art")
    ap.add_argument("--force", action="store_true", help="re-download sets that already have art")
    args = ap.parse_args()

    have = existing()
    targets = [s.upper() for s in args.sets] or known_sets()

    if args.list:
        print(f"pack art directory: {PACK_DIR}")
        for s in known_sets():
            print(f"  {s}: {'present' if s in have else 'procedural fallback'}")
        return 0

    if not targets:
        print("No booster sets found under", CARD_DIR)
        return 1

    print(f"{len(targets)} set(s) targeted; {len(have)} already present.")
    ok = 0
    for s in targets:
        if s in have and not args.force:
            print(f"  {s}: already present (use --force to replace)")
            continue
        if fetch(s):
            ok += 1
    print(f"done — {ok} fetched. Sets without art render the procedural foil pack.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
