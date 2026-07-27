// One Piece TCG — Sealed / Pre-Release: the deck showcase.
//
// The same idea as the constructed builder's showcase (DeckBuilderManager.Showcase): the deck laid
// out as one board, leader first, then every unique card as a fanned stack of its copies. Two things
// are different here, and both follow from Sealed being a BUILD screen rather than a finished-deck
// screen:
//
//   * it lives inside the builder, toggled from the toolbar, so the board is visible WHILE you cut
//     the pool down — that is when seeing the shape of the deck actually changes your decisions;
//   * the arrangement is yours. ARRANGE cycles cost / colour / type / name, so you can shuffle the
//     board into whatever reading order you are thinking in.
//
// It is not read-only either: the same click language as the pool grid works here (left adds a copy
// if the pool still has one, right removes one), because a board you cannot cut from would send you
// back to the pool for every change.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sealed
{
    public sealed partial class SealedDeckBuilderUI
    {
        private enum Arrange { Cost, Color, Type, Name }

        private bool showcase;
        private Arrange arrange = Arrange.Cost;
        private string selectedShowcaseId;
        private RectTransform showcaseContent;

        private const float ShowCardW = 118f;
        private const float ShowCardH = 165f;   // OP card aspect ≈ 0.716
        private const float ShowFanDx = 14f;    // per-copy fan offset

        private void BuildShowcase(RectTransform host)
        {
            var bar = SealedUI.Panel(host, "ShowcaseBar", new Color(0, 0, 0, 0));
            SealedUI.Stretch(bar, new Vector2(0f, 0.945f), new Vector2(1f, 1f));

            var heading = SealedUI.Label(bar, "H", "DECK SHOWCASE", 12, SealedUI.Accent, TextAnchor.MiddleLeft, true);
            SealedUI.Stretch(heading.rectTransform, new Vector2(0.01f, 0f), new Vector2(0.3f, 1f));

            SealedUI.Button(bar, "ARRANGE: " + arrange.ToString().ToUpperInvariant(),
                SealedUI.ChipOff, SealedUI.Ink, () =>
                {
                    var all = (Arrange[])System.Enum.GetValues(typeof(Arrange));
                    arrange = all[(System.Array.IndexOf(all, arrange) + 1) % all.Length];
                    RefreshShowcase();
                }, 11)
                .let(rt => SealedUI.Stretch(rt, new Vector2(0.62f, 0.1f), new Vector2(0.85f, 0.9f)));

            var hint = SealedUI.Label(bar, "Hint", "click +1  ·  right-click −1", 10,
                SealedUI.Muted, TextAnchor.MiddleRight);
            SealedUI.Stretch(hint.rectTransform, new Vector2(0.855f, 0f), new Vector2(0.99f, 1f));

            var area = SealedUI.Panel(host, "ShowcaseArea", new Color(0, 0, 0, 0));
            SealedUI.Stretch(area, new Vector2(0.01f, 0.01f), new Vector2(0.99f, 0.94f));
            showcaseContent = SealedUI.ScrollColumn(area, "Showcase", 0f);
            SealedUI.Fill(showcaseContent.parent as RectTransform);

            RefreshShowcase();
        }

        private void RefreshShowcase()
        {
            if (showcaseContent == null) return;
            for (int i = showcaseContent.childCount - 1; i >= 0; i--)
                Destroy(showcaseContent.GetChild(i).gameObject);

            var uniques = OrderedDeck();
            bool hasLeader = !string.IsNullOrEmpty(pool.LeaderId);
            int cells = uniques.Count + (hasLeader ? 1 : 0);

            if (cells == 0)
            {
                var empty = SealedUI.Label(showcaseContent, "Empty",
                    "No cards in the deck yet — switch to POOL and start adding.", 13,
                    SealedUI.Muted, TextAnchor.MiddleCenter);
                empty.rectTransform.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
                return;
            }

            // The board is absolutely positioned inside one fixed-height child of the scroll column,
            // so the fanned stacks can overlap freely — a layout group would space them apart and
            // undo the fan.
            const int COLS = 6;
            // A rect that has not been laid out yet reads 0, which would collapse the board to one
            // column, so fall back to the width this area gets at the 1920 reference: the pool span
            // (0.145 → 0.795) inset by the area's own 1% padding.
            float boardW = showcaseContent.rect.width > 200f
                ? showcaseContent.rect.width
                : 1920f * (0.795f - 0.145f) * 0.98f;
            float cellW = boardW / COLS;
            float cellH = ShowCardH + 44f;
            int rows = Mathf.CeilToInt(cells / (float)COLS);

            var board = SealedUI.Panel(showcaseContent, "Board", new Color(0, 0, 0, 0));
            board.gameObject.AddComponent<LayoutElement>().preferredHeight = rows * cellH + 12f;

            int cell = 0;
            if (hasLeader) { BuildShowcaseStack(board, pool.LeaderId, 1, true, cell++, cellW, cellH); }
            foreach (var u in uniques) BuildShowcaseStack(board, u.Key, u.Value, false, cell++, cellW, cellH);
        }

        /// <summary>The deck's unique cards in the player's chosen reading order.</summary>
        private List<KeyValuePair<string, int>> OrderedDeck()
        {
            int TypeRank(CardDef d)
            {
                switch ((d?.Type ?? "").ToLowerInvariant())
                {
                    case "event": return 1;
                    case "stage": return 2;
                    default: return 0;
                }
            }
            // Fixed colour order so the board does not reshuffle between redraws.
            string[] colorOrder = { "Red", "Green", "Blue", "Purple", "Black", "Yellow" };
            int ColorRank(CardDef d)
            {
                string first = (d?.Color ?? "").Split('/')[0].Trim();
                int i = System.Array.IndexOf(colorOrder, first);
                return i < 0 ? colorOrder.Length : i;
            }

            var entries = pool.Deck.Where(kv => kv.Value > 0).ToList();
            switch (arrange)
            {
                case Arrange.Color:
                    return entries
                        .OrderBy(kv => ColorRank(CardData.GetCard(kv.Key)))
                        .ThenBy(kv => CardData.GetCard(kv.Key)?.Cost ?? 0)
                        .ThenBy(kv => CardData.GetCard(kv.Key)?.Name ?? kv.Key).ToList();
                case Arrange.Type:
                    return entries
                        .OrderBy(kv => TypeRank(CardData.GetCard(kv.Key)))
                        .ThenBy(kv => CardData.GetCard(kv.Key)?.Cost ?? 0)
                        .ThenBy(kv => CardData.GetCard(kv.Key)?.Name ?? kv.Key).ToList();
                case Arrange.Name:
                    return entries
                        .OrderBy(kv => CardData.GetCard(kv.Key)?.Name ?? kv.Key).ToList();
                default:
                    return entries
                        .OrderBy(kv => CardData.GetCard(kv.Key)?.Cost ?? 0)
                        .ThenBy(kv => TypeRank(CardData.GetCard(kv.Key)))
                        .ThenBy(kv => CardData.GetCard(kv.Key)?.Name ?? kv.Key).ToList();
            }
        }

        private void BuildShowcaseStack(RectTransform board, string cardId, int count, bool isLeader,
                                        int cellIndex, float cellW, float cellH)
        {
            int col = cellIndex % 6, row = cellIndex / 6;
            bool selected = cardId == selectedShowcaseId;

            float stackW = ShowCardW + (count - 1) * ShowFanDx;
            var stack = SealedUI.Panel(board, cardId + " Stack", new Color(0, 0, 0, 0), raycast: true);
            stack.anchorMin = stack.anchorMax = new Vector2(0f, 1f);
            stack.pivot = new Vector2(0.5f, 0.5f);
            stack.sizeDelta = new Vector2(stackW, ShowCardH);
            stack.anchoredPosition = new Vector2(col * cellW + cellW / 2f, -(row * cellH + cellH / 2f));
            if (selected) stack.SetAsLastSibling();

            for (int i = 0; i < count; i++)
            {
                var face = BuildShowcaseFace(stack, cardId, i * ShowFanDx);
                if (i != count - 1) continue;                        // chrome only on the top copy
                if (isLeader && !selected) SealedUI.Border(face, new Color(SealedUI.Gold.r, SealedUI.Gold.g, SealedUI.Gold.b, 0.9f), 2f);
                if (selected) SealedUI.Border(face, SealedUI.Accent, 2.5f);
            }

            if (count > 1)
            {
                var pill = SealedUI.Panel(stack, "Count", SealedUI.Accent);
                pill.anchorMin = pill.anchorMax = new Vector2(1f, 1f);
                pill.pivot = new Vector2(1f, 0.5f);
                pill.sizeDelta = new Vector2(32f, 17f);
                pill.anchoredPosition = new Vector2(4f, 5f);
                SealedUI.Round(pill);
                var pt = SealedUI.Label(pill, "t", "×" + count, 11, SealedUI.BadgeInk, TextAnchor.MiddleCenter, true);
                SealedUI.Fill(pt.rectTransform);
            }

            if (isLeader)
            {
                var lp = SealedUI.Panel(stack, "Leader", SealedUI.Gold);
                lp.anchorMin = lp.anchorMax = new Vector2(0f, 1f);
                lp.pivot = new Vector2(0f, 0.5f);
                lp.sizeDelta = new Vector2(50f, 16f);
                lp.anchoredPosition = new Vector2(-4f, 5f);
                SealedUI.Round(lp);
                var lt = SealedUI.Label(lp, "t", "LEADER", 8, SealedUI.BadgeInk, TextAnchor.MiddleCenter, true);
                SealedUI.Fill(lt.rectTransform);
            }
            else
            {
                // How many of this card are still sitting in the pool, unused. The equivalent of the
                // pool grid's corner badges — without it you cannot tell a 4-of you have maxed out
                // from one you still have spares of.
                int spare = pool.Remaining(cardId);
                if (spare > 0)
                {
                    var sp = SealedUI.Label(stack, "Spare", "+" + spare + " in pool", 9,
                        SealedUI.Muted, TextAnchor.MiddleCenter);
                    var srt = sp.rectTransform;
                    srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(1f, 0f);
                    srt.pivot = new Vector2(0.5f, 1f);
                    srt.sizeDelta = new Vector2(0f, 14f);
                    srt.anchoredPosition = new Vector2(0f, -3f);
                }
            }

            string id = cardId;
            var click = stack.gameObject.AddComponent<SealedUI.ClickRouter>();
            click.OnLeft = () =>
            {
                selectedShowcaseId = id;
                if (!isLeader) pool.Add(id);       // no-op when the pool has none spare
                RefreshShowcase(); RefreshStats();
            };
            click.OnRight = () =>
            {
                if (isLeader) return;
                pool.Remove(id);
                RefreshShowcase(); RefreshStats();
            };

            var hover = stack.gameObject.AddComponent<SealedUI.HoverRouter>();
            hover.OnEnter = () => ShowPreview(id);
            hover.OnExit = HidePreview;
        }

        private RectTransform BuildShowcaseFace(RectTransform stack, string cardId, float x)
        {
            var card = SealedUI.Panel(stack, cardId + "@" + x, new Color32(11, 10, 22, 255));
            card.anchorMin = card.anchorMax = new Vector2(0f, 0.5f);
            card.pivot = new Vector2(0f, 0.5f);
            card.sizeDelta = new Vector2(ShowCardW, ShowCardH);
            card.anchoredPosition = new Vector2(x, 0f);
            SealedUI.Round(card);
            // Mask the art to the rounded backing rather than letting a square scan sit on top of it,
            // which is what makes the fan read as a stack of cards instead of a stack of rectangles.
            card.gameObject.AddComponent<Mask>().showMaskGraphic = true;

            var sprite = GetSprite(cardId);
            var art = SealedUI.Panel(card, "Art", sprite != null ? Color.white : new Color32(24, 36, 52, 255));
            SealedUI.Fill(art);
            var img = art.GetComponent<Image>();
            if (sprite != null) { img.sprite = sprite; img.preserveAspect = true; }
            else
            {
                var def = CardData.GetCard(cardId);
                var t = SealedUI.Label(art, "N", $"{def?.Name ?? cardId}\n{def?.Cost}c", 9,
                    SealedUI.Ink, TextAnchor.MiddleCenter);
                SealedUI.Stretch(t.rectTransform, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f));
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            return card;
        }
    }
}
