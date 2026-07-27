// Deck-composition graphs (colour identity, cost curve, card types, counters, archetypes),
// extracted from DeckBuilderManager so the Sealed / Pre-Release builder draws the SAME panel
// instead of growing a second copy that drifts.
//
// The drawing primitives are injected rather than reimplemented. That is deliberate: the
// constructed builder passes its own Panel/Text/Stretch/Round, so its stats panel renders
// byte-identically to before this was pulled out. A second caller supplies equivalents backed
// by whatever UI kit it already uses.
//
// Card facts are injected too (Lookup), because the two builders read different card models -
// DeckBuilderManager has CardRec, Sealed has its own records - and neither should have to know
// about the other.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public static class DeckStatsPanel
{
    /// <summary>The only things this panel needs to know about a card.</summary>
    public struct CardFacts
    {
        public string Type;                 // "character" | "event" | "stage"
        public int Cost;
        public int Counter;                 // 0 / 1000 / 2000
        public IEnumerable<string> Colors;
        public IEnumerable<string> Features;
    }

    /// <summary>Host-supplied drawing kit. Every call the panel makes goes through here.</summary>
    public sealed class Style
    {
        public Font Mono;
        public Color Ink, Muted, Accent, Accent2, Gold;
        public Func<string, string, Color> SwatchFor;              // colour name -> swatch
        public Func<int, string, Color> ArchColor;                 // index, key -> segment colour

        public Func<string, Transform, Color, RectTransform> Panel;
        public Func<string, Transform, string, int, Color, TextAnchor, Font, Text> Text;
        public Action<RectTransform, Vector2, Vector2, Vector2, Vector2> Stretch;
        public Action<RectTransform> Round;
        public Action<RectTransform> RoundCircle;
        public Action<RectTransform, Color, float> Border;

        internal Color Swatch(string key, Color fallback)
        {
            if (SwatchFor == null) return fallback;
            var c = SwatchFor(key, null);
            return c.a <= 0f ? fallback : c;
        }
    }

    /// <summary>Clear <paramref name="root"/> and redraw the whole panel for this decklist.</summary>
    public static void Render(RectTransform root,
                              IEnumerable<KeyValuePair<string, int>> cards,
                              Func<string, CardFacts?> lookup,
                              Style s)
    {
        if (root == null || s == null || lookup == null) return;
        for (int i = root.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(root.GetChild(i).gameObject);

        int chars = 0, events = 0, stages = 0;
        int[] cost = new int[11];
        int[] counter = new int[3];
        var colorCounts = new Dictionary<string, int>();
        var archCounts = new Dictionary<string, int>();

        foreach (var e in cards)
        {
            var facts = lookup(e.Key);
            if (facts == null) continue;
            var f = facts.Value;

            string t = (f.Type ?? "").ToLowerInvariant();
            if (t == "event") events += e.Value;
            else if (t == "stage") stages += e.Value;
            else chars += e.Value;

            cost[Mathf.Clamp(f.Cost, 0, 10)] += e.Value;
            int ci = f.Counter >= 2000 ? 2 : (f.Counter >= 1000 ? 1 : 0);
            counter[ci] += e.Value;

            if (f.Colors != null)
                foreach (var c in f.Colors)
                {
                    if (string.IsNullOrEmpty(c)) continue;
                    colorCounts.TryGetValue(c, out int cv); colorCounts[c] = cv + e.Value;
                }
            if (f.Features != null)
                foreach (var a in f.Features)
                {
                    if (string.IsNullOrEmpty(a)) continue;
                    archCounts.TryGetValue(a, out int av); archCounts[a] = av + e.Value;
                }
        }

        // average cost (10+ counts as 10)
        int costTotal = 0; float weighted = 0f;
        for (int i = 0; i < cost.Length; i++) { costTotal += cost[i]; weighted += i * cost[i]; }
        float avgCost = costTotal > 0 ? weighted / costTotal : 0f;

        // top 5 archetypes + "Other" (features overlap, so these DON'T sum to the deck size)
        var archSorted = archCounts.OrderByDescending(k => k.Value).ToList();
        var archTop = new List<KeyValuePair<string, int>>();
        int shown = Mathf.Min(5, archSorted.Count);
        for (int i = 0; i < shown; i++) archTop.Add(archSorted[i]);
        int other = 0; for (int i = shown; i < archSorted.Count; i++) other += archSorted[i].Value;
        if (other > 0) archTop.Add(new KeyValuePair<string, int>("Other", other));

        var colorList = colorCounts.OrderByDescending(k => k.Value).ToList();

        float y = -4f;

        if (colorList.Count > 0) y = ColorIdentity(root, s, colorList, y);

        y = SectionLabel(root, s, "COST CURVE", y, "avg " + avgCost.ToString("0.0"));
        y = CostCurve(root, s, cost, avgCost, y);
        y -= 10f;

        y = SectionLabel(root, s, "CARD TYPES", y);
        int typeMax = Mathf.Max(1, Mathf.Max(chars, Mathf.Max(events, stages)));
        y = Bar(root, s, "Character", chars, typeMax, y);
        y = Bar(root, s, "Event", events, typeMax, y);
        y = Bar(root, s, "Stage", stages, typeMax, y);
        y -= 10f;

        y = SectionLabel(root, s, "COUNTERS", y);
        int cMax = Mathf.Max(1, Mathf.Max(counter[0], Mathf.Max(counter[1], counter[2])));
        y = Bar(root, s, "None", counter[0], cMax, y);
        y = Bar(root, s, "+1000", counter[1], cMax, y);
        y = Bar(root, s, "+2000", counter[2], cMax, y);
        y -= 10f;

        if (archTop.Count > 0)
        {
            y = SectionLabel(root, s, "ARCHETYPES", y, "by card count");
            y = ArchetypeShare(root, s, archTop, y);
        }
    }

    /// <summary>Total height the panel just drew — for hosts that need to size a scroll area.</summary>
    public static float MeasuredHeight(RectTransform root)
    {
        float lowest = 0f;
        for (int i = 0; i < root.childCount; i++)
        {
            var rt = root.GetChild(i) as RectTransform;
            if (rt == null) continue;
            float bottom = rt.anchoredPosition.y - rt.sizeDelta.y;
            if (bottom < lowest) lowest = bottom;
        }
        return -lowest + 12f;
    }

    // ---- pieces (verbatim geometry from the original) ---------------------------------------

    private static float SectionLabel(RectTransform root, Style s, string text, float y, string right = null)
    {
        var t = s.Text("S_" + text, root, text, 9, s.Muted, TextAnchor.LowerLeft, s.Mono);
        t.fontStyle = FontStyle.Bold;
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, 16f);
        rt.anchoredPosition = new Vector2(0f, y);

        if (!string.IsNullOrEmpty(right))
        {
            var rv = s.Text("SR_" + text, root, right, 9, s.Gold, TextAnchor.LowerRight, s.Mono);
            rv.fontStyle = FontStyle.Bold;
            var rr = rv.rectTransform;
            rr.anchorMin = new Vector2(0f, 1f); rr.anchorMax = new Vector2(1f, 1f);
            rr.pivot = new Vector2(0.5f, 1f);
            rr.sizeDelta = new Vector2(0f, 16f);
            rr.anchoredPosition = new Vector2(0f, y);
        }
        return y - 18f;
    }

    private static float Bar(RectTransform root, Style s, string label, int value, int max, float y)
    {
        const float rowH = 18f, labelW = 64f, valueW = 28f;

        var row = s.Panel("StatRow", root, new Color(0, 0, 0, 0));
        row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.sizeDelta = new Vector2(0f, rowH);
        row.anchoredPosition = new Vector2(0f, y);
        NoRaycast(row);

        var lbl = s.Text("L", row, label, 9, s.Ink, TextAnchor.MiddleLeft, s.Mono);
        s.Stretch(lbl.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(labelW, 0f));

        var track = s.Panel("Track", row, new Color(1f, 1f, 1f, 0.05f));
        s.Stretch(track, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(labelW, 3f), new Vector2(-valueW, -3f));
        s.Round(track);
        NoRaycast(track);

        float frac = max > 0 ? Mathf.Clamp01(value / (float)max) : 0f;
        var fill = s.Panel("Fill", track, value > 0 ? s.Accent : new Color(0, 0, 0, 0));
        s.Stretch(fill, new Vector2(0f, 0f), new Vector2(frac, 1f), Vector2.zero, Vector2.zero);
        s.Round(fill);
        NoRaycast(fill);

        var val = s.Text("V", row, value.ToString(), 9, value > 0 ? s.Accent2 : s.Muted, TextAnchor.MiddleRight, s.Mono);
        val.fontStyle = FontStyle.Bold;
        s.Stretch(val.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-valueW + 2f, 0f), Vector2.zero);

        return y - (rowH + 4f);
    }

    private static float CostCurve(RectTransform root, Style s, int[] cost, float avg, float y)
    {
        const float barAreaH = 70f, capH = 12f, labelH = 14f;
        int n = cost.Length;
        int max = 1;
        for (int i = 0; i < n; i++) max = Mathf.Max(max, cost[i]);

        var area = s.Panel("CostArea", root, new Color(0, 0, 0, 0));
        area.anchorMin = new Vector2(0f, 1f); area.anchorMax = new Vector2(1f, 1f);
        area.pivot = new Vector2(0.5f, 1f);
        area.sizeDelta = new Vector2(0f, barAreaH + capH + labelH);
        area.anchoredPosition = new Vector2(0f, y);
        NoRaycast(area);

        for (int i = 0; i < n; i++)
        {
            float x0 = i / (float)n, x1 = (i + 1) / (float)n;
            float frac = Mathf.Clamp01(cost[i] / (float)max);

            var cap = s.Text("cap" + i, area, cost[i] > 0 ? cost[i].ToString() : "", 8,
                cost[i] > 0 ? s.Accent2 : s.Muted, TextAnchor.LowerCenter, s.Mono);
            var cr = cap.rectTransform;
            cr.anchorMin = new Vector2(x0, 1f); cr.anchorMax = new Vector2(x1, 1f);
            cr.offsetMin = new Vector2(1f, -capH); cr.offsetMax = new Vector2(-1f, 0f);

            var bar = s.Panel("bar" + i, area, cost[i] > 0 ? s.Accent : new Color(1f, 1f, 1f, 0.05f));
            bar.anchorMin = new Vector2(x0, 0f); bar.anchorMax = new Vector2(x1, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            float h = Mathf.Max(2f, frac * barAreaH);
            bar.offsetMin = new Vector2(2f, labelH);
            bar.offsetMax = new Vector2(-2f, labelH + h);
            s.Round(bar);
            NoRaycast(bar);

            var lab = s.Text("lab" + i, area, i == 10 ? "10+" : i.ToString(), 8, s.Muted, TextAnchor.LowerCenter, s.Mono);
            var lr = lab.rectTransform;
            lr.anchorMin = new Vector2(x0, 0f); lr.anchorMax = new Vector2(x1, 0f);
            lr.offsetMin = new Vector2(0f, 0f); lr.offsetMax = new Vector2(0f, labelH);
        }

        float mfrac = Mathf.Clamp01(avg / (n - 1));
        var mark = s.Panel("avg", area, new Color(s.Gold.r, s.Gold.g, s.Gold.b, 0.7f));
        mark.anchorMin = new Vector2(mfrac, 0f); mark.anchorMax = new Vector2(mfrac, 0f);
        mark.pivot = new Vector2(0.5f, 0f);
        mark.sizeDelta = new Vector2(1.6f, barAreaH);
        mark.anchoredPosition = new Vector2(0f, labelH);
        NoRaycast(mark);

        return y - (barAreaH + capH + labelH + 4f);
    }

    private static float ColorIdentity(RectTransform root, Style s, List<KeyValuePair<string, int>> colors, float y)
    {
        const float h = 30f;
        var row = s.Panel("ColorId", root, new Color(0, 0, 0, 0));
        row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.sizeDelta = new Vector2(0f, h);
        row.anchoredPosition = new Vector2(0f, y);
        NoRaycast(row);

        int n = Mathf.Max(1, colors.Count);
        for (int i = 0; i < colors.Count; i++)
        {
            var kv = colors[i];
            Color c = s.Swatch(kv.Key, s.Muted);
            float x0 = i / (float)n, x1 = (i + 1) / (float)n;

            var tile = s.Panel("c" + i, row, new Color(c.r, c.g, c.b, 0.12f));
            tile.anchorMin = new Vector2(x0, 0f); tile.anchorMax = new Vector2(x1, 1f);
            tile.offsetMin = new Vector2(i == 0 ? 0f : 3f, 0f);
            tile.offsetMax = new Vector2(-3f, 0f);
            s.Round(tile);
            s.Border?.Invoke(tile, new Color(c.r, c.g, c.b, 0.4f), 1f);

            var dot = s.Panel("dot", tile, c);
            dot.anchorMin = dot.anchorMax = new Vector2(0f, 0.5f);
            dot.pivot = new Vector2(0f, 0.5f);
            dot.sizeDelta = new Vector2(9f, 9f);
            dot.anchoredPosition = new Vector2(9f, 0f);
            s.RoundCircle?.Invoke(dot);

            var lbl = s.Text("l", tile, kv.Key.ToUpperInvariant(), 11, s.Ink, TextAnchor.MiddleLeft, null);
            s.Stretch(lbl.rectTransform, Vector2.zero, Vector2.one, new Vector2(24f, 0f), new Vector2(-26f, 0f));

            var val = s.Text("v", tile, kv.Value.ToString(), 11, s.Ink, TextAnchor.MiddleRight, s.Mono);
            val.fontStyle = FontStyle.Bold;
            s.Stretch(val.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-8f, 0f));
        }
        return y - (h + 12f);
    }

    private static float ArchetypeShare(RectTransform root, Style s, List<KeyValuePair<string, int>> items, float y)
    {
        const float barH = 14f, rowH = 18f;

        float total = 0f; foreach (var it in items) total += it.Value; if (total <= 0f) total = 1f;

        var bar = s.Panel("ArchBar", root, new Color(1f, 1f, 1f, 0.05f));
        bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, barH);
        bar.anchoredPosition = new Vector2(0f, y);
        s.Round(bar);
        NoRaycast(bar);

        float cum = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            float w = items[i].Value / total;
            var seg = s.Panel("s" + i, bar, s.ArchColor(i, items[i].Key));
            seg.anchorMin = new Vector2(cum, 0f); seg.anchorMax = new Vector2(cum + w, 1f);
            seg.offsetMin = Vector2.zero; seg.offsetMax = Vector2.zero;
            NoRaycast(seg);
            cum += w;
        }
        y -= (barH + 11f);

        int rows = Mathf.CeilToInt(items.Count / 2f);
        var leg = s.Panel("ArchLeg", root, new Color(0, 0, 0, 0));
        leg.anchorMin = new Vector2(0f, 1f); leg.anchorMax = new Vector2(1f, 1f);
        leg.pivot = new Vector2(0.5f, 1f);
        leg.sizeDelta = new Vector2(0f, rows * rowH);
        leg.anchoredPosition = new Vector2(0f, y);
        NoRaycast(leg);

        for (int i = 0; i < items.Count; i++)
        {
            int col = i % 2, r = i / 2;
            float x0 = col * 0.5f, x1 = x0 + 0.5f;
            float pad = col == 1 ? 8f : 0f;

            var cell = s.Panel("cell" + i, leg, new Color(0, 0, 0, 0));
            cell.anchorMin = new Vector2(x0, 1f); cell.anchorMax = new Vector2(x1, 1f);
            cell.pivot = new Vector2(0.5f, 1f);
            cell.sizeDelta = new Vector2(0f, rowH);
            cell.anchoredPosition = new Vector2(0f, -r * rowH);
            NoRaycast(cell);

            var dot = s.Panel("d", cell, s.ArchColor(i, items[i].Key));
            dot.anchorMin = dot.anchorMax = new Vector2(0f, 0.5f);
            dot.pivot = new Vector2(0f, 0.5f);
            dot.sizeDelta = new Vector2(8f, 8f);
            dot.anchoredPosition = new Vector2(pad, 0f);
            s.Round(dot);

            Color nameCol = items[i].Key == "Other" ? s.Muted : s.Ink;
            var nm = s.Text("n", cell, items[i].Key, 11, nameCol, TextAnchor.MiddleLeft, null);
            // Auto-shrink long archetype names ("The Seven Warlords of the Sea", …) so they fit
            // inside their half-width cell instead of overflowing into the next column.
            nm.horizontalOverflow = HorizontalWrapMode.Wrap;
            nm.verticalOverflow = VerticalWrapMode.Truncate;
            nm.resizeTextForBestFit = true;
            nm.resizeTextMinSize = 7;
            nm.resizeTextMaxSize = 11;
            s.Stretch(nm.rectTransform, Vector2.zero, Vector2.one, new Vector2(pad + 13f, 0f), new Vector2(-22f, 0f));

            var vv = s.Text("v", cell, items[i].Value.ToString(), 10,
                items[i].Key == "Other" ? s.Muted : s.Accent2, TextAnchor.MiddleRight, s.Mono);
            vv.fontStyle = FontStyle.Bold;
            s.Stretch(vv.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-2f, 0f));
        }

        return y - (rows * rowH + 4f);
    }

    private static void NoRaycast(RectTransform rt)
    {
        var img = rt != null ? rt.GetComponent<Image>() : null;
        if (img != null) img.raycastTarget = false;
    }
}
