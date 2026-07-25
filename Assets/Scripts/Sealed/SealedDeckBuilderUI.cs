// One Piece TCG — Sealed / Pre-Release: the deck builder.
//
// This is NOT the constructed deck builder. Sealed needs a different tool: you are working from a
// fixed 72-card pool rather than the whole game, the deck is 40 not 50, there is no colour rule and
// no copy limit, and the questions you ask are limited-format ones — "how many Counters do I have",
// "what does my curve look like", "what actually came out of pack 4".
//
// All of the decisions about WHICH cards show, in what order, under what headings live in
// SealedPoolView (pure C#, tested headlessly). This file owns pixels only: chips, grid, stats,
// validation, timer.
//
// Card interaction matches the spec:
//   click        add one          right-click   remove one
//   double-click add every copy   shift-click   move all
//   hover        zoom preview

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sealed
{
    public sealed class SealedDeckBuilderUI : MonoBehaviour
    {
        private RectTransform root, body;
        private SealedPool pool;
        private SealedPoolView view;
        private List<SealedFilter> chips;
        private Action<SealedPool> onDone;
        private readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

        private RectTransform preview;
        private float deadlineUnscaled = -1f;      // build timer; -1 = untimed
        private Text timerText;

        public void Begin(RectTransform parent, SealedPool sealedPool, Action<SealedPool> done, int buildSeconds = 0)
        {
            root = parent;
            pool = sealedPool;
            onDone = done;
            view = new SealedPoolView();
            chips = SealedFilters.All();
            if (buildSeconds > 0) deadlineUnscaled = Time.unscaledTime + buildSeconds;
            Render();
        }

        private void Update()
        {
            if (deadlineUnscaled < 0f || timerText == null) return;
            float left = Mathf.Max(0f, deadlineUnscaled - Time.unscaledTime);
            timerText.text = $"{(int)(left / 60):00}:{(int)(left % 60):00}";
            timerText.color = left <= 300f ? SealedUI.Bad : left <= 600f ? SealedUI.Gold : SealedUI.Ink;
            if (left <= 0f) { deadlineUnscaled = -1f; Finish(); }
        }

        // ---- Layout --------------------------------------------------------------------------

        private void Render()
        {
            if (body != null) Destroy(body.gameObject);
            body = SealedUI.Panel(root, "Sealed Builder", SealedUI.PanelBg);
            SealedUI.Fill(body);

            BuildToolbar();
            BuildFilterRail();
            BuildGrid();
            BuildStatsPanel();
        }

        private void BuildToolbar()
        {
            var bar = SealedUI.Panel(body, "Toolbar", SealedUI.PanelBg2);
            SealedUI.Stretch(bar, new Vector2(0f, 0.94f), new Vector2(1f, 1f));

            var title = SealedUI.Label(bar, "Title",
                $"SEALED · {pool.SetCode} · seed {pool.Seed}", 14, SealedUI.Accent, TextAnchor.MiddleLeft, true);
            SealedUI.Stretch(title.rectTransform, new Vector2(0.01f, 0f), new Vector2(0.28f, 1f));

            // Search
            var field = NewInput(bar, new Vector2(0.29f, 0.15f), new Vector2(0.46f, 0.85f), "Search...");
            field.onValueChanged.AddListener(v => { view.Search = v; RefreshGrid(); });

            // Sort cycle
            SealedUI.Button(bar, "SORT: " + view.Sort, SealedUI.ChipOff, SealedUI.Ink, () =>
            {
                var all = (SealedSort[])Enum.GetValues(typeof(SealedSort));
                view.Sort = all[(Array.IndexOf(all, view.Sort) + 1) % all.Length];
                Render();
            }).let(rt => SealedUI.Stretch(rt, new Vector2(0.47f, 0.15f), new Vector2(0.60f, 0.85f)));

            // Quick view cycle
            SealedUI.Button(bar, "VIEW: " + view.QuickView, SealedUI.ChipOff, SealedUI.Ink, () =>
            {
                var all = (SealedQuickView[])Enum.GetValues(typeof(SealedQuickView));
                view.QuickView = all[(Array.IndexOf(all, view.QuickView) + 1) % all.Length];
                Render();
            }).let(rt => SealedUI.Stretch(rt, new Vector2(0.61f, 0.15f), new Vector2(0.74f, 0.85f)));

            SealedUI.Button(bar, "RESET", SealedUI.ChipOff, SealedUI.Ink, () => { view.Reset(); Render(); })
                .let(rt => SealedUI.Stretch(rt, new Vector2(0.75f, 0.15f), new Vector2(0.83f, 0.85f)));

            // Build timer (tournament mode only)
            if (deadlineUnscaled >= 0f)
            {
                timerText = SealedUI.Label(bar, "Timer", "--:--", 18, SealedUI.Ink, TextAnchor.MiddleCenter, true);
                SealedUI.Stretch(timerText.rectTransform, new Vector2(0.84f, 0f), new Vector2(0.93f, 1f));
            }

            SealedUI.Button(bar, "DONE", SealedUI.Accent, SealedUI.BadgeInk, Finish)
                .let(rt => SealedUI.Stretch(rt, new Vector2(0.94f, 0.15f), new Vector2(0.99f, 0.85f)));
        }

        private void BuildFilterRail()
        {
            var rail = SealedUI.Panel(body, "Filters", SealedUI.PanelBg2);
            SealedUI.Stretch(rail, new Vector2(0f, 0f), new Vector2(0.17f, 0.94f));
            var col = SealedUI.ScrollColumn(rail, "Filter");
            SealedUI.Stretch(col.parent as RectTransform, new Vector2(0.03f, 0.01f), new Vector2(0.97f, 0.99f));

            SealedUI.Label(col, "H", "FILTERS", 11, SealedUI.Muted, TextAnchor.MiddleLeft, true)
                .rectTransform.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            string lastGroup = null;
            RectTransform row = null;
            foreach (var chip in chips)
            {
                if (chip.Group != lastGroup)
                {
                    lastGroup = chip.Group;
                    row = SealedUI.ChipRow(col, chip.Group + " row");
                    var le = row.gameObject.AddComponent<LayoutElement>();
                    le.preferredHeight = 26f * Mathf.Ceil(chips.Count(c => c.Group == chip.Group) / 2f);
                }
                var captured = chip;
                SealedUI.Chip(row, chip.Label, view.IsActive(chip), () => { view.Toggle(captured); Render(); });
            }

            // Deck-only toggle sits with the filters because that is how it reads to a player.
            var deckOnly = SealedUI.Chip(col, view.DeckOnly ? "IN DECK ✓" : "IN DECK", view.DeckOnly,
                () => { view.DeckOnly = !view.DeckOnly; Render(); });
            deckOnly.gameObject.GetComponent<LayoutElement>().preferredHeight = 24f;
        }

        private RectTransform gridHost;

        private void BuildGrid()
        {
            var host = SealedUI.Panel(body, "Pool", new Color(0, 0, 0, 0));
            SealedUI.Stretch(host, new Vector2(0.17f, 0f), new Vector2(0.76f, 0.94f));
            gridHost = SealedUI.ScrollColumn(host, "Pool", 6f);
            SealedUI.Stretch(gridHost.parent as RectTransform, new Vector2(0.01f, 0.01f), new Vector2(0.99f, 0.99f));
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            if (gridHost == null) return;
            for (int i = gridHost.childCount - 1; i >= 0; i--) Destroy(gridHost.GetChild(i).gameObject);

            var groups = view.Build(pool, chips);
            if (groups.Count == 0 || groups.All(g => g.Count == 0))
            {
                var none = SealedUI.Label(gridHost, "Empty", "No cards match those filters.", 13, SealedUI.Muted);
                none.rectTransform.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
                return;
            }

            foreach (var group in groups)
            {
                if (!string.IsNullOrEmpty(group.Heading))
                {
                    var h = SealedUI.Label(gridHost, "H " + group.Heading,
                        $"{group.Heading.ToUpperInvariant()}  ({group.Count})", 11, SealedUI.Accent, TextAnchor.MiddleLeft, true);
                    h.rectTransform.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
                }

                var grid = new GameObject("Grid", typeof(RectTransform)).GetComponent<RectTransform>();
                grid.SetParent(gridHost, false);
                var g = grid.gameObject.AddComponent<GridLayoutGroup>();
                g.cellSize = new Vector2(84f, 118f);
                g.spacing = new Vector2(6f, 6f);
                g.childAlignment = TextAnchor.UpperLeft;
                int cols = 8;
                int rows = Mathf.CeilToInt(group.Count / (float)cols);
                grid.gameObject.AddComponent<LayoutElement>().preferredHeight = rows * 124f;

                foreach (var id in group.CardIds) AddCardCell(grid, id);
            }
        }

        private void AddCardCell(RectTransform parent, string cardId)
        {
            var def = CardData.GetCard(cardId);
            int inPool = pool.InPool(cardId);
            int inDeck = pool.InDeck(cardId);
            bool exhausted = pool.Remaining(cardId) <= 0;

            var cell = SealedUI.Panel(parent, cardId, new Color32(24, 36, 52, 255), raycast: true);
            var img = cell.GetComponent<Image>();
            var sprite = GetSprite(cardId);
            if (sprite != null) { img.sprite = sprite; img.color = exhausted ? new Color(0.45f, 0.45f, 0.5f) : Color.white; img.preserveAspect = true; }
            else
            {
                var t = SealedUI.Label(cell, "N", $"{def?.Name ?? cardId}\n{def?.Cost}c", 9, SealedUI.Ink, TextAnchor.MiddleCenter);
                SealedUI.Stretch(t.rectTransform, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f));
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
            }

            // Pool / deck counts, always visible — no guessing how many are left.
            SealedUI.CornerBadge(cell, inPool.ToString(), new Vector2(0f, 1f), new Color32(40, 54, 72, 235), SealedUI.Muted);
            if (inDeck > 0)
                SealedUI.CornerBadge(cell, inDeck.ToString(), new Vector2(1f, 1f), SealedUI.Accent, SealedUI.BadgeInk);

            var click = cell.gameObject.AddComponent<SealedUI.ClickRouter>();
            click.OnLeft = () =>
            {
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (shift) pool.AddAll(cardId); else pool.Add(cardId);
                RefreshGrid(); RefreshStats();
            };
            click.OnDoubleLeft = () => { pool.AddAll(cardId); RefreshGrid(); RefreshStats(); };
            click.OnRight = () =>
            {
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (shift) pool.RemoveAll(cardId); else pool.Remove(cardId);
                RefreshGrid(); RefreshStats();
            };

            var hover = cell.gameObject.AddComponent<SealedUI.HoverRouter>();
            hover.OnEnter = () => ShowPreview(cardId);
            hover.OnExit = HidePreview;
        }

        // ---- Stats panel ---------------------------------------------------------------------

        private RectTransform statsHost;

        private void BuildStatsPanel()
        {
            var panel = SealedUI.Panel(body, "Stats", SealedUI.PanelBg2);
            SealedUI.Stretch(panel, new Vector2(0.76f, 0f), new Vector2(1f, 0.94f));
            statsHost = SealedUI.ScrollColumn(panel, "Stats", 3f);
            SealedUI.Stretch(statsHost.parent as RectTransform, new Vector2(0.05f, 0.01f), new Vector2(0.95f, 0.99f));
            RefreshStats();
        }

        private void RefreshStats()
        {
            if (statsHost == null) return;
            for (int i = statsHost.childCount - 1; i >= 0; i--) Destroy(statsHost.GetChild(i).gameObject);

            var v = pool.Validate();
            var s = pool.DeckStats();

            Row($"DECK  {v.MainCount} / {v.RequiredCount}", v.MainCount == v.RequiredCount ? SealedUI.Good : SealedUI.Ink, 15, true);

            // Leader
            string leaderName = string.IsNullOrEmpty(pool.LeaderId)
                ? "— none —"
                : (SealedLeaderRules.IsRainbowLuffy(pool.LeaderId)
                    ? "Rainbow Luffy"
                    : CardData.GetCard(pool.LeaderId)?.Name ?? pool.LeaderId);
            Row($"Leader: {leaderName}", v.LeaderLegal && v.HasLeader ? SealedUI.Good : SealedUI.Bad, 12);
            Row($"Format: {SealedLeaderRules.ModeName(pool.LeaderMode)}", SealedUI.Muted, 10);

            Gap();
            Row("COMPOSITION", SealedUI.Accent, 11, true);
            Row($"Characters   {s.Characters}", SealedUI.Ink, 12);
            Row($"Events       {s.Events}", SealedUI.Ink, 12);
            Row($"Stages       {s.Stages}", SealedUI.Ink, 12);
            Row($"Avg. cost    {s.AverageCost:F2}", SealedUI.Ink, 12);

            Gap();
            Row("COUNTER", SealedUI.Accent, 11, true);
            Row($"No Counter   {s.NoCounter}", s.NoCounter > 20 ? SealedUI.Bad : SealedUI.Ink, 12);
            Row($"1000         {s.Counter1000}", SealedUI.Ink, 12);
            Row($"2000         {s.Counter2000}", SealedUI.Ink, 12);

            Gap();
            Row("KEYWORDS", SealedUI.Accent, 11, true);
            Row($"Blockers     {s.Blockers}", SealedUI.Ink, 12);
            Row($"Triggers     {s.Triggers}", SealedUI.Ink, 12);
            if (s.Rush > 0) Row($"Rush         {s.Rush}", SealedUI.Ink, 12);
            if (s.DoubleAttack > 0) Row($"Dbl Attack   {s.DoubleAttack}", SealedUI.Ink, 12);

            Gap();
            Row("CURVE", SealedUI.Accent, 11, true);
            for (int c = 0; c <= 6; c++)
            {
                int n = s.Curve.TryGetValue(c, out var cv) ? cv : 0;
                Row($"{(c == 6 ? "6+" : c.ToString())}  {new string('█', Mathf.Min(14, n))} {n}", SealedUI.Muted, 11);
            }

            if (s.Colors.Count > 0)
            {
                Gap();
                Row("COLORS", SealedUI.Accent, 11, true);
                foreach (var kv in s.Colors.OrderByDescending(k => k.Value))
                    Row($"{kv.Key,-8} {kv.Value}", SealedUI.Ink, 12);
            }

            if (!v.Ok)
            {
                Gap();
                Row("TO FIX", SealedUI.Bad, 11, true);
                foreach (var p in v.Problems.Take(4)) Row("• " + p, SealedUI.Bad, 11);
            }

            void Row(string text, Color colour, int size, bool bold = false)
            {
                var t = SealedUI.Label(statsHost, "R", text, size, colour, TextAnchor.MiddleLeft, bold);
                t.rectTransform.gameObject.AddComponent<LayoutElement>().preferredHeight = size + 6f;
            }
            void Gap()
            {
                var g = SealedUI.Panel(statsHost, "Gap", new Color(0, 0, 0, 0));
                g.gameObject.AddComponent<LayoutElement>().preferredHeight = 8f;
            }
        }

        // ---- Preview -------------------------------------------------------------------------

        private void ShowPreview(string cardId)
        {
            HidePreview();
            var sprite = GetSprite(cardId);
            preview = SealedUI.Panel(root, "Preview", sprite != null ? Color.white : new Color32(24, 36, 52, 250));
            preview.anchorMin = preview.anchorMax = new Vector2(0.5f, 0.5f);
            preview.pivot = new Vector2(0.5f, 0.5f);
            preview.sizeDelta = new Vector2(300f, 420f);
            preview.SetAsLastSibling();
            var img = preview.GetComponent<Image>();
            if (sprite != null) { img.sprite = sprite; img.preserveAspect = true; }
            else
            {
                var def = CardData.GetCard(cardId);
                var t = SealedUI.Label(preview, "N",
                    $"{def?.Name}\n\n{def?.Effect}", 13, SealedUI.Ink, TextAnchor.UpperCenter);
                SealedUI.Stretch(t.rectTransform, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.94f));
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        private void HidePreview()
        {
            if (preview != null) { Destroy(preview.gameObject); preview = null; }
        }

        // ---- Art / exit ----------------------------------------------------------------------

        private Sprite GetSprite(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            if (spriteCache.TryGetValue(cardId, out var s)) return s;
            spriteCache[cardId] = null;
            KickLoad(cardId);
            return null;
        }

        private async void KickLoad(string cardId)
        {
            try
            {
                while (!CardAssets.Ready) await System.Threading.Tasks.Task.Yield();
                var rel = CardAssets.FirstExisting(CardAssets.ArtCandidates(cardId));
                if (string.IsNullOrEmpty(rel)) return;
                var bytes = await CardAssets.ReadBytesAsync(rel);
                if (bytes == null || bytes.Length == 0) return;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes)) return;
                spriteCache[cardId] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception e) { Debug.LogWarning($"[SealedBuilder] art load failed for {cardId}: {e.Message}"); }
        }

        private InputField NewInput(RectTransform parent, Vector2 min, Vector2 max, string placeholder)
        {
            var go = new GameObject("Search", typeof(RectTransform), typeof(Image), typeof(InputField));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            SealedUI.Stretch(rt, min, max);
            go.GetComponent<Image>().color = new Color32(20, 34, 50, 235);
            var f = go.GetComponent<InputField>();
            var ph = SealedUI.Label(rt, "PH", placeholder, 12, SealedUI.Muted);
            var tx = SealedUI.Label(rt, "TX", "", 12, SealedUI.Ink);
            SealedUI.Stretch(ph.rectTransform, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f));
            SealedUI.Stretch(tx.rectTransform, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f));
            f.textComponent = tx;
            f.placeholder = ph;
            return f;
        }

        private void Finish()
        {
            HidePreview();
            if (body != null) Destroy(body.gameObject);
            onDone?.Invoke(pool);
        }
    }

    internal static class RectTransformChain
    {
        /// <summary>Tiny fluent helper so a Button(...) call can be positioned inline.</summary>
        public static RectTransform let(this RectTransform rt, Action<RectTransform> act) { act?.Invoke(rt); return rt; }
    }
}
