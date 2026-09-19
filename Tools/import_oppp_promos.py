#!/usr/bin/env python3
"""Import the explicitly approved promo cards and their clean base artwork.

OPPP supplies the exact printable base-print identity. OPlay supplies structured
card metadata and, where it is larger, the art master for IDs that Bandai's
current English promo catalog omits. The importer is intentionally ID-bounded so
a future source update cannot silently add cards.
"""

from __future__ import annotations

import hashlib
import io
import json
import sys
import time
from pathlib import Path
from urllib.parse import quote

import requests
from PIL import Image, ImageFilter

sys.path.insert(0, str(Path(__file__).resolve().parent))
from update_op17_spoilers import fetch, to_library  # noqa: E402


ROOT = Path(__file__).resolve().parents[1]
CARD_ROOT = ROOT / "Assets" / "StreamingAssets" / "Cards"
LIBRARY = CARD_ROOT / "official-card-library.json"
REPORT = ROOT / "Tools" / "Content" / "oppp-requested-promos-import.json"
OPPP = "https://oppp.online"

PROMO_IDS = (
    "P-000", "P-038", "P-040", "P-064", "P-066", "P-067", "P-080", "P-086",
    "P-087", "P-094", "P-095", "P-108", "P-109", "P-114", "P-116", "P-118",
)

# OPlay currently exposes the translated P-087 record with a Chinese display
# name even though its English rules data is present. The printed identity is
# Nico Robin; keep the runtime search/display name in English.
NAME_OVERRIDES = {"P-087": "Nico Robin"}

# These product photos were visually checked against the requested base art and
# contain no SAMPLE overlay. Entries omitted here have a watermark or no product.
TCGPLAYER_CLEAN_PRODUCTS = {
    "P-040": 709539,
    "P-080": 580056,
    "P-108": 709561,
    "P-116": 709566,
    "P-118": 709632,
}

# OPlay is the best clean source for these prints. Other OPlay promo masters in
# the requested list visibly contain Bandai's SAMPLE overlay and are excluded.
OPLAY_CLEAN_IDS = {"P-000", "P-064", "P-066", "P-080", "P-094", "P-095",
                   "P-108", "P-109", "P-118"}

# These two masters were obtained from OPPP before its daily full-size allowance
# was exhausted. Keeping them is preferable to replacing them with a small thumb.
KNOWN_CLEAN_LOCAL_IDS = {"P-064", "P-066"}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def metadata(card_id: str) -> dict:
    _, source = fetch(card_id)
    if not source:
        raise RuntimeError(f"OPlay returned no structured metadata for {card_id}")
    record = to_library(source)
    record.update({
        "assetId": f"{card_id}__PROMO-OPPP__{card_id}",
        "seriesId": "PROMO-OPPP",
        "seriesCode": "PROMO",
        "seriesLabel": "PROMOTIONAL CARD",
        "sets": "Promotional card",
        "imageFile": f"{card_id}.jpg",
    })
    if card_id in NAME_OVERRIDES:
        record["name"] = NAME_OVERRIDES[card_id]
    return record


def oppp_base_variant(session: requests.Session, card_id: str) -> dict:
    response = session.get(f"{OPPP}/get-thumbnails", params={"query": card_id}, timeout=45)
    response.raise_for_status()
    cards = response.json().get("thumbnails") or []
    exact = next((card for card in cards if card.get("label") == card_id), None)
    if not exact or not exact.get("source_path"):
        raise RuntimeError(f"OPPP returned no exact base printing for {card_id}")
    return exact


def get_with_retries(session: requests.Session, url: str, card_id: str,
                     attempts: int = 4) -> requests.Response | None:
    for attempt in range(attempts):
        response = session.get(url, timeout=60)
        if response.status_code != 429:
            response.raise_for_status()
            return response
        if attempt + 1 >= attempts:
            return None
        delay = min(20, int(response.headers.get("Retry-After", "0") or 0) or 3 * (attempt + 1))
        print(f"{card_id}: source throttled; retrying in {delay}s")
        time.sleep(delay)
    return None


def decoded_image(content: bytes, card_id: str, source: str) -> Image.Image:
    if len(content) < 20_000:
        raise RuntimeError(f"{source} art response too small for {card_id}: {len(content)} bytes")
    opened = Image.open(io.BytesIO(content))
    opened.load()
    return opened.convert("RGB")


def download_art(session: requests.Session, card_id: str, variant: dict, record: dict) -> dict:
    source_path = variant["source_path"]
    oppp_url = f"{OPPP}/api/download-fullscale/{quote(source_path, safe='/')}"
    oplay_url = record.get("imageUrl") or ""
    full_path = CARD_ROOT / "OfficialById" / "P" / f"{card_id}.jpg"
    thumb_path = CARD_ROOT / "Thumbs" / "P" / f"{card_id}.jpg"
    full_path.parent.mkdir(parents=True, exist_ok=True)
    thumb_path.parent.mkdir(parents=True, exist_ok=True)

    candidates: list[tuple[str, str, Image.Image]] = []
    if (card_id in KNOWN_CLEAN_LOCAL_IDS and full_path.exists()
            and full_path.stat().st_size > 20_000):
        with Image.open(full_path) as existing:
            existing.load()
            candidates.append(("verified clean local OPPP master", str(full_path), existing.convert("RGB")))

    # OPlay's uncropped card master is generally larger than OPPP's print-tool
    # raster. Download it first, while retaining the OPPP base-print match in the
    # report. This also leaves the import usable when OPPP's daily limit is hit.
    if oplay_url and card_id in OPLAY_CLEAN_IDS:
        response = get_with_retries(session, oplay_url, card_id)
        if response is not None:
            candidates.append(("visually verified clean OPlay master", oplay_url,
                               decoded_image(response.content, card_id, "OPlay")))

    product_id = TCGPLAYER_CLEAN_PRODUCTS.get(card_id)
    if product_id:
        tcg_url = f"https://tcgplayer-cdn.tcgplayer.com/product/{product_id}_in_1000x1000.jpg"
        response = get_with_retries(session, tcg_url, card_id)
        if response is not None:
            candidates.append(("visually verified clean TCGplayer scan", tcg_url,
                               decoded_image(response.content, card_id, "TCGplayer")))

    # Try OPPP as a second candidate. A rate limit is a documented fallback,
    # not a partial-import failure, because its exact variant was already found.
    response = get_with_retries(session, oppp_url, card_id, attempts=1)
    if response is not None:
        candidates.append(("OPPP exact base print", oppp_url,
                           decoded_image(response.content, card_id, "OPPP")))

    # The public thumbnail remains clean even when OPPP's full-size daily quota
    # is exhausted. It is the final no-watermark fallback for this bounded set.
    oppp_thumb_url = f"{OPPP}{variant['url']}"
    response = get_with_retries(session, oppp_thumb_url, card_id)
    if response is not None:
        candidates.append(("OPPP clean thumbnail fallback", oppp_thumb_url,
                           decoded_image(response.content, card_id, "OPPP thumbnail")))

    if not candidates:
        raise RuntimeError(f"No usable art source for {card_id}")
    selected_name, selected_url, source = max(
        candidates, key=lambda item: item[2].width * item[2].height)
    source_width, source_height = source.size

    # Do not invent card content with generative fill. A deterministic Lanczos
    # resample keeps the exact clean print when only OPPP's 143x200 image remains.
    resampled = source_width < 350 or source_height < 480
    if resampled:
        source = source.resize((600, 839), Image.Resampling.LANCZOS)
        source = source.filter(ImageFilter.UnsharpMask(radius=1.0, percent=75, threshold=3))
    width, height = source.size

    source.save(full_path, "JPEG", quality=94, subsampling=0, optimize=True)
    thumb = source.copy()
    thumb.thumbnail((600, 838), Image.Resampling.LANCZOS)
    thumb.save(thumb_path, "JPEG", quality=90, optimize=True)
    for _, _, image in candidates:
        image.close()

    return {
        "cardId": card_id,
        "opppSourcePath": source_path,
        "opppSourceUrl": oppp_url,
        "selectedArtSource": selected_name,
        "selectedArtUrl": selected_url,
        "sourceDimensions": [source_width, source_height],
        "dimensions": [width, height],
        "resampled": resampled,
        "sampleWatermark": False,
        "fullArt": str(full_path.relative_to(ROOT)).replace("\\", "/"),
        "fullArtSha256": sha256(full_path),
        "thumbnail": str(thumb_path.relative_to(ROOT)).replace("\\", "/"),
        "thumbnailSha256": sha256(thumb_path),
    }


def main() -> int:
    session = requests.Session()
    session.headers["User-Agent"] = "Mozilla/5.0 (compatible; OPTCG-Simulator-Content-Importer/1.0)"

    records = []
    artwork = []
    for card_id in PROMO_IDS:
        record = metadata(card_id)
        variant = oppp_base_variant(session, card_id)
        art = download_art(session, card_id, variant, record)
        # Keep runtime repair/downloads on the same clean source selected here;
        # otherwise a missing local file could silently restore a SAMPLE master.
        selected_url = art["selectedArtUrl"]
        record["imageUrl"] = selected_url if selected_url.startswith("http") else art["opppSourceUrl"]
        records.append(record)
        artwork.append(art)
        print(f"{card_id}: metadata + OPPP identity + best available clean art")
        time.sleep(0.35)

    current = json.loads(LIBRARY.read_text(encoding="utf-8-sig"))
    wanted = set(PROMO_IDS)
    merged = [record for record in current if record.get("id") not in wanted]
    merged.extend(records)
    merged.sort(key=lambda x: (x.get("seriesCode", ""), x.get("id", ""),
                               x.get("variantIndexInSeries", 0), x.get("assetId", "")))
    LIBRARY.write_text(json.dumps(merged, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    report = {
        "identitySource": OPPP + "/print",
        "metadataSource": "https://oplaytcg.com/en/cards/{cardId}",
        "requestedIds": list(PROMO_IDS),
        "importedCount": len(records),
        "libraryRecordsBefore": len(current),
        "libraryRecordsAfter": len(merged),
        "artwork": artwork,
    }
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"library: {len(current)} -> {len(merged)} records")
    print(f"wrote {REPORT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
