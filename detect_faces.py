#!/usr/bin/env python3
"""
One Piece TCG Deck Builder — anime face detector (run once).

Writes  StreamingAssets/Cards/face-data.json  with, per card id:
    y — eye-line fraction (0 = top of card, 1 = bottom)
    x — face centre fraction (0 = left)
    z — hex zoom multiplier (1 = default 0.44-card-height window; <1 = tighter)
The deck builder reads this at startup to frame character art in deck rows and
to centre the hex-roster crops on the character's face. z lets the hex zoom in
on small or off-centre faces and on ONE character of a duo (Luffy & Ace,
Zoro & Sanji, ...) instead of splitting the frame between two half-faces.

Detection pipeline:
  1. Cascade sweep — strict passes (minNeighbors 3-5) at 1.0/1.6/2.2x upscale,
     a loose pass (mn 2) at native scale, and an "ultra" rescue sweep (mn 2,
     up to 3.2x, CLAHE + equalized) for dark/shadowed faces. ALL detections
     become scored candidates — nothing is hard-rejected for being high or low
     in the art; position only weighs the score. The best-scored box wins, so
     a real face beats the belly-button/knuckle false positives that used to
     hijack the crop whenever the true face was discarded.
  2. Duo handling — if a second strong face exists far from the first, the
     framer either includes both (when they genuinely fit in one hex window)
     or picks the primary face and tightens z to focus it.
  3. Skin-blob fallback — as before, for cards where the cascade finds nothing.
  4. OVERRIDES — hand-tuned entries for art the detector can't read (dark
     silhouettes, abstract cards). Applied last; survives re-runs.

USAGE:
    python detect_faces.py
    python detect_faces.py "path/to/StreamingAssets/Cards"

    # Inspect specific cards (e.g. a leader whose hex crop looks wrong):
    #   prints tier + face + hex window, writes annotated PNGs (face boxes
    #   green, chosen face crosshair red, hex crop window yellow) to
    #   face-debug/<ID>.png, and patches those ids inside face-data.json.
    python detect_faces.py --card OP07-038 --card OP01-001

    # Audit the whole deck-select roster (annotate EVERY leader card and
    # refresh their face-data entries):
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

# ── Hand-tuned entries for art the detector cannot read ─────────────────────
# id: (eye_y, face_x, zoom). Checked visually against the actual hex render.
# These are applied AFTER detection on every run, so they survive full re-runs
# and --leaders refreshes. Add to this table rather than hand-editing the JSON.
OVERRIDES = {
    "EB01-021": (0.130, 0.370, 0.85),   # Hannyabal — bent-over pose, head upper-left
    "EB01-040": (0.435, 0.420, 0.80),   # Kyros — dark screaming face low in the art
    "OP02-001": (0.155, 0.310, 0.90),   # Edward Newgate — mustache hides the face from the model
    "OP02-026": (0.105, 0.275, 0.85),   # Sanji — flames out-detect the face
    "OP03-076": (0.135, 0.300, 0.90),   # Rob Lucci — dark face under hat; slash effect wins
    "OP05-022": (0.145, 0.320, 0.85),   # Rosinante — flame false positive top-right
    "OP06-020": (0.295, 0.600, 0.90),   # Hody Jones — big hand in front of the face
    "OP06-080": (0.255, 0.720, 0.80),   # Gecko Moria — dark purple face, cascade-invisible
    "OP07-038": (0.225, 0.160, 0.70),   # Boa Hancock — face far left, arm raised across frame
    "OP08-002": (0.210, 0.465, 0.85),   # Marco — blue flames confuse the model
    "OP09-062": (0.150, 0.600, 0.85),   # Nico Robin — tilted-up face, flower false positive
    "OP10-003": (0.225, 0.300, 0.85),   # Sugar — crown detected instead of face
    "OP11-021": (0.355, 0.630, 0.80),   # Jinbe — blue skin; poster art false positives
    "OP11-040": (0.325, 0.475, 0.90),   # Luffy (nightmare) — very dark art
    "OP12-020": (0.105, 0.460, 0.80),   # Zoro — bandana + sword-in-mouth, face at very top
    "OP13-079": (0.300, 0.500, 1.00),   # Imu — dark silhouette + butterflies; centre the art
    "OP01-061": (0.200, 0.635, 0.85),   # Kaido — dark face between the horns
    "ST10-003": (0.130, 0.320, 0.85),   # Kid — screaming face upper-left, machinery right
    "OP15-098": (0.370, 0.770, 0.80),   # Luffy — laughing head thrown back, lower right
    "P-076":    (0.215, 0.280, 0.85),   # Sakazuki — magma reads as skin everywhere
    "ST05-001": (0.145, 0.530, 0.85),   # Shanks — face high, hair blends into background
    "ST12-001": (0.410, 0.330, 0.80),   # Zoro & Sanji duo — focus Zoro (sword in mouth, low-left)
    "ST30-001": (0.280, 0.440, 1.00),   # Luffy & Ace duo — both faces fit one window
    "OP09-016": (0.300, 0.300, 0.85),   # Rockstar — wink + spiky hair confuses the eye-line
    # Character cards whose art defeats the detector (hex + decklist rows):
    "EB02-037": (0.150, 0.300, 0.90),   # Franky — grayscale art, hand covering face
    "EB03-031": (0.235, 0.250, 0.85),   # Reiju — busy pink art, face upper-left
    "OP04-063": (0.140, 0.470, 0.85),   # Franky — flexing, face at very top
    "OP12-063": (0.210, 0.280, 0.85),   # Reiju — inverted kicking pose
    "OP15-066": (0.135, 0.620, 0.85),   # Satori — tiny face under the hat, giant white body
    "OP15-060": (0.430, 0.250, 0.85),   # Enel — dark art; white beast head lower-left
    "OP10-067": (0.220, 0.420, 0.85),   # Senor Pink — dark noir art (SAMPLE print)
    "OP15-089": (0.315, 0.500, 0.85),   # Franky — fists forward, face mid-frame
    "OP14-065": (0.310, 0.550, 0.85),   # Senor Pink — desk scene, face mid-right
    "OP11-070": (0.225, 0.620, 0.85),   # Pudding — big hair out-detects the face
    # Starter/LT alt-art leader prints (StarterLeaderArtOverride ids):
    "OP12-020_p3": (0.430, 0.400, 0.78), # Zoro LT01 alt — head bent low, sword in mouth
    "ST21-001_p2": (0.415, 0.315, 0.80), # G5 Luffy LT01 alt — face low-left under the hair swirls
    "OP02-001_p2": (0.245, 0.720, 0.80), # Whitebeard ST15 alt — face upper-right (SAMPLE art)
}


def preprocess(gray_art):
    import cv2
    clahe = cv2.createCLAHE(clipLimit=2.5, tileGridSize=(8, 8))
    return clahe.apply(gray_art)


def eye_fraction_from_box(box, full_h, art_h, img=None):
    """
    Eye line as a fraction of full card height.

    Baseline heuristic: lbpcascade boxes usually include the hair crown, so
    eyes sit ~33% down the box; very tall boxes that start right at the top of
    the art tend to span crown-to-chin, where eyes sit closer to 40%.

    When the card image is provided, the estimate is REFINED by locating the
    actual eye line inside the box: anime eyes are the strongest horizontal
    band of dark, high-contrast marks in the middle of the face. This matters
    most for the decklist rows, whose thin slice makes a few-percent error the
    difference between showing eyes and showing forehead (spiky hair pushes
    the box top up → 33% lands in hair) or chin.
    """
    x, y, w, h = box
    off = 0.40 if (y < art_h * 0.10 and h > full_h * 0.14) else 0.33
    base = (y + off * h) / full_h
    if img is None:
        return base

    import cv2, numpy as np
    H, W = img.shape[:2]
    # Central 64% of the box width (skip hair/ears), 18%..82% of its height.
    x0 = max(0, int(x + w * 0.18)); x1 = min(W, int(x + w * 0.82))
    y0 = max(0, int(y + h * 0.18)); y1 = min(H, int(y + h * 0.82))
    if x1 - x0 < 8 or y1 - y0 < 8:
        return base
    roi = cv2.cvtColor(img[y0:y1, x0:x1], cv2.COLOR_BGR2GRAY)
    dark = (255.0 - roi.astype(np.float32)) / 255.0          # dark strokes
    grad = np.abs(cv2.Sobel(roi, cv2.CV_32F, 0, 1, ksize=3)) / 255.0
    prof = (dark * dark * (0.35 + grad)).mean(axis=1)        # per-row energy
    k = max(3, (y1 - y0) // 12) | 1
    prof = cv2.GaussianBlur(prof.reshape(-1, 1), (1, k), 0).ravel()
    peak = int(np.argmax(prof))
    # Only trust a clear peak, and never wander far from the prior — a dark
    # mouth/eyebrow band scores too, so the prior keeps us honest.
    if prof[peak] < 0.02:
        return base
    refined = (y0 + peak) / full_h
    lo, hi = (y + 0.22 * h) / full_h, (y + 0.58 * h) / full_h
    refined = min(max(refined, lo), hi)
    # Asymmetric trust: a dark band BELOW the prior is usually the actual
    # eyes (the prior lands in the forehead when hair inflates the box), but
    # a dark band ABOVE it is usually hair shadow or eyebrows — lean on the
    # prior much harder in that direction.
    wgt = 0.65 if refined >= base else 0.30
    return wgt * refined + (1.0 - wgt) * base


def skin_fraction(img_bgr, box):
    """Fraction of warm skin-toned pixels inside a box (sanity check)."""
    import cv2, numpy as np
    x, y, w, h = box
    H, W = img_bgr.shape[:2]
    x0, y0 = max(0, x), max(0, y)
    x1, y1 = min(W, x + w), min(H, y + h)
    if x1 <= x0 or y1 <= y0:
        return 0.0
    hsv = cv2.cvtColor(img_bgr[y0:y1, x0:x1], cv2.COLOR_BGR2HSV)
    m = cv2.inRange(hsv, np.array([0, 12, 90], np.uint8), np.array([22, 210, 255], np.uint8))
    m |= cv2.inRange(hsv, np.array([0, 3, 175], np.uint8), np.array([22, 55, 255], np.uint8))
    return cv2.countNonZero(m) / float(m.size)


def collect_candidates(cascade, img):
    """
    Run the cascade at several params/scales/enhancements and return deduped
    face candidates as (x, y, w, h, tier, votes) in full-card pixels.
    tier: 0 = strict, 1 = loose, 2 = ultra(rescue).
    votes counts how many independent passes re-detected the same box — real
    faces fire across several scales/param sets, false positives rarely do.
    Only candidates of the BEST tier present survive: loose boxes are used
    when nothing strict exists, ultra (skin-checked) when nothing else does,
    so the rescue sweeps can never override a confident detection.
    """
    import cv2

    full_h, W = img.shape[:2]
    art_h = int(full_h * ART_FRACTION)
    art_bgr = img[:art_h]
    gray0 = cv2.cvtColor(art_bgr, cv2.COLOR_BGR2GRAY)
    gray_c = preprocess(gray0)
    gray_e = cv2.equalizeHist(gray0)

    raw = []

    def sweep(gray, nn, msf, scale, tier):
        if scale == 1.0:
            g = gray
        else:
            g = cv2.resize(gray, None, fx=scale, fy=scale, interpolation=cv2.INTER_CUBIC)
        sw, sa = int(W * scale), int(art_h * scale)
        ms = (int(sw * msf), int(sa * msf))
        faces = cascade.detectMultiScale(g, scaleFactor=1.03, minNeighbors=nn, minSize=ms)
        inv = 1.0 / scale
        for (x, y, w, h) in (faces if len(faces) else []):
            raw.append((int(x * inv), int(y * inv), int(w * inv), int(h * inv), tier))

    # Strict: high confidence at multiple upscales (small faces need upscale
    # to clear the LBP model's ~24px floor).
    for (nn, msf) in [(4, 0.07), (3, 0.055), (5, 0.09)]:
        for scale in (1.0, 1.6, 2.2):
            sweep(gray_c, nn, msf, scale, 0)
    # Loose: native scale only.
    sweep(gray_c, 2, 0.05, 1.0, 1)
    # Ultra rescue: dark/shadowed faces (Roger, Whitebeard...) — consulted only
    # when the tiers above found nothing at all, and skin-validated.
    for g in (gray_c, gray_e):
        for scale in (1.6, 2.4, 3.2):
            sweep(g, 2, 0.045, scale, 2)

    def iou(a, b):
        ax0, ay0, aw, ah = a[:4]; bx0, by0, bw, bh = b[:4]
        ax1, ay1, bx1, by1 = ax0 + aw, ay0 + ah, bx0 + bw, by0 + bh
        ix = max(0, min(ax1, bx1) - max(ax0, bx0))
        iy = max(0, min(ay1, by1) - max(ay0, by0))
        inter = ix * iy
        return inter / float(aw * ah + bw * bh - inter + 1e-6)

    # Merge overlapping raw hits into candidates, counting votes.
    raw.sort(key=lambda c: (c[4], -(c[2] * c[3])))       # best tier first, then area
    kept = []
    for c in raw:
        x, y, w, h, tier = c
        # Speck / clearly-in-the-lower-art boxes are noise: real faces centre
        # above 62% of art height (empirically; the false positives on action
        # cards — fists, navels, effects — land at 63%+).
        if w < W * 0.045 or (y + h * 0.5) > art_h * 0.62:
            continue
        merged = False
        for k in kept:
            if iou(c, k[0]) >= 0.35:
                k[1] += (1.0, 0.6, 0.4)[tier]
                merged = True
                break
        if not merged:
            kept.append([c, (1.0, 0.6, 0.4)[tier]])

    if not kept:
        return []
    best_tier = min(k[0][4] for k in kept)
    out = []
    for (x, y, w, h, tier), votes in kept:
        if tier != best_tier:
            continue
        # Ultra hits are rescue-grade: require some skin inside the box so a
        # texture fluke on clothing/effects can't hijack the crop, and reject
        # boxes hugging the very top of the art — that's where cost badges,
        # power text and frame trim live, and rescue-tier hits there are junk.
        if tier == 2 and (skin_fraction(img, (x, y, w, h)) < 0.08
                          or (y + h * 0.5) < art_h * 0.10):
            continue
        out.append((x, y, w, h, tier, votes))
    return out


def score_candidate(c, W, art_h):
    """
    Rank face boxes: bigger, higher, more central and more re-detected is
    better. Soft weights only — a big central real face fairly low in the art
    can still beat a small false positive up top, but belly buttons and
    knuckles (small, low, weakly re-detected) reliably lose to the real face.
    """
    x, y, w, h, tier, votes = c
    cx = (x + w * 0.5) / W
    cy = (y + h * 0.5) / float(art_h)
    area = (w * h) / float(W * art_h)
    central = 1.0 - 0.45 * min(1.0, abs(cx - 0.5) * 2.0)
    height = 1.0 if cy <= 0.45 else max(0.35, 1.0 - 1.2 * (cy - 0.45))
    conf = 0.55 + 0.15 * min(votes, 5.0)
    return (area ** 0.7) * central * height * conf


# ─────────────────────────────────────────────────────────────────────────────
# Hex framing — must mirror DeckBuilderManager.BuildDeckHexCell /
# BuildStarterDeckHexCell: a window centred on the face in both axes, clamped
# inside the card's illustration region so the hex only ever contains art and
# NEVER reaches the card border or the text box.
HEX_ASPECT = 2.0 / (3.0 ** 0.5)           # flat-top hex: width / height ≈ 1.1547
SAFE_L, SAFE_R = 0.07, 0.93
SAFE_T, SAFE_B = 0.05, 0.60
HEX_VIS_H = 0.44                          # base zoom: fraction of card height shown
ZOOM_MIN, ZOOM_MAX = 0.68, 1.00
FACE_TARGET = 0.28                        # ideal face height as fraction of the window


def hex_window(eye_frac, x_frac, card_aspect, zoom=1.0):
    """The exact crop rectangle the hex will show, as card fractions."""
    vis_h = HEX_VIS_H * zoom
    vis_w = vis_h * HEX_ASPECT / max(card_aspect, 0.01)
    if vis_w > SAFE_R - SAFE_L:
        vis_w = SAFE_R - SAFE_L
        vis_h = vis_w * card_aspect / HEX_ASPECT
    # Face sits 45% down the window (a touch of headroom for hair/hats).
    cx = min(max(x_frac, SAFE_L + vis_w / 2), SAFE_R - vis_w / 2)
    cy = min(max(eye_frac + vis_h * 0.05, SAFE_T + vis_h / 2), SAFE_B - vis_h / 2)
    return cx - vis_w / 2, cy - vis_h / 2, cx + vis_w / 2, cy + vis_h / 2


def face_placement(eye, fx, card_aspect, zoom):
    """Where the face lands inside the window (0..1 in each axis)."""
    wl, wt, wr, wb = hex_window(eye, fx, card_aspect, zoom)
    px = (fx - wl) / max(wr - wl, 1e-6)
    py = (eye - wt) / max(wb - wt, 1e-6)
    return px, py


def choose_zoom(eye, fx, h_face_frac, card_aspect):
    """
    Pick z so the face reads at a good size AND lands well inside the hex.
    Small faces (full-body poses) zoom in; faces near the card's left/right
    edge zoom in until the SAFE-clamped window can keep them off the hex's
    pointed corners. Never zooms out past the default window.
    """
    z = 1.0
    if h_face_frac is not None and h_face_frac > 0:
        z = (h_face_frac / FACE_TARGET) / HEX_VIS_H
    z = min(max(z, ZOOM_MIN), ZOOM_MAX)
    # Tighten until the face sits within the central 64% of the window
    # horizontally (the hex's pointed corners eat the outer ~18% per side).
    for _ in range(24):
        px, _ = face_placement(eye, fx, card_aspect, z)
        if 0.18 <= px <= 0.82 or z <= ZOOM_MIN:
            break
        z = max(ZOOM_MIN, z - 0.03)
    return round(z, 3)


def frame_faces(cands, img):
    """
    Turn scored candidates into (eye_y, x, zoom).

    Zoom is CONFIDENCE-GATED: the wide default window is very forgiving of a
    slightly-off detection, so we only tighten it when the face was
    re-detected across several passes (votes) — a lone weak box keeps z = 1
    and still lands in frame even if imprecise.

    Duos: include both faces only when one default window genuinely holds
    both; otherwise focus the primary face (bigger/better placed) and tighten
    the zoom so the hex clearly features ONE character.
    """
    full_h, W = img.shape[:2]
    art_h = int(full_h * ART_FRACTION)
    aspect = W / float(full_h)

    scored = sorted(((score_candidate(c, W, art_h), c) for c in cands), reverse=True)
    s0, prim = scored[0]
    eye0 = eye_fraction_from_box(prim[:4], full_h, art_h, img)
    fx0 = (prim[0] + prim[2] * 0.5) / W
    hf0 = prim[3] / float(full_h)
    confident = prim[5] >= 3.0                        # re-detected ≥3 pass-votes

    def zoom_for(eye, fx, hf):
        return choose_zoom(eye, fx, hf, aspect) if confident else 1.0

    # Secondary face: strong, comparable size, re-detected itself, and
    # clearly a different character (far away horizontally).
    second = None
    for s, c in scored[1:]:
        if s < s0 * 0.30 or (c[2] * c[3]) < (prim[2] * prim[3]) * 0.30:
            continue
        if c[5] < 2.0:
            continue
        if abs(((c[0] + c[2] * 0.5) / W) - fx0) < 0.18:
            continue
        second = c
        break

    if second is not None:
        eye1 = eye_fraction_from_box(second[:4], full_h, art_h, img)
        fx1 = (second[0] + second[2] * 0.5) / W
        # Weighted midpoint (bigger face pulls harder).
        a0, a1 = prim[2] * prim[3], second[2] * second[3]
        mx = (fx0 * a0 + fx1 * a1) / (a0 + a1)
        my = (eye0 * a0 + eye1 * a1) / (a0 + a1)
        wl, wt, wr, wb = hex_window(my, mx, aspect, 1.0)
        vis_w, vis_h = wr - wl, wb - wt
        # Both faces must land inside the window's central region — heads
        # whole, off the pointed corners. Otherwise: focus ONE character.
        def inside(fx, ey, hf):
            px = (fx - wl) / vis_w
            py = (ey - wt) / vis_h
            return 0.16 <= px <= 0.84 and 0.10 <= py <= 0.72 and hf < vis_h * 0.62
        if inside(fx0, eye0, hf0) and inside(fx1, eye1, second[3] / float(full_h)):
            return my, mx, 1.0, "duo-both"
        return eye0, fx0, zoom_for(eye0, fx0, hf0), "duo-focus"

    return eye0, fx0, zoom_for(eye0, fx0, hf0), None


def skin_fallback(img_bgr_art, art_h, full_h):
    """
    Estimate the face position from skin-coloured regions when the cascade
    fails. Finds connected skin BLOBS and picks the best "head" candidate: a
    reasonably large blob that starts high in the art. The face estimate is
    the centroid of that blob's TOP SLICE (the head part), not the whole
    blob, so face+neck+chest merging into one component doesn't pull the
    estimate down to the sternum.

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
    # Min saturation 10: genuinely pale anime skin keeps a warm tint, while
    # pure white/cream backgrounds (S≈0) used to flood this mask and drag the
    # fallback to the top of bright cards.
    m_pale = cv2.inRange(hsv,
        np.array([0,  10, 175], np.uint8),
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
        # HEIGHT — prefer blobs starting higher (heads over torsos). Slightly
        # stronger than before: giant chest blobs on shirtless characters used
        # to out-area the actual head and drag the crop to the sternum.
        height = 1.0 - 0.62 * (top / float(search_h))
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
    Returns (eye_fraction, x_fraction, zoom, tier_name, boxes) where boxes is
    the deduped candidate list (may be empty) in full-card pixel coordinates.
    eye_fraction/x_fraction are None when nothing at all was found.
    """
    full_h, W = img.shape[:2]
    art_h = int(full_h * ART_FRACTION)

    cands = collect_candidates(cascade, img)
    if cands:
        ey, ex, z, duo = frame_faces(cands, img)
        tiers = ("cascade-strict", "cascade-loose", "cascade-ultra")
        best = min(c[4] for c in cands)
        tier = duo if duo else tiers[best]
        return ey, ex, z, tier, cands

    res = skin_fallback(img[:art_h], art_h, full_h)
    if res is not None:
        ey, ex = res
        return ey, ex, 1.0, "skin-fallback", []
    return None, None, 1.0, "miss", []


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


def load_face_data(fpath):
    data = {"ids": [], "y": [], "x": [], "z": []}
    if os.path.isfile(fpath):
        try:
            with open(fpath) as f:
                data = json.load(f)
        except Exception as e:
            print("face-data.json unreadable, rebuilding entries:", e)
    ids = data.get("ids", [])
    ys  = data.get("y", [])
    xs  = data.get("x", [])
    zs  = data.get("z", [])
    if len(xs) != len(ids):                # older file without x
        xs = [0.5] * len(ids)
    if len(zs) != len(ids):                # older file without z
        zs = [1.0] * len(ids)
    return {i: (yy, xx, zz) for i, yy, xx, zz in zip(ids, ys, xs, zs)}


def save_face_data(fpath, merged):
    ids = sorted(merged)
    with open(fpath, "w") as f:
        json.dump({"ids": ids,
                   "y": [merged[i][0] for i in ids],
                   "x": [merged[i][1] for i in ids],
                   "z": [merged[i][2] for i in ids]}, f)


def apply_overrides(entries):
    for cid, (oy, ox, oz) in OVERRIDES.items():
        if cid in entries or True:   # overrides apply even if detection missed
            entries[cid] = (round(oy, 3), round(ox, 3), round(oz, 3))
    return entries


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

        ey, ex, z, tier, boxes = detect_one(cascade, img)
        if cid in OVERRIDES:
            ey, ex, z = OVERRIDES[cid]
            tier = "override"
        full_h, W = img.shape[:2]
        vis = img.copy()

        for (x, y, w, h, _t, _v) in boxes:                    # face boxes (green)
            cv2.rectangle(vis, (x, y), (x + w, y + h), (0, 255, 0), 3)
        if ey is not None:
            ey_c, ex_c = clamp_frac(ey), clamp_x(ex)
            fixed[os.path.splitext(os.path.basename(p))[0]] = (ey_c, ex_c, z)
            ey_px, ex_px = int(ey_c * full_h), int(ex_c * W)  # face crosshair (red)
            cv2.line(vis, (0, ey_px), (W, ey_px), (0, 0, 255), 3)
            cv2.line(vis, (ex_px, 0), (ex_px, full_h), (0, 0, 255), 2)
            aspect = W / full_h
            wl, wt, wr, wb = hex_window(ey_c, ex_c, aspect, z)     # hex window (yellow)
            cv2.rectangle(vis, (int(wl * W), int(wt * full_h)),
                          (int(wr * W), int(wb * full_h)), (0, 220, 255), 3)
            print(f"{cid}: tier={tier}  face=({ex_c:.3f}, {ey_c:.3f})  z={z:.2f}"
                  f"  hex-crop=x {wl:.3f}..{wr:.3f}, y {wt:.3f}..{wb:.3f}")
        else:
            print(f"{cid}: tier={tier}  NO DETECTION — C# skin heuristic will apply")

        out_png = os.path.join(dbg_dir, os.path.basename(p))
        cv2.imwrite(out_png, vis)

    # Patch just these ids into the existing face-data.json (no full re-run).
    if fixed:
        fpath = os.path.join(cards, "face-data.json")
        merged = load_face_data(fpath)
        merged.update(fixed)
        apply_overrides(merged)
        save_face_data(fpath, merged)
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
        # Include alt-art prints of leaders (e.g. ST21-001_p2, OP12-020_p3) —
        # the starter-deck browser shows these via StarterLeaderArtOverride,
        # so their hex crops need face data of their own.
        idset = set(ids)
        byid = os.path.join(cards, "OfficialById")
        for root, _, files in os.walk(byid):
            for fn in files:
                stem, ext = os.path.splitext(fn)
                if ext.lower() != ".png" or "_p" not in stem:
                    continue
                base = stem.split("_p")[0]
                if base in idset:
                    idset.add(stem)
        print(f"Auditing {len(idset)} leader cards (incl. alt-art prints)…")
        debug_cards(cascade, cards, sorted(idset))
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

    entries = {}
    tiers = {"cascade-strict": 0, "cascade-loose": 0, "cascade-ultra": 0,
             "duo-both": 0, "duo-focus": 0, "skin-fallback": 0, "miss": 0}

    for i, p in enumerate(pngs):
        cid = os.path.splitext(os.path.basename(p))[0]
        img = cv2.imread(p)
        if img is None:
            continue

        ey, ex, z, tier, _boxes = detect_one(cascade, img)
        tiers[tier] = tiers.get(tier, 0) + 1

        if ey is not None:
            entries[cid] = (clamp_frac(ey), clamp_x(ex), z)

        if (i + 1) % 250 == 0:
            print(f"  {i+1}/{len(pngs)}  strict={tiers['cascade-strict']}"
                  f"  loose={tiers['cascade-loose']}  ultra={tiers['cascade-ultra']}"
                  f"  duo={tiers['duo-both']+tiers['duo-focus']}"
                  f"  skin={tiers['skin-fallback']}  miss={tiers['miss']}")

    apply_overrides(entries)
    out = os.path.join(cards, "face-data.json")
    save_face_data(out, entries)

    total = len(pngs)
    print(f"\nDone.  {total} images:")
    print(f"  cascade (strict): {tiers['cascade-strict']}")
    print(f"  cascade (loose):  {tiers['cascade-loose']}")
    print(f"  cascade (ultra):  {tiers['cascade-ultra']}")
    print(f"  duo both/focus:   {tiers['duo-both']}/{tiers['duo-focus']}")
    print(f"  skin fallback:    {tiers['skin-fallback']}")
    print(f"  no detection:     {tiers['miss']}  (non-human/stage/event cards — C# heuristic applies)")
    print(f"  face-data.json:   {len(entries)} entries")
    print("Wrote:", out)


if __name__ == "__main__":
    main()
