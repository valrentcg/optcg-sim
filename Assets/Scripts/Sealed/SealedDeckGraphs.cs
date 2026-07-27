// One Piece TCG — Sealed / Pre-Release: the deck-composition graphs.
//
// The graphs themselves live in DeckStatsPanel, shared with the constructed builder, so the colour
// identity bar, cost curve, card types, counters and archetype share are the SAME panel in both
// places rather than a second implementation that drifts. This file is only the adapter: it hands
// DeckStatsPanel the Sealed palette and the Sealed drawing primitives, and translates a card id
// into the handful of facts the panel needs.
//
// The palette is deliberately Sealed's own (SealedUI.Accent and friends) rather than the
// constructed builder's — the panel should look like it belongs to the screen it is drawn on.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sealed
{
    public static class SealedDeckGraphs
    {
        // Same six One Piece colours the constructed builder uses for its chips and dots.
        private static readonly Dictionary<string, Color> Swatches = new Dictionary<string, Color>
        {
            { "Red",    new Color32(214,  68,  68, 255) },
            { "Green",  new Color32( 70, 180, 110, 255) },
            { "Blue",   new Color32( 70, 140, 220, 255) },
            { "Purple", new Color32(160, 110, 210, 255) },
            { "Black",  new Color32( 90, 100, 120, 255) },
            { "Yellow", new Color32(230, 200,  90, 255) },
        };

        private static readonly Color[] ArchPalette =
        {
            new Color32( 79, 195, 224, 255),   // Sealed cyan leads, so the panel reads as this mode's
            new Color32(226, 188,  74, 255),
            new Color32( 96, 200, 130, 255),
            new Color32(160, 110, 210, 255),
            new Color32(214,  68,  68, 255),
            new Color32( 70, 140, 220, 255),
        };
        private static readonly Color ArchOther = new Color32(51, 65, 84, 255);

        private static DeckStatsPanel.Style style;

        public static DeckStatsPanel.Style Style() => style ??= new DeckStatsPanel.Style
        {
            Mono = SealedUI.Legacy,
            Ink = SealedUI.Ink,
            Muted = SealedUI.Muted,
            Accent = SealedUI.Accent,
            Accent2 = SealedUI.Gold,
            Gold = SealedUI.Gold,
            SwatchFor = (key, _) => Swatches.TryGetValue(key, out var c) ? c : new Color(0, 0, 0, 0),
            ArchColor = (i, key) => key == "Other" ? ArchOther : ArchPalette[i % ArchPalette.Length],
            // DeckStatsPanel's signature is (name, parent, colour) with parent as a Transform, while
            // SealedUI.Panel takes (parent, name, colour) with a RectTransform — hence the shuffle.
            Panel = (n, parent, c) => SealedUI.Panel(parent as RectTransform, n, c),
            Text = (n, parent, v, size, c, a, f) =>
            {
                var t = SealedUI.Label(parent as RectTransform, n, v, size, c, a);
                if (f != null) t.font = f;
                return t;
            },
            Stretch = (rt, min, max, oMin, oMax) =>
            {
                rt.anchorMin = min; rt.anchorMax = max;
                rt.offsetMin = oMin; rt.offsetMax = oMax;
            },
            Round = SealedUI.Round,
            RoundCircle = SealedUI.RoundCircle,
            Border = SealedUI.Border,
        };

        /// <summary>The facts DeckStatsPanel needs, read off the shared card database.</summary>
        public static DeckStatsPanel.CardFacts? Facts(string cardId)
        {
            var def = CardData.GetCard(cardId);
            if (def == null) return null;
            return new DeckStatsPanel.CardFacts
            {
                Type = def.Type,
                Cost = def.Cost,
                Counter = def.Counter,
                // CardDef stores colour as one string; a multicolour card reads "Red/Green".
                Colors = string.IsNullOrEmpty(def.Color)
                    ? System.Array.Empty<string>()
                    : def.Color.Split('/').Select(c => c.Trim()).Where(c => c.Length > 0),
                Features = def.Features ?? (IEnumerable<string>)System.Array.Empty<string>(),
            };
        }

        /// <summary>Draw the graphs into a fixed-height child of a VerticalLayoutGroup column.
        ///
        /// DeckStatsPanel positions everything absolutely from the top of its root, so it cannot be a
        /// direct child of the stats column's layout group — it would be given a zero height and
        /// collapse. It gets its own host instead, measured after drawing and pinned to that height so
        /// the column scrolls over the whole panel.</summary>
        public static void RenderInto(RectTransform column,
                                      IEnumerable<KeyValuePair<string, int>> cards)
        {
            var host = SealedUI.Panel(column, "Graphs", new Color(0, 0, 0, 0));
            var le = host.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 1f;               // real height set below, once the panel has drawn

            DeckStatsPanel.Render(host, cards, Facts, Style());
            le.preferredHeight = DeckStatsPanel.MeasuredHeight(host);
        }
    }
}
