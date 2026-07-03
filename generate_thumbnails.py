#!/usr/bin/env python3
"""
One Piece TCG Deck Builder — card art thumbnail generator (run once, then again
whenever a new set is added).

Writes downscaled copies of every card image under StreamingAssets/Cards/
OfficialById/{set}/{id}.png to a mirrored StreamingAssets/Cards/Thumbs/{set}/
{id}.jpg tree, proportionally resized (no padding/letterboxing) to fit inside
a 448x626 box. Hex cells, decklist rows, and library thumbnail cards decode
these instead of the ~716x1000 masters — same on-screen quality at the small
sizes those contexts actually display at, a fraction of the decode cost.

JPEG, not PNG: card art has no transparency (painted illustrations, fully
opaque), and PNG barely compresses it (~450KB per 448x626 thumbnail vs a
~950KB master - only ~2x smaller despite ~2.5x fewer pixels). JPEG at quality
88 lands around ~95KB with no visible artifacts at these display sizes, and
Texture2D.LoadImage on the Unity side decodes JPEG just as readily as PNG.

USAGE:
    python generate_thumbnails.py
    python generate_thumbnails.py "path/to/StreamingAssets/Cards"

    # Regenerate every thumbnail even if one already exists and looks current:
    python generate_thumbnails.py --force

Incremental by default: an id is skipped if its thumbnail already exists and
is newer than its source master, so re-running after adding a new set only
does the new work, but re-running after replacing a master's art (same id,
newer file) picks up the change instead of leaving a stale thumbnail.

Requires Python 3. Auto-installs Pillow if missing.
"""

import os, sys, argparse


def ensure_deps():
    try:
        from PIL import Image  # noqa
    except ImportError:
        import subprocess
        print("Installing Pillow (one-time)…")
        subprocess.check_call([sys.executable, "-m", "pip", "install", "Pillow"])


def find_cards_dir(arg):
    if arg and os.path.isdir(arg):
        return arg
    start = os.path.dirname(os.path.abspath(__file__))
    roots = [start, os.path.abspath(os.path.join(start, "..")), os.getcwd()]
    for base in roots:
        for root, dirs, _ in os.walk(base):
            norm = root.replace("\\", "/")
            if norm.endswith("StreamingAssets/Cards") and os.path.isdir(os.path.join(root, "OfficialById")):
                return root
            if root.count(os.sep) - base.count(os.sep) > 7:
                dirs[:] = []
    return None


# Fit-within box, not fill/crop - every source card shares roughly the same
# ~0.716 aspect (both the 716x1000 and 480x671 masters already in this library
# match it), so in practice this always produces the same output aspect, but
# deriving it from each source image's own size (rather than hardcoding a
# target and letterboxing) means a mismatched source can never produce a
# padded thumbnail that would corrupt the hex-crop math on the Unity side,
# which reads its aspect ratio directly off the loaded sprite.
THUMB_MAX_W, THUMB_MAX_H = 448, 626


JPEG_QUALITY = 88


def make_thumbnail(src_path, dst_path):
    from PIL import Image
    with Image.open(src_path) as im:
        im = im.convert("RGB")   # JPEG has no alpha channel; card art has no real transparency anyway
        w, h = im.size
        scale = min(THUMB_MAX_W / w, THUMB_MAX_H / h)
        if scale >= 1.0:
            # Source is already smaller than the target box - don't upscale,
            # just copy through (keeps behaviour sane for any small promo art).
            out = im.copy()
        else:
            out = im.resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.LANCZOS)
        os.makedirs(os.path.dirname(dst_path), exist_ok=True)
        out.save(dst_path, "JPEG", quality=JPEG_QUALITY, optimize=True)


def main():
    ap = argparse.ArgumentParser(description="One Piece TCG card art thumbnail generator")
    ap.add_argument("cards_dir", nargs="?", default=None,
                     help="path to StreamingAssets/Cards (auto-detected if omitted)")
    ap.add_argument("--force", action="store_true",
                     help="regenerate every thumbnail, even ones that already look current")
    args = ap.parse_args()

    ensure_deps()

    cards = find_cards_dir(args.cards_dir)
    if not cards:
        print("ERROR: could not locate StreamingAssets/Cards. Pass the path as an argument.")
        return
    print("Cards folder:", cards)

    byid = os.path.join(cards, "OfficialById")
    thumbs = os.path.join(cards, "Thumbs")

    pngs = []
    for root, _, files in os.walk(byid):
        for f in files:
            if f.lower().endswith(".png"):
                pngs.append(os.path.join(root, f))
    pngs.sort()
    print(f"Found {len(pngs)} card images…")

    written = skipped = failed = 0
    for i, src in enumerate(pngs):
        rel = os.path.relpath(src, byid)                        # "{set}/{id}.png"
        dst = os.path.join(thumbs, os.path.splitext(rel)[0] + ".jpg")   # "{set}/{id}.jpg"

        if not args.force and os.path.isfile(dst) and os.path.getmtime(dst) >= os.path.getmtime(src):
            skipped += 1
            continue

        try:
            make_thumbnail(src, dst)
            written += 1
        except Exception as e:
            print(f"  FAILED {rel}: {e}")
            failed += 1

        if (i + 1) % 250 == 0:
            print(f"  {i+1}/{len(pngs)}  written={written}  skipped={skipped}  failed={failed}")

    print(f"\nDone. written={written}  skipped={skipped}  failed={failed}")
    print("Wrote thumbnails to:", thumbs)


if __name__ == "__main__":
    main()
