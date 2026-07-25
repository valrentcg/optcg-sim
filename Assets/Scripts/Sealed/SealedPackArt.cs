// One Piece TCG — Sealed / Pre-Release: booster-pack artwork resolution.
//
// Pack art is a DROP-IN, never a hard dependency. The opening animation must look right the moment
// the mode ships, so a set with no downloaded wrapper still renders a proper foil pack built from
// the set's own colour identity. Drop a real wrapper in and it is picked up automatically with no
// code change.
//
// Lookup order for set "OP16":
//   1. StreamingAssets/Cards/Packs/OP16.png   (drop real wrapper art here)
//   2. procedural foil pack generated from the set's dominant colours
//
// Fetching the real wrappers is left to Tools/fetch_pack_art.py, which the project owner runs
// against the official product pages — the same way the card art in StreamingAssets/Cards got
// there. Nothing here downloads anything at runtime.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sealed
{
    public static class SealedPackArt
    {
        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Where a real wrapper image goes, if you have one.</summary>
        public static string LocalPath(string setCode) =>
            Path.Combine(Application.dataPath, "StreamingAssets", "Cards", "Packs", setCode + ".png");

        /// <summary>Wrapper art for a set — real if present, procedural otherwise. Cached per set.</summary>
        public static Sprite For(string setCode)
        {
            if (string.IsNullOrEmpty(setCode)) setCode = "OP";
            if (cache.TryGetValue(setCode, out var cached) && cached != null) return cached;

            Sprite sprite = LoadReal(setCode) ?? Procedural(setCode);
            cache[setCode] = sprite;
            return sprite;
        }

        public static bool HasRealArt(string setCode) => File.Exists(LocalPath(setCode));

        private static Sprite LoadReal(string setCode)
        {
            try
            {
                string path = LocalPath(setCode);
                if (!File.Exists(path)) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(File.ReadAllBytes(path))) return null;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SealedPackArt] Could not load wrapper for {setCode}: {e.Message}");
                return null;
            }
        }

        /// <summary>The two colours a set is "about", taken from the colours its own cards actually use.
        /// A set led by Black/Yellow produces a black-and-gold wrapper without anyone authoring one.</summary>
        public static (Color top, Color bottom) SetColours(string setCode)
        {
            var tally = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string prefix = setCode + "-";
            foreach (var kv in CardData.Library)
            {
                if (!kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var c in SealedPool.SplitColors(kv.Value?.Color))
                    tally[c] = tally.TryGetValue(c, out var n) ? n + 1 : 1;
            }
            var ranked = tally.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
            Color a = Named(ranked.ElementAtOrDefault(0));
            Color b = Named(ranked.ElementAtOrDefault(1));
            if (ranked.Count < 2) b = Color.Lerp(a, Color.black, 0.55f);
            return (a, b);
        }

        private static Color Named(string colour) => (colour ?? "").ToLowerInvariant() switch
        {
            "red" => new Color32(196, 54, 54, 255),
            "green" => new Color32(46, 150, 92, 255),
            "blue" => new Color32(48, 116, 196, 255),
            "purple" => new Color32(126, 72, 176, 255),
            "black" => new Color32(48, 50, 58, 255),
            "yellow" => new Color32(214, 178, 46, 255),
            _ => new Color32(70, 90, 120, 255),
        };

        /// <summary>A foil-looking wrapper drawn from the set's colours: a diagonal gradient, a bright
        /// sheen band, and a darker heat-seal strip across the top where the pack tears.</summary>
        private static Sprite Procedural(string setCode)
        {
            const int W = 220, H = 320;
            var (top, bottom) = SetColours(setCode);
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var px = new Color32[W * H];

            // A wrapper, not a rectangle: rounded corners cut out of the alpha, crimped serrated seals
            // top and bottom, layered holo bands, and a soft inner shadow so it reads as a bagged
            // object with depth.
            const float corner = 0.055f;          // corner radius, as a fraction of the width
            const float sealTop = 0.885f;         // heat-seal band the rip tears along
            const float sealBottom = 0.075f;

            for (int y = 0; y < H; y++)
            {
                float v = (float)y / (H - 1);
                for (int x = 0; x < W; x++)
                {
                    float u = (float)x / (W - 1);

                    // --- silhouette: rounded rectangle via a corner distance test ---
                    float rx = corner, ry = corner * W / (float)H;
                    float dx = Mathf.Max(Mathf.Abs(u - 0.5f) - (0.5f - rx), 0f) / rx;
                    float dy = Mathf.Max(Mathf.Abs(v - 0.5f) - (0.5f - ry), 0f) / ry;
                    float corner01 = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = 1f - Mathf.SmoothStep(0.92f, 1.04f, corner01);
                    if (alpha <= 0.001f) { px[y * W + x] = new Color(0, 0, 0, 0); continue; }

                    Color c = Color.Lerp(bottom, top, Mathf.Clamp01(v * 0.85f + u * 0.15f));

                    // --- layered foil: one broad sheen plus a tighter secondary band ---
                    float band = Mathf.Abs(Mathf.Sin((u * 2.4f + v * 1.6f) * Mathf.PI));
                    c = Color.Lerp(c, Color.white, Mathf.Pow(band, 12f) * 0.55f);
                    float band2 = Mathf.Abs(Mathf.Sin((u * 5.1f - v * 3.2f) * Mathf.PI));
                    c = Color.Lerp(c, Color.white, Mathf.Pow(band2, 26f) * 0.30f);

                    // --- crimped seals: fine vertical serration, darker and desaturated ---
                    bool inSeal = v > sealTop || v < sealBottom;
                    if (inSeal)
                    {
                        float crimp = Mathf.Abs(Mathf.Sin(u * W * 0.16f));   // tight vertical ribs
                        c = Color.Lerp(c, Color.black, 0.30f);
                        c = Color.Lerp(c, Color.white, Mathf.Pow(crimp, 3f) * 0.22f);
                        // A dark scored line where the pack is meant to be torn.
                        float scoreDist = Mathf.Abs(v - (sealTop + 0.012f));
                        if (v > sealTop && scoreDist < 0.006f) c = Color.Lerp(c, Color.black, 0.55f);
                    }

                    // --- inner shadow + vignette, so it looks bagged rather than printed ---
                    float edge = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
                    c = Color.Lerp(c * 0.5f, c, Mathf.SmoothStep(0f, 0.085f, edge));
                    // Faint horizontal crease across the middle where a pack naturally bends.
                    float crease = Mathf.Exp(-Mathf.Pow((v - 0.47f) * 22f, 2f));
                    c = Color.Lerp(c, c * 1.16f, crease * 0.5f);

                    c.a = alpha;
                    px[y * W + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
