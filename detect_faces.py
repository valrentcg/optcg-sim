#!/usr/bin/env python3
"""
One Piece TCG Deck Builder — anime face detector (run once).

Writes  StreamingAssets/Cards/face-data.json  mapping each card id to the
eye-line fraction y (0 = top of card, 1 = bottom) and the face centre x
(0 = left). The deck builder reads this at startup to frame character art in
deck rows and to centre the hex-roster crops on the character's face.

Detection pipeline (first hit wins):
  1. Cascade (strict)   — minNeighbors 3-5, run at 1.0x / 1.6x / 2.2x upscale
  2. Cascade (loose)    — minNeighbors 2, native scale only
  3. Skin-blob fallback — connected skin components scored by size, height,
                          centrality and face-like shape; head = top slice

USAGE:
    python detect_faces.py
    python detect_faces.py "path/to/StreamingAssets/Cards"

    # Inspect specific cards (e.g. a leader whose hex crop looks wrong):
    #   prints which tier detected the face + the eye-line fraction, and writes
    #   an annotated PNG (face box green, face crosshair red, hex crop window
    #   yellow) to face-debug/<ID>.png inside the Cards folder. Also refreshes
    #   those ids inside the existing face-data.json so a single bad card can
    #   be fixed without a full re-run.
    python detect_faces.py --card OP07-038 --card OP01-001

    # Audit the whole deck-select roster: annotate EVERY leader card and
    # refresh their face-data entries. Flip through face-debug/ afterwards —
    # the yellow rectangle is exactly what each hex will show.
    python detect_faces.py --leaders

Requires Python 3.  Auto-installs opencv-python if missing.
"""

import os, sys, json, ssl, urllib.request, subprocess, argparse


def ensure_deps():
    try:
        import cv2, numpy  # noqa
    except ImportError:
        print("Installing opencv-python + numpy (one-time)…")
        subprocess.check_call([sys.executable, "-m", "pip", "install", "opencv-python", "numpy"])


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


CASCADE_URLS = [
    "https://raw.githubusercontent.com/nagadomi/lbpcascade_animeface/master/lbpcascade_animeface.xml",
    "https://cdn.jsdelivr.net/gh/nagadomi/lbpcascade_animeface@master/lbpcascade_animeface.xml",
]


def get_cascade(cards_dir):
    path = os.path.join(cards_dir, "lbpcascade_animeface.xml")
    if os.path.isfile(path) and os.path.getsize(path) > 100000:
        return path
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    for u in CASCADE_URLS:
        try:
            print("Downloading anime-face model:", u)
            req = urllib.request.Request(u, headers={"User-Agent": "Mozilla/5.0"})
            data = urllib.request.urlopen(req, timeout=60, context=ctx).read()
            if len(data) > 100000:
                with open(path, "wb") as f:
                    f.write(data)
                print(f"  saved ({len(data)} bytes)")
                return path
        except Exception as e:
            print("  failed:", e)
    return None


# OPTCG illustration occupies the top 62% of the card; text box is below that.
ART_FRACTION = 0.62


def preprocess(gray_art):
    import cv2
    clahe = cv2.createCLAHE(clipLimit=2.5, tileGridSize=(8, 8))
    return clahe.apply(gray_art)


def cascade_detect(cascade, gray_art, W, art_h, min_neighbors, min_size_frac, full_h):
    """Run the cascade and return the largest usable detection in the upper art region."""
    import numpy as np
    ms = (int(W * min_size_frac), int(art_h * min_size_frac))
    faces = cascade.detectMultiScale(gray_art, scaleFactor=1.03,
                                     minNeighbors=min_neighbors, minSize=ms)
    best = None
    for (x, y, w, h) in (faces if len(faces) else []):
        face_centre_y = y + h * 0.5
        # Reject detections whose centre is in the lower 38% of the art region
        # (empirically, real face boxes centre above 62% of art height; the false
        # positives we see on action cards land at 63-70%).
        if face_centre_y > art_h * 0.62:
            continue
        # Reject large boxes that start very near the top of the art: the
        # 33% eye-offset rule breaks when lbpcascade spans from the hair
        # crown (at y≈0) all the way to the chin.  For those cards the skin
        # fallback gives a far more accurate result.
        if y < art_h * 0.12 and h > full_h * 0.15:
            continue
        area = w * h
        if best is None or area > best[0]:
            best = (area, x, y, w, h)
    return best


def eye_fraction_from_box(y_art, h_face, full_h):
    """
    Eye line as a fraction of full card height.
    lbpcascade bounding boxes include the hair crown, so eyes sit roughly
    33% down from the top of the box (not 40% as you might guess).
    """
    return (y_art + 0.33 * h_face) / full_h


def skin_fallback(img_bgr_art, art_h, full_h):
    """
    Estimate the face position from skin-coloured regions when the cascade
    fails. v2: instead of the centre-of-mass of ALL skin (which characters
    showing a lot of skin — Boa! — drag down to the chest), find connected
    skin BLOBS and pick the best "head" candidate: a reasonably large blob
    that starts high in the art. The face estimate is the centroid of that
    blob's TOP SLICE (the head part), not the whole blob, so face+neck+chest
    merging into one component no longer pulls the estimate down.

    Returns (y_fraction, x_fraction) — both relative to the FULL card,
    0 = top/left — or None if no skin found (non-human characters like
    Brook, Laboon, etc.).
    """
    import cv2, numpy as np

    H, W = img_bgr_art.shape[:2]

    # Wide strip — faces are often off-centre; only skip the outer border art.
    cx0, cx1 = int(W * 0.10), int(W * 0.90)
    strip = img_bgr_art[:, cx0:cx1]

    # HSV gives clean hue-based skin detection regardless of brightness.
    # Split the warm range by hue: near-red (H<8) is only skin at MODERATE
    # saturation — vivid red at high saturation is clothing/capes (Kyros,
    # Shanks), and including it merges the whole art into one giant blob.
    hsv = cv2.cvtColor(strip, cv2.COLOR_BGR2HSV)
    m_red  = cv2.inRange(hsv,
        np.array([0,  12, 90], np.uint8),
        np.array([8, 150, 255], np.uint8))
    m_tan  = cv2.inRange(hsv,
        np.array([8,  12, 90], np.uint8),
        np.array([22, 210, 255], np.uint8))
    m_pale = cv2.inRange(hsv,
        np.array([0,   3, 175], np.uint8),
        np.array([22,  55, 255], np.uint8))
    mask = cv2.bitwise_or(cv2.bitwise_or(m_red, m_tan), m_pale)

    # Opening removes speckle; closing bridges small gaps (eyes/mouth/blush
    # lines) so a face reads as one blob.
    k = np.ones((3, 3), np.uint8)
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, k)
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, np.ones((7, 7), np.uint8))

    # Heads live in the upper art; the scoring below prefers high, central,
    # face-SHAPED blobs, so we can search fairly deep without a hard cutoff.
    search_h = int(H * 0.78)
    m = mask[:search_h]
    if cv2.countNonZero(m) < m.size * 0.004:
        return None   # too little skin → non-human or no character

    num, labels, stats, _cent = cv2.connectedComponentsWithStats(m, 8)
    best = None
    for i in range(1, num):
        area = stats[i, cv2.CC_STAT_AREA]
        if area < m.size * 0.004:
            continue                               # stray pixels / tiny hands
        top = stats[i, cv2.CC_STAT_TOP]
        w_i = stats[i, cv2.CC_STAT_WIDTH]
        h_i = max(stats[i, cv2.CC_STAT_HEIGHT], 1)
        # CENTRALITY — the named character is drawn at the focus of the card,
        # so an edge blob (background figure) must be much bigger to win.
        blob_cx = (cx0 + stats[i, cv2.CC_STAT_LEFT] + w_i * 0.5) / W
        central = 1.0 - 0.55 * min(1.0, abs(blob_cx - 0.5) * 2.4)
        # HEIGHT — gentle preference for blobs starting higher (heads over
        # torsos), but not so brutal that a low central face loses to an arm.
        height = 1.0 - 0.5 * (top / float(search_h))
        # SHAPE — faces are compact and roughly round; swords/arms/legs are
        # elongated and sparse within their bounding box.
        aspect = w_i / float(h_i)
        aspect_ok = 1.0 if 0.45 <= aspect <= 2.4 else 0.45
        fill = area / float(w_i * h_i)
        fill_ok = min(1.0, max(0.30, fill / 0.45))
        score = float(area) * height * central * aspect_ok * fill_ok
        if best is None or score > best[0]:
            best = (score, i)
    if best is None:
        return None

    i = best[1]
    top = stats[i, cv2.CC_STAT_TOP]
    h_i = stats[i, cv2.CC_STAT_HEIGHT]

    # Head slice: the top of the blob, at most face-sized. If the blob is just
    # a face it IS the slice; if it's face+neck+chest, the slice is the head.
    face_px = max(int(full_h * 0.055), 8)          # ≈ typical face height on card
    band_h = int(min(max(h_i * 0.40, face_px), face_px * 2.2))
    sub = (labels[top:top + band_h] == i)
    import numpy as _np
    ys, xs = _np.nonzero(sub)
    if len(ys) == 0:
        return None

    head_y = top + float(ys.mean())
    head_x = cx0 + float(xs.mean())

    # The slice centroid sits around forehead/eye level already; nudge down a
    # touch so wild hair above the face doesn't push the estimate too high.
    eye_px_y = head_y + full_h * 0.015
    frac_y = float(min(max(eye_px_y / full_h, 0.06), 0.52))
    frac_x = float(min(max(head_x / W, 0.15), 0.85))
    return frac_y, frac_x


def detect_one(cascade, img):
    """
    Run the full pipeline on one card image (BGR).
    Returns (eye_fraction or None, x_fraction or None, tier_name, face_box or
    None) where face_box is (x, y, w, h) in full-card pixel coordinates when a
    cascade tier hit. x_fraction is the face centre, 0 = left edge of the card.
    """
    import cv2

    full_h, W = img.shape[:2]
    art_h = int(full_h * ART_FRACTION)
    art_bgr = img[:art_h]
    gray_art = preprocess(cv2.cvtColor(art_bgr, cv2.COLOR_BGR2GRAY))

    # Run the cascade at several UPSCALES too: the LBP model has an absolute
    # minimum detectable size, so faces that are small relative to the art
    # (full-body poses, chibi, distant characters) only get caught after the
    # art is blown up. A strict pass at any scale beats any loose pass.
    def run(nn, msf, scale):
        if scale == 1.0:
            g, sw, sa, sf = gray_art, W, art_h, full_h
        else:
            g = cv2.resize(gray_art, None, fx=scale, fy=scale,
                           interpolation=cv2.INTER_CUBIC)
            sw, sa, sf = int(W * scale), int(art_h * scale), int(full_h * scale)
        best = cascade_detect(cascade, g, sw, sa, nn, msf, sf)
        if best is None:
            return None
        _, x, y, w, h = best
        inv = 1.0 / scale
        return int(x * inv), int(y * inv), int(w * inv), int(h * inv)

    SCALES = (1.0, 1.6, 2.2)

    # ── Tier 1: strict cascade, all scales ───────────────────────────────────
    for (nn, msf) in [(4, 0.07), (3, 0.055), (5, 0.09)]:
        for scale in SCALES:
            # Keep the RELATIVE min size at every scale: upscaling only lifts
            # faces past the cascade's absolute ~24px floor. Letting relatively
            # smaller boxes through produced tiny false positives (hair, ribbons).
            box = run(nn, msf, scale)
            if box is not None:
                x, y, w, h = box
                return (eye_fraction_from_box(y, h, full_h), (x + w * 0.5) / W,
                        "cascade-strict", (x, y, w, h))

    # ── Tier 2: loose cascade (minNeighbors=2), native scale only ────────────
    box = run(2, 0.05, 1.0)
    if box is not None:
        x, y, w, h = box
        return (eye_fraction_from_box(y, h, full_h), (x + w * 0.5) / W,
                "cascade-loose", (x, y, w, h))

    # ── Tier 3: skin-blob head finder ────────────────────────────────────────
    res = skin_fallback(art_bgr, art_h, full_h)
    if res is not None:
        ey, ex = res
        return ey, ex, "skin-fallback", None
    return None, None, "miss", None


def clamp_frac(ey):
    return round(float(min(max(ey, 0.06), 0.52)), 3)


def clamp_x(ex):
    return round(float(min(max(ex if ex is not None else 0.5, 0.10), 0.90)), 3)


def find_card_png(cards, card_id):
    byid = os.path.join(cards, "OfficialById")
    want = card_id.lower()
    for root, _, files in os.walk(byid):
        for f in files:
            if f.lower().endswith(".png") and os.path.splitext(f)[0].lower() == want:
                return os.path.join(root, f)
    return None


# Must match DeckBuilderManager.BuildDeckHexCell: a window CENTRED on the face
# in both axes, clamped inside the card's illustration region so the hex only
# ever contains art.
HEX_ASPECT = 2.0 / (3.0 ** 0.5)          # flat-top hex: width / height ≈ 1.1547
SAFE_L, SAFE_R = 0.06, 0.94
SAFE_T, SAFE_B = 0.05, 0.62
HEX_VIS_H = 0.44                          # zoom: fraction of card height shown
def hex_crop_window(eye_frac, x_frac, card_aspect):
    vis_h = HEX_VIS_H
    vis_w = vis_h * HEX_ASPECT / max(card_aspect, 0.01)
    if vis_w > SAFE_R - SAFE_L:
        vis_w = SAFE_R - SAFE_L
        vis_h = vis_w * card_aspect / HEX_ASPECT
    # Face sits 45% down the window (a touch of headroom for hair/hats).
    cx = min(max(x_frac, SAFE_L + vis_w / 2), SAFE_R - vis_w / 2)
    cy = min(max(eye_frac + vis_h * 0.05, SAFE_T + vis_h / 2), SAFE_B - vis_h / 2)
    return cx - vis_w / 2, cy - vis_h / 2, cx + vis_w / 2, cy + vis_h / 2


def debug_cards(cascade, cards, card_ids):
    """Annotate + report specific cards, and patch their entries in face-data.json."""
    import cv2

    dbg_dir = os.path.join(cards, "face-debug")
    os.makedirs(dbg_dir, exist_ok=True)
    fixed = {}

    for cid in card_ids:
        p = find_card_png(cards, cid)
        if p is None:
            print(f"{cid}: PNG not found under OfficialById")
            continue
        img = cv2.imread(p)
        if img is None:
            print(f"{cid}: could not read {p}")
            continue

        ey, ex, tier, box = detect_one(cascade, img)
        full_h, W = img.shape[:2]
        vis = img.copy()

        if box is not None:                                   # face box (green)
            x, y, w, h = box
            cv2.rectangle(vis, (x, y), (x + w, y + h), (0, 255, 0), 3)
        if ey is not None:
            ey_c, ex_c = clamp_frac(ey), clamp_x(ex)
            fixed[os.path.splitext(os.path.basename(p))[0]] = (ey_c, ex_c)
            ey_px, ex_px = int(ey_c * full_h), int(ex_c * W)  # face crosshair (red)
            cv2.line(vis, (0, ey_px), (W, ey_px), (0, 0, 255), 3)
            cv2.line(vis, (ex_px, 0), (ex_px, full_h), (0, 0, 255), 2)
            aspect = W / full_h
            wl, wt, wr, wb = hex_crop_window(ey_c, ex_c, aspect)   # hex window (yellow)
            cv2.rectangle(vis, (int(wl * W), int(wt * full_h)),
                          (int(wr * W), int(wb * full_h)), (0, 220, 255), 3)
            print(f"{cid}: tier={tier}  face=({ex_c:.3f}, {ey_c:.3f})"
                  f"  hex-crop=x {wl:.3f}..{wr:.3f}, y {wt:.3f}..{wb:.3f}")
        else:
            print(f"{cid}: tier={tier}  NO DETECTION — C# skin heuristic will apply")

        out_png = os.path.join(dbg_dir, os.path.basename(p))
        cv2.imwrite(out_png, vis)

    # Patch just these ids into the existing face-data.json (no full re-run).
    if fixed:
        fpath = os.path.join(cards, "face-data.json")
        data = {"ids": [], "y": [], "x": []}
        if os.path.isfile(fpath):
            try:
                with open(fpath) as f:
                    data = json.load(f)
            except Exception as e:
                print("face-data.json unreadable, rebuilding entries:", e)
        old_ids = data.get("ids", [])
        old_y   = data.get("y", [])
        old_x   = data.get("x", [])
        if len(old_x) != len(old_ids):                # older file without x
            old_x = [0.5] * len(old_ids)
        merged = {i: (yy, xx) for i, yy, xx in zip(old_ids, old_y, old_x)}
        merged.update(fixed)
        ids = sorted(merged)
        with open(fpath, "w") as f:
            json.dump({"ids": ids,
                       "y": [merged[i][0] for i in ids],
                       "x": [merged[i][1] for i in ids]}, f)
        print(f"Updated {len(fixed)} entr{'y' if len(fixed)==1 else 'ies'} in {fpath}")


def main():
    ap = argparse.ArgumentParser(description="One Piece TCG anime face detector")
    ap.add_argument("cards_dir", nargs="?", default=None,
                    help="path to StreamingAssets/Cards (auto-detected if omitted)")
    ap.add_argument("--card", action="append", default=[],
                    help="debug a single card id (repeatable); writes annotated "
                         "PNGs to Cards/face-debug/ and patches face-data.json")
    ap.add_argument("--leaders", action="store_true",
                    help="debug EVERY leader card (the deck-select roster) — "
                         "annotated PNGs + face-data refresh for all of them")
    args = ap.parse_args()

    ensure_deps()
    import cv2

    cards = find_cards_dir(args.cards_dir)
    if not cards:
        print("ERROR: could not locate StreamingAssets/Cards. Pass the path as an argument.")
        return
    print("Cards folder:", cards)

    cpath = get_cascade(cards)
    if not cpath:
        print("ERROR: could not download anime-face model (check internet).")
        return
    cascade = cv2.CascadeClassifier(cpath)
    if cascade.empty():
        print("ERROR: cascade model failed to load.")
        return

    if args.leaders:
        lib = os.path.join(cards, "official-card-library.json")
        ids = []
        try:
            with open(lib, encoding="utf-8") as f:
                for c in json.load(f):
                    if str(c.get("type", "")).lower() == "leader" and c.get("id"):
                        ids.append(c["id"])
        except Exception as e:
            print("could not read official-card-library.json:", e)
            return
        if not ids:
            print("no leader cards found in official-card-library.json")
            return
        print(f"Auditing {len(set(ids))} leader cards…")
        debug_cards(cascade, cards, sorted(set(ids)))
        return

    if args.card:
        debug_cards(cascade, cards, args.card)
        return

    byid = os.path.join(cards, "OfficialById")
    pngs = []
    for root, _, files in os.walk(byid):
        for f in files:
            if f.lower().endswith(".png"):
                pngs.append(os.path.join(root, f))
    pngs.sort()
    print(f"Processing {len(pngs)} card images…")

    ids_out, ys_out, xs_out = [], [], []
    tiers = {"cascade-strict": 0, "cascade-loose": 0, "skin-fallback": 0, "miss": 0}

    for i, p in enumerate(pngs):
        cid = os.path.splitext(os.path.basename(p))[0]
        img = cv2.imread(p)
        if img is None:
            continue

        ey, ex, tier, _box = detect_one(cascade, img)
        tiers[tier] = tiers.get(tier, 0) + 1

        if ey is not None:
            ids_out.append(cid)
            ys_out.append(clamp_frac(ey))
            xs_out.append(clamp_x(ex))

        if (i + 1) % 250 == 0:
            print(f"  {i+1}/{len(pngs)}  cascade(strict)={tiers['cascade-strict']}"
                  f"  cascade(loose)={tiers['cascade-loose']}"
                  f"  skin={tiers['skin-fallback']}  miss={tiers['miss']}")

    out = os.path.join(cards, "face-data.json")
    with open(out, "w") as f:
        json.dump({"ids": ids_out, "y": ys_out, "x": xs_out}, f)

    total = len(pngs)
    print(f"\nDone.  {total} images:")
    print(f"  cascade (strict): {tiers['cascade-strict']}")
    print(f"  cascade (loose):  {tiers['cascade-loose']}")
    print(f"  skin fallback:    {tiers['skin-fallback']}")
    print(f"  no detection:     {tiers['miss']}  (non-human/stage/event cards — C# heuristic applies)")
    print(f"  face-data.json:   {len(ids_out)} entries")
    print("Wrote:", out)


if __name__ == "__main__":
    main()
