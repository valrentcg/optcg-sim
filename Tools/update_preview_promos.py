#!/usr/bin/env python3
"""Import currently revealed promo cards absent from Bandai's English catalog.

OPlayTCG provides structured English records and print images for announced
cards which have not reached Bandai's normal card-list page yet.  This importer
is deliberately ID-bounded so a future unrelated reveal cannot silently change
the simulator's library.
"""

from __future__ import annotations

import argparse
import io
import json
import sys
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

import requests
from PIL import Image

from update_op17_spoilers import CARD_ROOT, LIBRARY, fetch, printing_image, to_library


# Known/revealed promos after the last complete Bandai English promo index.
# P-156, P-160 and P-162 onward were not published at time of this importer.
PROMO_NUMBERS = tuple(range(120, 155)) + (157, 158, 159, 161)

# Source transcription defects confirmed against the printed card images.
EFFECT_OVERRIDES = {
    "P-129": "[On Play] Give up to one of your Characters with a base power of 6000 +2000 power during this turn.",
    "P-142": "If your {Straw Hat Crew} type Character with 8000 base power or less would be K.O.'d, you may trash this Stage instead.",
    "P-147": "If there is a Character with a cost of 0 or with a cost of 8 or more, this Character gains +2000 power.\n[On K.O.] Add up to 1 Character card with a type including \"Baroque Works\" from your trash to your hand.",
}


def promo_record(card: dict) -> dict:
    record = to_library(card)
    card_id = card["code"]
    if card_id in EFFECT_OVERRIDES:
        record["effect"] = EFFECT_OVERRIDES[card_id]
    record.update({
        "assetId": f"{card_id}__PREVIEW-PROMO__{card_id}",
        "seriesId": "PREVIEW-PROMO",
        "seriesCode": "PROMO-PREVIEW",
        "seriesLabel": "Announced promotional card (pre-release English translation)",
        "sets": "Promotional card",
        "imageFile": f"{card_id}.jpg",
    })
    return record


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--download-art", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    ids = [f"P-{number:03d}" for number in PROMO_NUMBERS]

    found: dict[str, dict] = {}
    with ThreadPoolExecutor(max_workers=10) as pool:
        futures = [pool.submit(fetch, card_id) for card_id in ids]
        for future in as_completed(futures):
            card_id, card = future.result()
            if card:
                found[card_id] = card
    missing = [card_id for card_id in ids if card_id not in found]
    if missing:
        raise RuntimeError(f"Promo source did not return expected IDs: {', '.join(missing)}")
    cards = [promo_record(found[card_id]) for card_id in ids]
    print(f"preview promos: {len(cards)} cards parsed")

    if args.download_art:
        destination_dir = CARD_ROOT / "OfficialById" / "P"
        destination_dir.mkdir(parents=True, exist_ok=True)
        downloaded = skipped = 0
        for card in cards:
            destination = destination_dir / card["imageFile"]
            if destination.exists() and destination.stat().st_size > 1024:
                skipped += 1
                continue
            response = requests.get(printing_image(found[card["id"]]), timeout=45)
            response.raise_for_status()
            if len(response.content) < 1024:
                raise RuntimeError(f"Art response too small for {card['id']}")
            with Image.open(io.BytesIO(response.content)) as source:
                source.convert("RGB").save(destination, "JPEG", quality=88, optimize=True)
            downloaded += 1
        print(f"art: {downloaded} downloaded, {skipped} already present")

    existing = json.loads(LIBRARY.read_text(encoding="utf-8"))
    imported_ids = {card["id"] for card in cards}
    # Prefer Bandai's official English record wherever it already exists. The
    # preview record only fills an absent ID and can be safely rerun later.
    official_ids = {
        card.get("id") for card in existing
        if card.get("id") in imported_ids and card.get("seriesCode") != "PROMO-PREVIEW"
    }
    additions = [card for card in cards if card["id"] not in official_ids]
    merged = [card for card in existing if card.get("seriesCode") != "PROMO-PREVIEW"] + additions
    merged.sort(key=lambda x: (x.get("seriesCode", ""), x.get("id", ""), x.get("variantIndexInSeries", 0)))
    print(f"library: {len(existing)} -> {len(merged)} records ({len(additions)} preview IDs added)")
    if not args.dry_run:
        LIBRARY.write_text(json.dumps(merged, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(f"wrote {LIBRARY}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
