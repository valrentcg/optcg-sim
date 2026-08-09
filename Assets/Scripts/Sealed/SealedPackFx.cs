// One Piece TCG — Sealed / Pre-Release: reusable uGUI light primitives for the pack opening.
// The active sequence uses feathered additive sprites instead of square particle debris.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OnePieceTcg.Sealed
{
    public static class SealedPackFx
    {
        // ---- building blocks -------------------------------------------------------------------

        public static RectTransform Quad(RectTransform parent, string name, Color colour, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = colour;
            img.raycastTarget = false;
            return rt;
        }

        private static void SetAlpha(RectTransform rt, float a)
        {
            if (rt == null) return;
            var img = rt.GetComponent<Image>();
            if (img == null) return;
            var c = img.color; c.a = a; img.color = c;
        }

        public static float EaseOut(float k) => 1f - Mathf.Pow(1f - Mathf.Clamp01(k), 3f);
        public static float EaseIn(float k) => Mathf.Pow(Mathf.Clamp01(k), 2.2f);

        // ---- anticipation ----------------------------------------------------------------------

        /// <summary>A specular band that travels across the pack on a diagonal, clipped to the pack's
        /// own rect. This is what stops the wrapper reading as a flat rectangle while it sits there.</summary>
        public static IEnumerator Glint(RectTransform pack, float dur, float alpha = 0.5f)
        {
            yield return GlintTinted(pack, dur, alpha, Color.white);
        }

        public static IEnumerator GlintTinted(RectTransform pack, float dur, float alpha, Color tint)
        {
            if (pack == null) yield break;

            // Clip to the pack so the band never spills onto the backdrop.
            var clip = Quad(pack, "Glint Clip", new Color(0, 0, 0, 0), pack.sizeDelta);
            clip.gameObject.AddComponent<RectMask2D>();

            float w = pack.sizeDelta.x, h = pack.sizeDelta.y;
            var band = Quad(clip, "Glint", new Color(tint.r, tint.g, tint.b, alpha), new Vector2(w * 0.20f, h * 1.9f));
            band.localRotation = Quaternion.Euler(0, 0, 22f);

            float t = 0f;
            while (t < dur && band != null)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                band.anchoredPosition = new Vector2(Mathf.Lerp(-w * 1.1f, w * 1.1f, k), 0f);
                // Brightest mid-sweep so it reads as a highlight rolling over a curved foil surface.
                SetAlpha(band, alpha * Mathf.Sin(k * Mathf.PI));
                yield return null;
            }
            if (clip != null) UnityEngine.Object.Destroy(clip.gameObject);
        }

        // ---- the tear --------------------------------------------------------------------------

        /// <summary>Jagged paper teeth along a horizontal tear line. Small squares rotated 45° read as
        /// a serrated torn edge at this size, and cost nothing.</summary>
        public static List<RectTransform> TornTeeth(RectTransform parent, float width, float y,
            Color colour, int count, float size)
        {
            var teeth = new List<RectTransform>(count);
            for (int i = 0; i < count; i++)
            {
                float x = Mathf.Lerp(-width * 0.5f, width * 0.5f, count == 1 ? 0.5f : i / (float)(count - 1));
                var tooth = Quad(parent, "Tooth", colour, Vector2.one * size * UnityEngine.Random.Range(0.7f, 1.25f));
                tooth.anchoredPosition = new Vector2(x, y + UnityEngine.Random.Range(-size * 0.35f, size * 0.35f));
                tooth.localRotation = Quaternion.Euler(0, 0, 45f + UnityEngine.Random.Range(-18f, 18f));
                SetAlpha(tooth, 0f);          // revealed as the tear front passes
                teeth.Add(tooth);
            }
            return teeth;
        }

        /// <summary>Foil flecks thrown off the tear. Given real gravity and spin so the debris settles
        /// rather than fading in place.</summary>
        public static IEnumerator Confetti(RectTransform parent, Vector2 origin, Color[] palette,
            int count, float spread, float life)
        {
            var bits = new List<RectTransform>(count);
            var vel = new List<Vector2>(count);
            var spin = new List<float>(count);

            for (int i = 0; i < count; i++)
            {
                var c = palette[UnityEngine.Random.Range(0, palette.Length)];
                var bit = Quad(parent, "Fleck", c,
                    new Vector2(UnityEngine.Random.Range(4f, 9f), UnityEngine.Random.Range(7f, 15f)));
                bit.anchoredPosition = origin + new Vector2(UnityEngine.Random.Range(-spread, spread), 0f);
                bit.localRotation = Quaternion.Euler(0, 0, UnityEngine.Random.Range(0f, 360f));
                bits.Add(bit);
                vel.Add(new Vector2(UnityEngine.Random.Range(-320f, 320f), UnityEngine.Random.Range(120f, 560f)));
                spin.Add(UnityEngine.Random.Range(-520f, 520f));
            }

            float t = 0f;
            while (t < life)
            {
                float dt = Time.unscaledDeltaTime;
                t += dt;
                float k = Mathf.Clamp01(t / life);
                for (int i = 0; i < bits.Count; i++)
                {
                    if (bits[i] == null) continue;
                    var v = vel[i];
                    v.y -= 1500f * dt;                        // gravity
                    v.x *= 1f - 1.2f * dt;                    // air drag
                    vel[i] = v;
                    bits[i].anchoredPosition += v * dt;
                    bits[i].localRotation *= Quaternion.Euler(0, 0, spin[i] * dt);
                    SetAlpha(bits[i], 1f - EaseIn(k));
                }
                yield return null;
            }
            foreach (var b in bits) if (b != null) UnityEngine.Object.Destroy(b.gameObject);
        }

        /// <summary>Screen shake. Displaces the whole stage, so it reads as impact rather than as one
        /// object wobbling.</summary>
        public static IEnumerator Shake(RectTransform stage, float amplitude, float dur)
        {
            if (stage == null) yield break;
            Vector2 home = stage.anchoredPosition;
            float t = 0f;
            while (t < dur && stage != null)
            {
                t += Time.unscaledDeltaTime;
                float falloff = 1f - Mathf.Clamp01(t / dur);
                stage.anchoredPosition = home + new Vector2(
                    UnityEngine.Random.Range(-amplitude, amplitude) * falloff,
                    UnityEngine.Random.Range(-amplitude, amplitude) * falloff);
                yield return null;
            }
            if (stage != null) stage.anchoredPosition = home;
        }

        // ---- light -----------------------------------------------------------------------------

        /// <summary>Broad feathered rays around a point, slowly rotating. Returns the root so the
        /// caller can fade and destroy it.</summary>
        public static RectTransform Rays(RectTransform parent, Vector2 at, Color colour, int count, float length)
        {
            var root = Quad(parent, "Rays", new Color(0, 0, 0, 0), Vector2.one);
            root.anchoredPosition = at;
            for (int i = 0; i < count; i++)
            {
                var ray = Quad(root, "Ray", colour, new Vector2(UnityEngine.Random.Range(44f, 82f), length));
                var image = ray.GetComponent<Image>();
                image.sprite = UiGlow.Sprite;
                image.material = UiGlow.Additive;
                ray.pivot = new Vector2(0.5f, 0f);            // rotate about the origin point
                ray.anchoredPosition = Vector2.zero;
                ray.localRotation = Quaternion.Euler(0, 0, (360f / count) * i + UnityEngine.Random.Range(-8f, 8f));
            }
            return root;
        }

        /// <summary>A feathered additive halo that expands behind a revealed card.</summary>
        public static IEnumerator SoftGlow(RectTransform parent, Vector2 at, Color colour,
            float fromSize, float toSize, float dur, float maxAlpha)
        {
            var glow = Quad(parent, "Soft Glow", new Color(colour.r, colour.g, colour.b, 0f),
                Vector2.one * fromSize);
            glow.anchoredPosition = at;
            glow.SetAsFirstSibling();
            var image = glow.GetComponent<Image>();
            image.sprite = UiGlow.Sprite;
            image.material = UiGlow.Additive;

            float t = 0f;
            while (t < dur && glow != null)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                float size = Mathf.Lerp(fromSize, toSize, EaseOut(k));
                glow.sizeDelta = Vector2.one * size;
                SetAlpha(glow, maxAlpha * Mathf.Sin(k * Mathf.PI));
                yield return null;
            }
            if (glow != null) UnityEngine.Object.Destroy(glow.gameObject);
        }

        /// <summary>Small crossed light-stars that twinkle out of a freshly opened seam. These use
        /// feathered additive strokes rather than ParticleSystem quads, so even a compact burst reads
        /// as polished sparkles instead of square debris.</summary>
        public static IEnumerator RipSparkles(RectTransform parent, Vector2 at, Color colour,
            int count, float width, float life)
        {
            if (parent == null) yield break;
            var roots = new List<RectTransform>(count);
            var velocities = new List<Vector2>(count);
            var delays = new List<float>(count);
            var scales = new List<float>(count);

            for (int i = 0; i < count; i++)
            {
                var root = Quad(parent, "Rip Sparkle", Color.clear, Vector2.one);
                root.anchoredPosition = at + new Vector2(UnityEngine.Random.Range(-width, width),
                    UnityEngine.Random.Range(-8f, 10f));
                float scale = UnityEngine.Random.Range(0.72f, 1.35f);
                AddStroke(root, new Vector2(3.2f, 24f) * scale);
                AddStroke(root, new Vector2(15f, 2.4f) * scale);
                roots.Add(root);
                velocities.Add(new Vector2(UnityEngine.Random.Range(-26f, 26f), UnityEngine.Random.Range(22f, 72f)));
                delays.Add(UnityEngine.Random.Range(0f, life * 0.34f));
                scales.Add(scale);
            }

            float t = 0f;
            while (t < life)
            {
                float dt = Time.unscaledDeltaTime;
                t += dt;
                for (int i = 0; i < roots.Count; i++)
                {
                    var star = roots[i];
                    if (star == null) continue;
                    float local = Mathf.Clamp01((t - delays[i]) / Mathf.Max(0.05f, life - delays[i]));
                    float twinkle = local <= 0f ? 0f : Mathf.Sin(local * Mathf.PI);
                    star.anchoredPosition += velocities[i] * dt;
                    star.localRotation *= Quaternion.Euler(0f, 0f, 32f * dt);
                    star.localScale = Vector3.one * scales[i] * (0.55f + twinkle * 0.65f);
                    foreach (Transform child in star)
                    {
                        var image = child.GetComponent<Image>();
                        if (image == null) continue;
                        image.color = new Color(colour.r, colour.g, colour.b, twinkle * colour.a);
                    }
                }
                yield return null;
            }
            foreach (var star in roots) if (star != null) UnityEngine.Object.Destroy(star.gameObject);

            void AddStroke(RectTransform star, Vector2 size)
            {
                var stroke = Quad(star, "Glow Stroke", new Color(colour.r, colour.g, colour.b, 0f), size);
                stroke.anchoredPosition = Vector2.zero;
                var image = stroke.GetComponent<Image>();
                image.sprite = UiGlow.Sprite;
                image.material = UiGlow.Additive;
            }
        }

        public static IEnumerator SpinAndFade(RectTransform rays, float spinDegPerSec, float dur, float startAlpha)
        {
            float t = 0f;
            while (t < dur && rays != null)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                rays.localRotation *= Quaternion.Euler(0, 0, spinDegPerSec * Time.unscaledDeltaTime);
                float a = startAlpha * Mathf.Sin(k * Mathf.PI);
                foreach (Transform child in rays)
                {
                    var img = child.GetComponent<Image>();
                    if (img == null) continue;
                    var c = img.color; c.a = a; img.color = c;
                }
                yield return null;
            }
            if (rays != null) UnityEngine.Object.Destroy(rays.gameObject);
        }

        /// <summary>An expanding ring pulse — the punctuation on a big pull.</summary>
        public static IEnumerator RingPulse(RectTransform parent, Vector2 at, Color colour,
            float fromSize, float toSize, float dur)
        {
            var ring = Quad(parent, "Ring", colour, Vector2.one * fromSize);
            ring.anchoredPosition = at;
            float t = 0f;
            while (t < dur && ring != null)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                float s = Mathf.Lerp(fromSize, toSize, EaseOut(k));
                ring.sizeDelta = new Vector2(s, s);
                SetAlpha(ring, colour.a * (1f - k));
                yield return null;
            }
            if (ring != null) UnityEngine.Object.Destroy(ring.gameObject);
        }

        /// <summary>A vertical column of light behind a card — the "this one matters" cue.</summary>
        public static IEnumerator Pillar(RectTransform parent, Vector2 at, Color colour,
            float width, float height, float dur)
        {
            var pillar = Quad(parent, "Pillar", colour, new Vector2(0f, height));
            pillar.anchoredPosition = at;
            pillar.SetAsFirstSibling();                        // behind the card
            float t = 0f;
            while (t < dur && pillar != null)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                pillar.sizeDelta = new Vector2(width * Mathf.Sin(k * Mathf.PI), height);
                SetAlpha(pillar, colour.a * Mathf.Sin(k * Mathf.PI));
                yield return null;
            }
            if (pillar != null) UnityEngine.Object.Destroy(pillar.gameObject);
        }

        // ---- palettes ---------------------------------------------------------------------------

        public static readonly Color[] GoldFoil =
        {
            new Color32(226, 188, 74, 255), new Color32(255, 226, 150, 255), new Color32(196, 150, 40, 255),
        };
        public static readonly Color[] SilverFoil =
        {
            new Color32(206, 214, 226, 255), new Color32(255, 255, 255, 255), new Color32(160, 175, 195, 255),
        };
        public static Color[] Prismatic(int n)
        {
            var cols = new Color[n];
            for (int i = 0; i < n; i++) cols[i] = Color.HSVToRGB(i / (float)n, 0.62f, 1f);
            return cols;
        }
    }
}
