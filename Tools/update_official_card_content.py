#!/usr/bin/env python3
"""Merge current official English card pages into the simulator card library.

The official site exposes one HTML page per product/catalog series.  This tool
parses the same fields consumed by GameManager and downloads printed card art
into StreamingAssets.  Existing records and print variants are preserved.

Examples:
    python Tools/update_official_card_content.py --starters
    python Tools/update_official_card_content.py --promos
    python Tools/update_official_card_content.py --starters --promos --download-art
"""

from __future__ import annotations

import argparse
import html
import io
import json
import re
import sys
from pathlib import Path
from urllib.parse import urljoin, urlsplit, urlunsplit

import requests
from bs4 import BeautifulSoup
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
CARD_ROOT = ROOT / "Assets" / "StreamingAssets" / "Cards"
LIBRARY = CARD_ROOT / "official-card-library.json"
OFFICIAL_BASE = "https://en.onepiece-cardgame.com/"
SERIES = {
    "ST31": "569031",
    "ST32": "569032",
    "ST33": "569033",
    "ST34": "569034",
    "ST35": "569035",
    "ST36": "569036",
    "PROMO": "569901",
}
KEYWORDS = ("Rush", "Blocker", "Double Attack", "Banish", "On K.O.")


def clean_text(node) -> str:
    if node is None:
        return ""
    # The official page writes printed attribute symbols as custom-looking HTML
    # tags (for example <Slash>). BeautifulSoup correctly treats those as tags,
    # so restore the visible rules token before extracting text.
    for attribute in ("slash", "strike", "ranged", "special", "wisdom"):
        for symbol in node.find_all(attribute):
            # Bandai closes this custom element after the remainder of the
            # sentence, so replacing it would delete valid rules text.
            symbol.insert_before(f"＜{attribute.title()}＞")
            symbol.unwrap()
    for br in node.find_all("br"):
        br.replace_with("\n")
    heading = node.find("h3")
    if heading:
        heading.decompose()
    value = html.unescape(node.get_text("", strip=False))
    value = value.replace("−", "-").replace("‐", "-")
    value = re.sub(r"[ \t\r\f\v]+", " ", value)
    value = re.sub(r" *\n *", "\n", value)
    return value.strip()


def integer(value: str) -> int:
    match = re.search(r"\d+", value or "")
    return int(match.group()) if match else 0


def without_query(url: str) -> str:
    parts = urlsplit(url)
    return urlunsplit((parts.scheme, parts.netloc, parts.path, "", ""))


def parse_series(code: str, series_id: str, session: requests.Session) -> list[dict]:
    source_url = f"{OFFICIAL_BASE}cardlist/?series={series_id}"
    response = session.get(source_url, timeout=45)
    response.raise_for_status()
    soup = BeautifulSoup(response.text, "html.parser")
    cards: list[dict] = []
    variant_counts: dict[str, int] = {}

    for modal in soup.select("dl.modalCol[id]"):
        info = modal.select_one(".infoCol")
        spans = [clean_text(x) for x in info.select("span")] if info else []
        if len(spans) < 3 or not re.fullmatch(r"(?:[A-Z]+\d+|P)-\d{3}", spans[0]):
            continue
        card_id, rarity, card_type = spans[:3]
        card_type = card_type.lower()
        variant_counts[card_id] = variant_counts.get(card_id, 0) + 1
        variant = variant_counts[card_id]
        modal_id = card_id if variant == 1 else f"{card_id}_p{variant - 1}"
        image = modal.select_one(".frontCol img[data-src]")
        image_url = urljoin(source_url, image.get("data-src", "")) if image else ""
        image_url = without_query(image_url)
        # Runtime masters use the same compact, universally-decodable JPEG format as
        # the rest of OfficialById. The source URL may be PNG; imageFile is local.
        image_file = f"{modal_id}.jpg"
        effect = clean_text(modal.select_one(".text"))
        trigger = clean_text(modal.select_one(".trigger"))
        keyword_source = effect + "\n" + trigger
        keywords = [name for name in KEYWORDS if f"[{name}]" in keyword_source]
        life_node = modal.select_one(".life")

        cards.append({
            "assetId": f"{card_id}__{series_id}__{modal_id}",
            "id": card_id,
            "modalId": modal_id,
            "variantIndexInSeries": variant,
            "seriesId": series_id,
            "seriesCode": code,
            "seriesLabel": clean_text(modal.select_one(".getInfo")),
            "sourceUrl": source_url,
            "imageUrl": image_url,
            "imageFile": image_file,
            "rarity": rarity,
            "type": card_type,
            "name": clean_text(modal.select_one(".cardName")),
            "color": clean_text(modal.select_one(".color")),
            "cost": integer(clean_text(modal.select_one(".cost"))),
            "life": integer(clean_text(life_node)) if life_node else None,
            "power": integer(clean_text(modal.select_one(".power"))),
            "counter": integer(clean_text(modal.select_one(".counter"))),
            "attribute": clean_text(modal.select_one(".attribute")),
            "block": clean_text(modal.select_one(".block")),
            "feature": clean_text(modal.select_one(".feature")),
            "effect": effect,
            "trigger": trigger,
            "sets": clean_text(modal.select_one(".getInfo")),
            "keywords": keywords,
        })

    if not cards:
        raise RuntimeError(f"No cards parsed from {source_url}")
    return cards


def download_art(cards: list[dict], session: requests.Session) -> tuple[int, int]:
    downloaded = skipped = 0
    seen_ids: set[str] = set()
    for card in cards:
        # The runtime resolves art strictly by card-ID prefix and base card ID;
        # it does not render separate print variants. Reprints already use their
        # original set's master, so fetch only one missing runtime image per ID.
        card_id = card["id"]
        if card_id in seen_ids:
            skipped += 1
            continue
        seen_ids.add(card_id)
        url = card.get("imageUrl", "")
        if not url:
            continue
        runtime_set = card_id.split("-", 1)[0]
        destination = CARD_ROOT / "OfficialById" / runtime_set / f"{card_id}.jpg"
        if destination.exists() and destination.stat().st_size > 1024:
            skipped += 1
            continue
        response = session.get(url, timeout=45)
        response.raise_for_status()
        if len(response.content) < 1024:
            raise RuntimeError(f"Art response was too small for {card['assetId']}: {len(response.content)}")
        destination.parent.mkdir(parents=True, exist_ok=True)
        with Image.open(io.BytesIO(response.content)) as source:
            source.convert("RGB").save(destination, "JPEG", quality=88, optimize=True)
        downloaded += 1
    return downloaded, skipped


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--starters", action="store_true", help="merge ST-31 through ST-36")
    parser.add_argument("--promos", action="store_true", help="refresh the official promotion catalog")
    parser.add_argument("--download-art", action="store_true", help="download missing card images")
    parser.add_argument("--dry-run", action="store_true", help="report without writing")
    args = parser.parse_args()
    if not args.starters and not args.promos:
        parser.error("choose --starters and/or --promos")

    targets = []
    if args.starters:
        targets.extend(f"ST{i}" for i in range(31, 37))
    if args.promos:
        targets.append("PROMO")

    session = requests.Session()
    session.headers["User-Agent"] = "Mozilla/5.0 (compatible; OPTCG-Simulator-Content-Importer/1.0)"
    fetched: list[dict] = []
    for code in targets:
        cards = parse_series(code, SERIES[code], session)
        fetched.extend(cards)
        print(f"{code}: {len({x['id'] for x in cards})} unique IDs, {len(cards)} print records")

    existing = json.loads(LIBRARY.read_text(encoding="utf-8"))
    target_series = set(targets)
    kept = [card for card in existing if card.get("seriesCode") not in target_series]
    merged = kept + fetched
    merged.sort(key=lambda x: (x.get("seriesCode", ""), x.get("id", ""), x.get("variantIndexInSeries", 0)))
    print(f"library: {len(existing)} -> {len(merged)} records ({len(fetched)} refreshed)")

    if args.download_art:
        downloaded, skipped = download_art(fetched, session)
        print(f"art: {downloaded} downloaded, {skipped} already present")
    if not args.dry_run:
        LIBRARY.write_text(json.dumps(merged, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(f"wrote {LIBRARY}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
