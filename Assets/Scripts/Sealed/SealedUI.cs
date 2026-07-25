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
