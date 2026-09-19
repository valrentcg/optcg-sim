#!/usr/bin/env python3
"""Import the complete OP-17 spoiler into the simulator.

OP-17 is fully revealed but is not yet present in Bandai's official English card
database. OPlayTCG exposes structured English translations plus Japanese official
print provenance. Records produced here therefore retain the community source URL
and use ``seriesLabel`` to make their pre-release translation status explicit.
"""

from __future__ import annotations

import argparse
import io
import json
import re
import sys
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from urllib.parse import urlsplit, urlunsplit

import requests
from bs4 import BeautifulSoup
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
CARD_ROOT = ROOT / "Assets" / "StreamingAssets" / "Cards"
LIBRARY = CARD_ROOT / "official-card-library.json"
SOURCE = "https://oplaytcg.com/en/cards/{card_id}"
KEYWORDS = ("Rush", "Rush: Character", "Blocker", "Double Attack", "Banish", "On K.O.")
RARITY_CODES = {
    "Common": "C",
    "Uncommon": "UC",
    "Rare": "R",
    "Super Rare": "SR",
    "Secret Rare": "SEC",
    "Leader": "L",
    "Special": "SP CARD",
    "Treasure Rare": "TR",
}

# OPlay's stable base URLs were later overwritten with parallel artwork while
# still reporting source_variant=base. These masters are curated local base-art
# reconstructions and must never be silently replaced from that endpoint.
LOCAL_BASE_ART_OVERRIDES = {"OP17-020", "OP17-061"}
EFFECT_OVERRIDES = {
    "OP17-037": "[Main] Look at 5 cards from the top of your deck; reveal up to 1 card with a type including \"Red-Haired Pirates\" and add it to your hand. Then, place the rest at the bottom of your deck in any order.\n[Counter] You may rest 1 of your cards: Up to 1 of your Leader or Characters gains +3000 power during this battle.",
    "OP17-058": "[When Attacking]/[On Your Opponent's Attack] [Once Per Turn] DON!! −1: Up to 1 of your opponent's Characters gains −2000 power during this turn.",
    "OP17-110": "[Your Turn] [On Play] Play up to 1 {Big Mom Pirates} type Character card with a cost of 6 or less from your hand. Then, this Character gains [Rush].",
}


def without_query(url: str) -> str:
    p = urlsplit(url)
    return urlunsplit((p.scheme, p.netloc, p.path, "", ""))


def extract_card(page: str, card_id: str) -> dict | None:
    for script in BeautifulSoup(page, "html.parser").find_all("script"):
        text = script.string or script.get_text()
        if "self.__next_f.push" not in text or card_id not in text:
            continue
        match = re.match(r"self\.__next_f\.push\((.*)\)\s*$", text, re.S)
        if not match:
            continue
        try:
            payload = json.loads(match.group(1))[1]
        except (json.JSONDecodeError, IndexError, TypeError):
            continue
        marker = '"card":'
        position = payload.find(marker)
        if position < 0:
            continue
        try:
            card, _ = json.JSONDecoder().raw_decode(payload, position + len(marker))
        except json.JSONDecodeError:
            continue
        if card.get("code") == card_id:
            return card
    return None


def fetch(card_id: str) -> tuple[str, dict | None]:
    response = requests.get(SOURCE.format(card_id=card_id), timeout=45,
                            headers={"User-Agent": "Mozilla/5.0 (compatible; OPTCG-Simulator/1.0)"})
    if response.status_code == 404:
        return card_id, None
    response.raise_for_status()
    return card_id, extract_card(response.text, card_id)


def printing_image(card: dict) -> str:
    printings = card.get("printings") or []
    english = next((x for x in printings if x.get("language_code") == "en"), None)
    japanese = next((x for x in printings if x.get("language_code") == "jp"), None)
    selected = english or japanese or (printings[0] if printings else {})
    return without_query(selected.get("imageFull") or selected.get("image") or "")


def to_library(card: dict) -> dict:
    card_id = card["code"]
    effect = card.get("effect") or ""
    effect = EFFECT_OVERRIDES.get(card_id, effect)
    trigger = card.get("trigger") or ""
    image_url = printing_image(card)
    rarity = ((card.get("printings") or [{}])[0].get("rarity_code")
              or card.get("rarity") or "")
    rarity = RARITY_CODES.get(rarity, rarity)
    keywords = [x for x in KEYWORDS if f"[{x}]" in effect or f"[{x}]" in trigger]
    return {
        "assetId": f"{card_id}__OP17-SPOILER__{card_id}",
        "id": card_id,
        "modalId": card_id,
        "variantIndexInSeries": 1,
        "seriesId": "OP17-SPOILER",
        "seriesCode": "OP17",
        "seriesLabel": "THE WORLD'S STRONGEST WARRIORS [OP-17] (pre-release English translation)",
        "sourceUrl": SOURCE.format(card_id=card_id),
        "imageUrl": image_url,
        "imageFile": f"{card_id}.jpg",
        "rarity": rarity,
        "type": (card.get("type") or "").lower(),
        "name": card.get("name") or "",
        "color": "/".join(card.get("colorNames") or card.get("colors") or []),
        "cost": card.get("cost") or 0,
        "life": card.get("life"),
        "power": card.get("power") or 0,
        "counter": card.get("counter") or 0,
        "attribute": card.get("attribute") or "",
        "block": "/".join(card.get("blocks") or []),
        "feature": "/".join(x.get("name", "") for x in (card.get("traits") or [])),
        "effect": effect,
        "trigger": trigger,
        "sets": "THE WORLD'S STRONGEST WARRIORS [OP-17]",
        "keywords": keywords,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--download-art", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    ids = [f"OP17-{number:03d}" for number in range(1, 121)]
    found: dict[str, dict] = {}
    with ThreadPoolExecutor(max_workers=10) as pool:
        futures = {pool.submit(fetch, card_id): card_id for card_id in ids}
        for future in as_completed(futures):
            card_id, card = future.result()
            if card:
                found[card_id] = card
    cards = [to_library(found[x]) for x in sorted(found)]
    missing = [x for x in ids if x not in found]
    print(f"OP17: {len(cards)} cards parsed; missing IDs: {', '.join(missing) if missing else 'none'}")
    if len(cards) < 110:
        raise RuntimeError("OP-17 source returned fewer than 110 cards; refusing partial library write")

    if args.download_art:
        destination_dir = CARD_ROOT / "OfficialById" / "OP17"
        destination_dir.mkdir(parents=True, exist_ok=True)
        downloaded = skipped = 0
        for card in cards:
            destination = destination_dir / card["imageFile"]
            if destination.exists() and destination.stat().st_size > 1024:
                skipped += 1
                continue
            if card["id"] in LOCAL_BASE_ART_OVERRIDES:
                raise RuntimeError(
                    f"{card['id']} uses a curated local base-art master; restore it from source control "
                    "instead of downloading OPlay's mislabeled parallel image"
                )
            response = requests.get(card["imageUrl"], timeout=45)
            response.raise_for_status()
            if len(response.content) < 1024:
                raise RuntimeError(f"Art response too small for {card['id']}")
            with Image.open(io.BytesIO(response.content)) as source:
                source.convert("RGB").save(destination, "JPEG", quality=88, optimize=True)
            downloaded += 1
        print(f"art: {downloaded} downloaded, {skipped} already present")

    existing = json.loads(LIBRARY.read_text(encoding="utf-8"))
    merged = [x for x in existing if x.get("seriesCode") != "OP17"] + cards
    merged.sort(key=lambda x: (x.get("seriesCode", ""), x.get("id", ""), x.get("variantIndexInSeries", 0)))
    print(f"library: {len(existing)} -> {len(merged)} records")
    if not args.dry_run:
        LIBRARY.write_text(json.dumps(merged, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(f"wrote {LIBRARY}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
