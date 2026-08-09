// One Piece TCG — Sealed / Pre-Release: shared uGUI primitives for the mode's screens.
//
// Sealed builds its own UI rather than borrowing GameManager's helpers, which are private to that
// class. Keeping the primitives here means the pack-opening sequence, the deck builder and the event
// screens all look like one mode instead of three, and the palette lives in exactly one place.

using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OnePieceTcg.Sealed
{
    public static class SealedUI
    {
        public static readonly Color Ink = new Color32(238, 242, 247, 255);
        public static readonly Color Muted = new Color32(159, 171, 190, 255);
        public static readonly Color Accent = new Color32(79, 195, 224, 255);
        public static readonly Color BadgeInk = new Color32(6, 32, 44, 255);
        public static readonly Color PanelBg = new Color32(16, 30, 46, 250);
        public static readonly Color PanelBg2 = new Color32(20, 34, 50, 235);
        public static readonly Color ChipOff = new Color32(34, 48, 66, 235);
        public static readonly Color Good = new Color32(96, 200, 130, 255);
        public static readonly Color Bad = new Color32(232, 120, 120, 255);
        public static readonly Color Gold = new Color32(226, 188, 74, 255);

        private static Font legacy;
        private static Sprite standardBackground;
        private static Shader cardGlowShader;
        public static Font Legacy =>
            legacy != null ? legacy : (legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static RectTransform Panel(RectTransform parent, string name, Color colour, bool raycast = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = colour;
            img.raycastTarget = raycast;
            return rt;
        }

        public static Text Label(RectTransform parent, string name, string value, int size, Color colour,
            TextAnchor anchor = TextAnchor.MiddleLeft, bool bold = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = value;
            t.fontSize = size;
            t.color = colour;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.font = Legacy;
            if (bold) t.fontStyle = FontStyle.Bold;
            return t;
        }

        public static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        public static void Fill(RectTransform rt) => Stretch(rt, Vector2.zero, Vector2.one);

        /// <summary>The deep blue play-mat language used by the rest of the client.</summary>
        public static void AddStandardBackground(RectTransform parent)
        {
            var bg = Panel(parent, "Blue Background", Color.white);
            Fill(bg);
            bg.GetComponent<Image>().sprite = StandardBackgroundSprite();
            bg.SetAsFirstSibling();
            AddAmbientGlow(parent, "Upper Cyan Bloom", new Vector2(0.14f, 0.28f),
                new Vector2(0.88f, 1.12f), new Color(0.08f, 0.48f, 0.72f, 0.11f));
            AddAmbientGlow(parent, "Lower Blue Bloom", new Vector2(0.34f, -0.22f),
                new Vector2(1.08f, 0.54f), new Color(0.04f, 0.27f, 0.55f, 0.08f));
        }

        private static Sprite StandardBackgroundSprite()
        {
            if (standardBackground != null) return standardBackground;
            const int W = 8, H = 256;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[W * H];
            Color bottom = new Color32(13, 38, 50, 255), top = new Color32(13, 33, 60, 255);
            for (int y = 0; y < H; y++)
            {
                Color c = Color.Lerp(bottom, top, y / (H - 1f));
                for (int x = 0; x < W; x++) px[y * W + x] = c;
            }
            tex.SetPixels32(px); tex.Apply(false, true);
            standardBackground = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
            return standardBackground;
        }

        private static void AddAmbientGlow(RectTransform parent, string name, Vector2 min, Vector2 max, Color colour)
        {
            var glow = Panel(parent, name, colour);
            Stretch(glow, min, max);
            var image = glow.GetComponent<Image>();
            image.sprite = UiGlow.Sprite;
            image.material = UiGlow.Additive;
            glow.SetAsFirstSibling();
            var background = parent.Find("Blue Background") as RectTransform;
            if (background != null) background.SetAsFirstSibling();
        }

        /// <summary>Add the same animated preview rim used by cards in a match.</summary>
        public static RectTransform AddCardPreviewGlow(RectTransform holder)
        {
            if (holder == null) return null;
            if (cardGlowShader == null) cardGlowShader = Shader.Find("UI/CardHoverGlow");
            if (cardGlowShader == null) { Border(holder, Gold, 2.5f); return null; }
            const float expand = 0.28f;
            var go = new GameObject("Preview Glow", typeof(RectTransform), typeof(RawImage));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(holder, false);
            rt.anchorMin = new Vector2(-expand, -expand); rt.anchorMax = new Vector2(1f + expand, 1f + expand);
            rt.offsetMin = rt.offsetMax = Vector2.zero; rt.SetAsFirstSibling();
            var image = go.GetComponent<RawImage>();
            image.texture = Texture2D.whiteTexture; image.raycastTarget = false;
            var material = new Material(cardGlowShader);
            material.SetColor("_GlowColor", new Color(1.00f, 0.59f, 0.10f, 1f) * 1.28f);
            material.SetColor("_CoreColor", new Color(1.00f, 0.80f, 0.38f, 1f) * 1.12f);
            material.SetColor("_OuterColor", new Color(0.82f, 0.29f, 0.04f, 1f) * 1.05f);
            material.SetFloat("_Speed", 0.55f); material.SetFloat("_NoiseScale", 3.0f);
            material.SetFloat("_Pulse", 0.22f);
            image.material = material;
            go.AddComponent<CardPreviewGlowDriver>().Init(image, expand);
            return rt;
        }

        private sealed class CardPreviewGlowDriver : MonoBehaviour
        {
            private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
            private Material material; private RectTransform rect; private float expand, intensity;
            public void Init(RawImage image, float expandFraction)
            {
                rect = image.rectTransform; material = image.material; expand = expandFraction;
                if (material != null) material.SetFloat(IntensityId, 0f);
            }
            private void Update()
            {
                if (material == null || rect == null) return;
                Vector2 glow = rect.rect.size;
                if (glow.x > 1f && glow.y > 1f)
                {
                    Vector2 card = glow / (1f + 2f * expand); float edge = Mathf.Min(card.x, card.y);
                    material.SetVector("_GlowSize", new Vector4(glow.x, glow.y, 0f, 0f));
                    material.SetVector("_CardSize", new Vector4(card.x, card.y, 0f, 0f));
                    material.SetFloat("_CornerPx", edge * 0.06f); material.SetFloat("_BleedPx", edge * 0.05f);
                    material.SetFloat("_GlowWidthPx", edge * 0.075f);
                    material.SetFloat("_CoreWidthPx", Mathf.Max(1.5f, edge * 0.02f));
                    material.SetFloat("_WispPx", edge * 0.06f);
                }
                intensity = Mathf.MoveTowards(intensity, 1f, 6.5f * Time.unscaledDeltaTime);
                material.SetFloat(IntensityId, intensity);
            }
            private void OnDestroy() { if (material != null) Destroy(material); }
        }

        public static RectTransform Button(RectTransform parent, string label, Color bg, Color fg, Action onClick,
            int size = 12, bool bold = true)
        {
            var rt = Panel(parent, label + " Btn", bg, raycast: true);
            var t = Label(rt, "Label", label, size, fg, TextAnchor.MiddleCenter, bold);
            Fill(t.rectTransform);
            var b = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            b.onClick.AddListener(() => onClick?.Invoke());
            return rt;
        }

        /// <summary>A filter chip — the on/off pill the builder's filters are made of.</summary>
        public static RectTransform Chip(RectTransform parent, string label, bool on, Action onClick)
        {
            var rt = Button(parent, label, on ? Accent : ChipOff, on ? BadgeInk : Ink, onClick, 11, on);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 22f;
            le.preferredWidth = Mathf.Max(38f, 9f * label.Length + 16f);
            return rt;
        }

        /// <summary>Small count pill in a card thumbnail's corner (pool count / deck count).</summary>
        public static void CornerBadge(RectTransform parent, string text, Vector2 anchor, Color bg, Color fg)
        {
            var badge = Panel(parent, "Badge", bg);
            badge.anchorMin = badge.anchorMax = anchor;
            badge.pivot = new Vector2(anchor.x, anchor.y);
            badge.sizeDelta = new Vector2(20f, 16f);
            badge.anchoredPosition = new Vector2(anchor.x > 0.5f ? -2f : 2f, anchor.y > 0.5f ? -2f : 2f);
            var t = Label(badge, "N", text, 10, fg, TextAnchor.MiddleCenter, true);
            Fill(t.rectTransform);
        }

        /// <summary>Vertical list container with a scroll rect — used by the filter rail and stats panel.</summary>
        public static RectTransform ScrollColumn(RectTransform parent, string name, float spacing = 4f)
        {
            var viewport = Panel(parent, name + " Viewport", new Color(0, 0, 0, 0), raycast: true);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = new GameObject(name + " Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0, 0);
            content.offsetMax = new Vector2(0, 0);

            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = spacing;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 22f;
            return content;
        }

        /// <summary>Wrapping row used for chip rails.</summary>
        public static RectTransform ChipRow(RectTransform parent, string name)
        {
            var row = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(parent, false);
            var grid = row.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(76f, 22f);
            grid.spacing = new Vector2(4f, 4f);
            grid.childAlignment = TextAnchor.UpperLeft;
            var fitter = row.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return row;
        }

        // ---- Rounded corners -------------------------------------------------------------------
        // The shared DeckStatsPanel draws through host-supplied Round/RoundCircle/Border hooks so it
        // can render inside either builder. Sealed had no rounded-rect primitive of its own, so these
        // generate the same kind of 9-sliced SDF sprite the constructed builder uses — without that,
        // every bar and swatch in the composition graphs would come out as a hard rectangle.

        private static Sprite roundSprite, circleSprite;
        private static readonly System.Collections.Generic.Dictionary<int, Sprite> borderSprites
            = new System.Collections.Generic.Dictionary<int, Sprite>();

        public static Sprite RoundSprite() => roundSprite != null ? roundSprite : (roundSprite = MakeRounded(24, 5f));

        public static Sprite CircleSprite()
        {
            if (circleSprite != null) return circleSprite;
            const int S = 48; const float R = S * 0.5f;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - R) * (x + 0.5f - R) + (y + 0.5f - R) * (y + 0.5f - R));
                    float a = Mathf.Clamp01(R - d);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            circleSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return circleSprite;
        }

        private static Sprite MakeRounded(int S, float r)
        {
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float cx = Mathf.Min(x + 0.5f, S - x - 0.5f);
                    float cy = Mathf.Min(y + 0.5f, S - y - 0.5f);
                    float dx = Mathf.Max(0f, r - cx), dy = Mathf.Max(0f, r - cy);
                    float a = Mathf.Clamp01(r + 0.75f - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        }

        public static void Round(RectTransform rt)
        {
            var img = rt != null ? rt.GetComponent<Image>() : null;
            if (img == null) return;
            img.sprite = RoundSprite(); img.type = Image.Type.Sliced;
        }

        public static void RoundCircle(RectTransform rt)
        {
            var img = rt != null ? rt.GetComponent<Image>() : null;
            if (img == null) return;
            img.sprite = CircleSprite(); img.type = Image.Type.Simple;
        }

        public static void Border(RectTransform parent, Color colour, float thickness)
        {
            if (parent == null || colour.a <= 0f || thickness <= 0f) return;
            int key = Mathf.Clamp(Mathf.RoundToInt(thickness * 10f), 1, 80);
            if (!borderSprites.TryGetValue(key, out var sprite))
            {
                const int W = 64, H = 64; const float R = 6.5f;
                float th = Mathf.Clamp(key / 10f, 0.75f, 8f);
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
                var px = new Color32[W * H];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float outer = RoundRect(x + 0.5f, y + 0.5f, W, H, R);
                        float inner = RoundRect(x + 0.5f - th, y + 0.5f - th, W - th * 2f, H - th * 2f, Mathf.Max(0f, R - th));
                        float a = Mathf.Clamp01(outer * (1f - inner));
                        px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                tex.SetPixels32(px); tex.Apply(false, true);
                sprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(R, R, R, R));
                borderSprites[key] = sprite;
            }
            var rt = Panel(parent, "Border", colour);
            rt.GetComponent<Image>().sprite = sprite;
            rt.GetComponent<Image>().type = Image.Type.Sliced;
            Fill(rt);
        }

        private static float RoundRect(float x, float y, float w, float h, float r)
        {
            float cx = Mathf.Min(x, w - x), cy = Mathf.Min(y, h - y);
            float dx = Mathf.Max(0f, r - cx), dy = Mathf.Max(0f, r - cy);
            return Mathf.Clamp01(r + 0.75f - Mathf.Sqrt(dx * dx + dy * dy));
        }

        /// <summary>Routes left/right clicks separately — the builder needs right-click to remove.</summary>
        public sealed class ClickRouter : MonoBehaviour, IPointerClickHandler
        {
            public Action OnLeft, OnRight, OnDoubleLeft;
            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Right) { OnRight?.Invoke(); return; }
                if (e.button != PointerEventData.InputButton.Left) return;
                if (e.clickCount >= 2) OnDoubleLeft?.Invoke(); else OnLeft?.Invoke();
            }
        }

        /// <summary>Hover callbacks, for the card zoom preview.</summary>
        public sealed class HoverRouter : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Action OnEnter, OnExit;
            public void OnPointerEnter(PointerEventData e) => OnEnter?.Invoke();
            public void OnPointerExit(PointerEventData e) => OnExit?.Invoke();
        }
    }
}
